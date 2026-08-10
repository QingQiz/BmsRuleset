using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

internal readonly record struct BmsTrackLoadResult(ushort SampleKey, Track? Track);

/// <summary>
///     Owns BMS sample Track instances, their loading tasks, and the chart-driven preload window.
/// </summary>
internal sealed class BmsSampleTrackRegistry : IDisposable
{
    private static readonly TimeSpan track_load_timeout = TimeSpan.FromSeconds(30);
    private const int track_load_batch_size = 16;
    private const double track_prefetch_time = 10_000;

    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly IReadOnlyList<BmsSampleUsage>? sampleUsages;
    private readonly string? basePath;
    private readonly double rate;
    private readonly CancellationTokenSource runtimeLoadCancellation = new();

    private readonly Dictionary<ushort, Track> tracks = [];
    private readonly Dictionary<ushort, Task<Track?>> trackInitialisations = [];
    private readonly Dictionary<ushort, List<Track>> additionalTracks = [];
    private readonly Dictionary<ushort, List<Task<Track?>>> additionalTrackInitialisations = [];
    private readonly Dictionary<ushort, int> requiredTrackCounts = [];

    private SampleLifetime[]? sampleLifetimes;
    private int nextLifetimeIndex;
    private ITrackStore? trackStore;
    private BmsAudioResourceStore? audioResourceStore;
    private bool disposed;

    public double MaxTrackLengthMilliseconds => tracks.Count == 0 ? 0 : tracks.Values.Max(track => track.Length);

    public BmsSampleTrackRegistry(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<BmsSampleUsage>? sampleUsages)
    {
        this.sampleDefinitions = sampleDefinitions
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        this.sampleUsages = sampleUsages?.ToArray();
        this.basePath = basePath;
        this.rate = rate;
    }

    public static int SelectMaxConcurrentTrackCount(
        IEnumerable<BmsSampleUsage> usages,
        double trackLengthMilliseconds)
    {
        var intervals = usages
            .Select(usage => (Start: usage.EarliestTriggerTime, End: usage.LatestTriggerTime + trackLengthMilliseconds))
            .OrderBy(interval => interval.Start)
            .ToArray();

        if (intervals.Length == 0)
            return 0;

        if (trackLengthMilliseconds <= 0)
            return 1;

        PriorityQueue<double, double> activeEnds = new();
        var maxConcurrent = 0;

        foreach (var interval in intervals)
        {
            // A voice ending exactly at the next hit can be reused without overlap.
            while (activeEnds.TryPeek(out _, out var endTime) && endTime <= interval.Start)
                activeEnds.Dequeue();

            var end = interval.End;
            activeEnds.Enqueue(end, end);
            maxConcurrent = Math.Max(maxConcurrent, activeEnds.Count);
        }

        return maxConcurrent;
    }

    public bool HasSampleDefinition(ushort sampleKey) => sampleDefinitions.ContainsKey(sampleKey);

    public Track? GetTrack(ushort sampleKey) => tracks.GetValueOrDefault(sampleKey);

    public double GetTrackLength(ushort sampleKey) => GetTrack(sampleKey)?.Length ?? 0;

    public IEnumerable<Track> GetTracks(ushort sampleKey)
    {
        if (tracks.TryGetValue(sampleKey, out var track))
            yield return track;

        if (additionalTracks.TryGetValue(sampleKey, out var candidates))
        {
            foreach (var candidate in candidates)
                yield return candidate;
        }
    }

    public bool IsInitialising(ushort sampleKey) => trackInitialisations.ContainsKey(sampleKey);

    public void Initialise(AudioManager audioManager, AudioMixer? mixer, CancellationToken cancellationToken, double currentTime)
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return;

        audioResourceStore = new BmsAudioResourceStore(basePath, runtimeLoadCancellation.Token);
        var resources = new ResourceStore<byte[]>(audioResourceStore);
        BmsAudioResourceStore.AddExtensions(resources);
        trackStore = audioManager.GetTrackStore(resources, mixer);

        if (sampleUsages != null)
        {
            sampleLifetimes = sampleUsages
                .Where(usage => sampleDefinitions.ContainsKey(usage.SampleKey))
                .GroupBy(usage => usage.SampleKey)
                .Select(group => new SampleLifetime(group.Key, group.Min(usage => usage.EarliestTriggerTime) - track_prefetch_time))
                .OrderBy(lifetime => lifetime.LifetimeStart)
                .ToArray();

            while (nextLifetimeIndex < sampleLifetimes.Length
                   && sampleLifetimes[nextLifetimeIndex].LifetimeStart <= currentTime)
            {
                nextLifetimeIndex++;
            }
        }

        var initialKeys = sampleLifetimes == null
            ? sampleDefinitions.Keys
            : sampleLifetimes.Take(nextLifetimeIndex).Select(lifetime => lifetime.SampleKey);
        preloadTracks(initialKeys, cancellationToken);
    }

    public void EnsureTrackLoaded(ushort sampleKey)
    {
        if (tracks.ContainsKey(sampleKey) || trackInitialisations.ContainsKey(sampleKey))
            return;

        trackInitialisations[sampleKey] = loadTrackAsync(sampleKey, runtimeLoadCancellation.Token);
    }

    public BmsTrackLoadResult[] Update(double currentTime)
    {
        List<BmsTrackLoadResult> completed = [];

        foreach (var (sampleKey, task) in trackInitialisations.ToArray())
        {
            if (!task.IsCompleted)
                continue;

            trackInitialisations.Remove(sampleKey);

            if (task.IsCompletedSuccessfully && task.Result is { } track)
            {
                tracks[sampleKey] = track;
                requiredTrackCounts[sampleKey] = getRequiredTrackCount(sampleKey, track);
                ensureAdditionalTracksLoaded(sampleKey);
                completed.Add(new BmsTrackLoadResult(sampleKey, track));
                continue;
            }

            var trackName = tracks.GetValueOrDefault(sampleKey)?.Name ?? $"key {sampleKey:X2}";
            discardFailedTrack(sampleKey);
            BmsLogger.LogAudioFailure($"Failed to load BMS sample track {trackName}; this definition will be unavailable during gameplay.");
            completed.Add(new BmsTrackLoadResult(sampleKey, null));
        }

        completeAdditionalInitialisations();

        if (sampleLifetimes == null || trackStore == null)
            return completed.ToArray();

        var loadsStarted = 0;

        while (nextLifetimeIndex < sampleLifetimes.Length && loadsStarted < track_load_batch_size)
        {
            var lifetime = sampleLifetimes[nextLifetimeIndex];

            if (lifetime.LifetimeStart > currentTime)
                break;

            nextLifetimeIndex++;

            if (tracks.ContainsKey(lifetime.SampleKey))
                continue;

            EnsureTrackLoaded(lifetime.SampleKey);
            loadsStarted++;
        }

        return completed.ToArray();
    }

    private void completeAdditionalInitialisations()
    {
        foreach (var (sampleKey, initialisations) in additionalTrackInitialisations.ToArray())
        {
            var completed = initialisations.Where(task => task.IsCompleted).ToArray();

            foreach (var task in completed)
            {
                initialisations.Remove(task);

                if (task.IsCompletedSuccessfully && task.Result is { } track)
                {
                    if (!additionalTracks.TryGetValue(sampleKey, out var candidates))
                        additionalTracks[sampleKey] = candidates = [];

                    candidates.Add(track);
                }
            }

            if (initialisations.Count == 0)
                additionalTrackInitialisations.Remove(sampleKey);
        }
    }

    private void ensureAdditionalTracksLoaded(ushort sampleKey)
    {
        if (!BmsKeysoundMixerPatcher.IsInstalled
            || requiredTrackCounts.GetValueOrDefault(sampleKey) < 2)
            return;

        var loadedCount = additionalTracks.GetValueOrDefault(sampleKey)?.Count ?? 0;
        var pendingCount = additionalTrackInitialisations.GetValueOrDefault(sampleKey)?.Count ?? 0;
        var additionalCount = requiredTrackCounts[sampleKey] - 1 - loadedCount - pendingCount;

        if (additionalCount <= 0)
            return;

        if (!additionalTrackInitialisations.TryGetValue(sampleKey, out var initialisations))
            additionalTrackInitialisations[sampleKey] = initialisations = [];

        for (var i = 0; i < additionalCount; i++)
            initialisations.Add(loadTrackAsync(sampleKey, runtimeLoadCancellation.Token));
    }

    private async Task<Track?> loadTrackAsync(ushort sampleKey, CancellationToken cancellationToken)
    {
        if (trackStore == null || !sampleDefinitions.TryGetValue(sampleKey, out var path))
            return null;

        Track? track = null;

        try
        {
            foreach (var lookup in new BmsSampleInfo(path).LookupNames)
            {
                using (BmsTrackAudioPatcher.EnterBmsTrackScope())
                    track = await trackStore.GetAsync(lookup, cancellationToken).ConfigureAwait(false);

                if (track == null)
                    continue;

                configureTrack(track);

                if (!await track.SeekAsync(0).ConfigureAwait(false) || !track.IsLoaded)
                {
                    track.Dispose();
                    return null;
                }

                cancellationToken.ThrowIfCancellationRequested();
                return track;
            }
        }
        catch (OperationCanceledException)
        {
            track?.Dispose();
        }
        catch (Exception exception)
        {
            track?.Dispose();

            if (!cancellationToken.IsCancellationRequested)
                BmsLogger.LogAudioFailure($"Failed to initialise BMS sample key {sampleKey:X2}.", exception);
        }

        return null;
    }

    private Task<bool>? tryCreateTrack(ushort sampleKey)
    {
        if (trackStore == null || !sampleDefinitions.TryGetValue(sampleKey, out var path))
            return null;

        foreach (var lookup in new BmsSampleInfo(path).LookupNames)
        {
            Track? track;
            using (BmsTrackAudioPatcher.EnterBmsTrackScope())
                track = trackStore.Get(lookup);

            if (track == null)
                continue;

            configureTrack(track);
            tracks[sampleKey] = track;
            requiredTrackCounts[sampleKey] = getRequiredTrackCount(sampleKey, track);

            List<Track> additional = [];

            if (BmsKeysoundMixerPatcher.IsInstalled)
            {
                for (var i = 1; i < requiredTrackCounts[sampleKey]; i++)
                {
                    using (BmsTrackAudioPatcher.EnterBmsTrackScope())
                    {
                        var additionalTrack = trackStore.Get(lookup);

                        if (additionalTrack == null)
                            break;

                        configureTrack(additionalTrack);
                        additional.Add(additionalTrack);
                    }
                }
            }

            if (additional.Count > 0)
                additionalTracks[sampleKey] = additional;

            return prepareTracks(track, additional);
        }

        return null;
    }

    private async Task<bool> prepareTracks(Track track, List<Track> additionalTracksForKey)
    {
        if (!await track.SeekAsync(0).ConfigureAwait(false) || !track.IsLoaded)
            return false;

        for (var i = additionalTracksForKey.Count - 1; i >= 0; i--)
        {
            var additionalTrack = additionalTracksForKey[i];

            if (await additionalTrack.SeekAsync(0).ConfigureAwait(false) && additionalTrack.IsLoaded)
                continue;

            additionalTracksForKey.RemoveAt(i);
            additionalTrack.Dispose();
        }

        return true;
    }

    private void configureTrack(Track track)
    {
        if (Math.Abs(rate - 1.0) > 0.001)
            track.AddAdjustment(AdjustableProperty.Tempo, new BindableDouble(rate));
    }

    private int getRequiredTrackCount(ushort sampleKey, Track track)
    {
        if (sampleUsages == null || !BmsKeysoundMixerPatcher.IsInstalled)
            return 1;

        return SelectMaxConcurrentTrackCount(
            sampleUsages.Where(usage => usage.SampleKey == sampleKey),
            track.Length / Math.Max(rate, 0.01));
    }

    private void discardFailedTrack(ushort sampleKey)
    {
        requiredTrackCounts.Remove(sampleKey);

        if (!tracks.Remove(sampleKey, out var track))
            return;

        track.Dispose();

        if (additionalTracks.Remove(sampleKey, out var candidates))
        {
            foreach (var candidate in candidates)
                candidate.Dispose();
        }
    }

    private void preloadTracks(IEnumerable<ushort> sampleKeys, CancellationToken cancellationToken)
    {
        Dictionary<ushort, Task<bool>> tasks = [];
        var stopwatch = Stopwatch.StartNew();

        foreach (var sampleKey in sampleKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopwatch.Elapsed >= track_load_timeout)
                break;

            if (tryCreateTrack(sampleKey) is { } task)
                tasks[sampleKey] = task;

            if (tasks.Count < track_load_batch_size)
                continue;

            waitForTracks(tasks, cancellationToken, stopwatch);
            tasks.Clear();
        }

        waitForTracks(tasks, cancellationToken, stopwatch);
    }

    private void waitForTracks(IReadOnlyDictionary<ushort, Task<bool>> tasks, CancellationToken cancellationToken, Stopwatch stopwatch)
    {
        while (tasks.Values.Any(task => !task.IsCompleted) && stopwatch.Elapsed < track_load_timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(10);
        }

        foreach (var (sampleKey, task) in tasks)
        {
            var track = tracks[sampleKey];

            if (task.IsCompletedSuccessfully && task.Result && track.IsLoaded)
                continue;

            BmsLogger.LogAudioFailure($"{(task.IsCompleted ? "Failed" : "Timed out while loading")} BMS sample track {track.Name}; this definition will be unavailable during gameplay.");
            discardFailedTrack(sampleKey);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        runtimeLoadCancellation.Cancel();

        disposeInitialisations(trackInitialisations);
        disposeInitialisations(additionalTrackInitialisations);

        foreach (var track in tracks.Values.Concat(additionalTracks.Values.SelectMany(candidates => candidates)).Distinct())
            track.Dispose();

        tracks.Clear();
        additionalTracks.Clear();
        requiredTrackCounts.Clear();
        trackStore?.Dispose();
        trackStore = null;
        audioResourceStore?.Dispose();
        audioResourceStore = null;
        runtimeLoadCancellation.Dispose();
    }

    private static void disposeInitialisations(Dictionary<ushort, Task<Track?>> initialisations)
    {
        foreach (var task in initialisations.Values)
            disposeInitialisation(task);

        initialisations.Clear();
    }

    private static void disposeInitialisations(Dictionary<ushort, List<Task<Track?>>> initialisations)
    {
        foreach (var taskList in initialisations.Values)
        {
            foreach (var task in taskList)
                disposeInitialisation(task);
        }

        initialisations.Clear();
    }

    private static void disposeInitialisation(Task<Track?> task)
    {
        if (task.IsCompletedSuccessfully)
            task.Result?.Dispose();
        else if (!task.IsCompleted)
            _ = task.ContinueWith(completedTask =>
            {
                if (completedTask.IsCompletedSuccessfully)
                    completedTask.Result?.Dispose();
                else
                    _ = completedTask.Exception;
            }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private readonly record struct SampleLifetime(ushort SampleKey, double LifetimeStart);
}
