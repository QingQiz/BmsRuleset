using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

public readonly record struct BmsSampleUsage(ushort SampleKey, double Time);

/// <summary>
///     Owns seekable <see cref="Track" /> instances for chart sample definitions.
/// </summary>
/// <remarks>
///     Track identity follows the BMS definition key rather than the resolved file path. Reusing a
///     key therefore truncates and restarts its existing playback, while distinct keys can overlap
///     even when they reference the same file. When sample usages are supplied, Tracks are loaded
///     shortly before their first scheduled use and retained until this store is disposed.
/// </remarks>
public partial class BmsSampleStore : Component
{
    private static readonly TimeSpan track_load_timeout = TimeSpan.FromSeconds(30);
    private const int track_load_batch_size = 16;
    private const double track_prefetch_time = 10_000;

    public double MaxTrackLengthMilliseconds => tracks.Count == 0 ? 0 : tracks.Values.Max(track => track.Length);

    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly IEnumerable<BmsSampleUsage>? sampleUsages;
    private readonly string? basePath;
    private readonly double rate;

    private SampleLifetime[]? sampleLifetimes;
    private int nextLifetimeIndex;

    private readonly Dictionary<ushort, Track> tracks = [];
    private readonly Dictionary<ushort, Task<Track?>> trackInitialisations = [];
    private readonly Dictionary<ushort, PendingPlay> pendingPlays = [];
    private readonly HashSet<ushort> activeKeys = [];
    private readonly HashSet<ushort> pausedKeys = [];
    private readonly Dictionary<ushort, TrackCommandQueue> commandQueues = [];
    private readonly object commandLock = new();
    private readonly CancellationTokenSource runtimeLoadCancellation = new();

    private ITrackStore? trackStore;
    private BmsAudioResourceStore? audioResourceStore;
    private bool playbackBlocked;
    private bool isDisposing;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public BmsSampleStore(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath = null,
        double rate = 1.0,
        IEnumerable<BmsSampleUsage>? sampleUsages = null)
    {
        this.sampleDefinitions = sampleDefinitions
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        this.sampleUsages = sampleUsages;
        this.basePath = basePath;
        this.rate = rate;
    }

    protected override void Dispose(bool isDisposing)
    {
        runtimeLoadCancellation.Cancel();

        lock (commandLock)
        {
            this.isDisposing = true;

            foreach (var queue in commandQueues.Values)
                queue.PendingCommand = null;
        }

        activeKeys.Clear();
        pausedKeys.Clear();
        commandQueues.Clear();
        pendingPlays.Clear();

        foreach (var task in trackInitialisations.Values)
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

        trackInitialisations.Clear();

        // Queue each owned track for native BASS cleanup directly. Relying only on the nested
        // TrackStore would defer this by an additional audio-collection update.
        foreach (var track in tracks.Values.Distinct())
            track.Dispose();

        tracks.Clear();
        trackStore?.Dispose();
        trackStore = null;
        audioResourceStore?.Dispose();
        audioResourceStore = null;
        runtimeLoadCancellation.Dispose();
        base.Dispose(isDisposing);
    }

    internal Track? GetTrack(ushort sampleKey) => tracks.GetValueOrDefault(sampleKey);

    internal double GetTrackLength(ushort sampleKey) => GetTrack(sampleKey)?.Length ?? 0;

    /// <summary>
    ///     Truncates any playback for <paramref name="sampleKey" /> and starts that key's Track at
    ///     <paramref name="offset" />. Other definition keys are unaffected.
    /// </summary>
    internal void Play(ushort sampleKey, int volume = 100, double offset = 0)
    {
        if (playbackBlocked || !sampleDefinitions.ContainsKey(sampleKey))
            return;

        offset = Math.Max(0, offset);
        ensureTrackLoaded(sampleKey);

        if (trackInitialisations.ContainsKey(sampleKey))
        {
            pendingPlays[sampleKey] = new PendingPlay(volume, offset, Time.Current);
            return;
        }

        if (!tracks.TryGetValue(sampleKey, out var track))
            return;

        playLoadedTrack(sampleKey, track, volume, offset);
    }

    private void playLoadedTrack(ushort sampleKey, Track track, int volume, double offset)
    {
        if (track.Length <= 0 || offset >= track.Length)
            return;

        bindTrackVolumeAdjustments(track, volume);
        pausedKeys.Remove(sampleKey);
        activeKeys.Add(sampleKey);
        queueCommand(sampleKey, new TrackCommand(TrackCommandType.Restart, offset));
    }

    internal void SetPlaybackBlocked(bool blocked)
    {
        if (blocked == playbackBlocked)
            return;

        playbackBlocked = blocked;

        if (playbackBlocked)
            PauseAll();
    }

    internal void PauseAll()
    {
        pausedKeys.Clear();

        foreach (var sampleKey in activeKeys)
        {
            queueCommand(sampleKey, new TrackCommand(TrackCommandType.Stop));
            pausedKeys.Add(sampleKey);
        }
    }

    internal void ResumeAll()
    {
        if (playbackBlocked)
            return;

        foreach (var sampleKey in pausedKeys)
            queueCommand(sampleKey, new TrackCommand(TrackCommandType.Start));

        pausedKeys.Clear();
    }

    internal void StopAll()
    {
        foreach (var sampleKey in activeKeys)
            queueCommand(sampleKey, new TrackCommand(TrackCommandType.Stop));

        activeKeys.Clear();
        pausedKeys.Clear();
    }

    protected override void Update()
    {
        base.Update();

        updateTrackInitialisations();
        activeKeys.RemoveWhere(sampleKey => !hasPendingCommand(sampleKey) && tracks[sampleKey].HasCompleted);

        if (sampleLifetimes == null || trackStore == null)
            return;

        var loadsStarted = 0;

        while (nextLifetimeIndex < sampleLifetimes.Length && loadsStarted < track_load_batch_size)
        {
            var lifetime = sampleLifetimes[nextLifetimeIndex];

            if (lifetime.LifetimeStart > Time.Current)
                break;

            nextLifetimeIndex++;

            if (tracks.ContainsKey(lifetime.SampleKey))
                continue;

            ensureTrackLoaded(lifetime.SampleKey);
            loadsStarted++;
        }
    }

    private void ensureTrackLoaded(ushort sampleKey)
    {
        if (tracks.ContainsKey(sampleKey) || trackInitialisations.ContainsKey(sampleKey))
            return;

        trackInitialisations[sampleKey] = loadTrackAsync(sampleKey, runtimeLoadCancellation.Token);
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
                BmsAudioLogger.LogLoadFailure($"Failed to initialise BMS sample key {sampleKey:X2}.", exception);
        }

        return null;
    }

    private Task<bool>? tryCreateTrack(ushort sampleKey)
    {
        if (trackStore == null || !sampleDefinitions.TryGetValue(sampleKey, out var path))
            return null;

        foreach (var lookup in new BmsSampleInfo(path).LookupNames)
        {
            var track = trackStore.Get(lookup);

            if (track == null)
                continue;

            configureTrack(track);

            tracks[sampleKey] = track;
            return track.SeekAsync(0);
        }

        return null;
    }

    private void configureTrack(Track track)
    {
        if (Math.Abs(rate - 1.0) > 0.001)
            track.AddAdjustment(AdjustableProperty.Tempo, new BindableDouble(rate));
    }

    private void updateTrackInitialisations()
    {
        foreach (var (sampleKey, task) in trackInitialisations.ToArray())
        {
            if (!task.IsCompleted)
                continue;

            trackInitialisations.Remove(sampleKey);

            if (task.IsCompletedSuccessfully && task.Result is { } track)
            {
                tracks[sampleKey] = track;

                if (pendingPlays.Remove(sampleKey, out var pendingPlay))
                {
                    var elapsed = Math.Max(0, Time.Current - pendingPlay.RequestedAt);
                    playLoadedTrack(sampleKey, track, pendingPlay.Volume, pendingPlay.Offset + elapsed);
                }

                continue;
            }

            pendingPlays.Remove(sampleKey);
            var trackName = tracks.GetValueOrDefault(sampleKey)?.Name ?? $"key {sampleKey:X2}";
            discardFailedTrack(sampleKey);
            BmsAudioLogger.LogLoadFailure($"Failed to load BMS sample track {trackName}; this definition will be unavailable during gameplay.");
        }
    }

    private void discardFailedTrack(ushort sampleKey)
    {
        if (!tracks.Remove(sampleKey, out var track))
            return;

        track.Dispose();
    }

    private void queueCommand(ushort sampleKey, TrackCommand command)
    {
        TrackCommandQueue queue;
        var startProcessing = false;

        lock (commandLock)
        {
            if (isDisposing)
                return;

            if (!commandQueues.TryGetValue(sampleKey, out queue!))
                commandQueues[sampleKey] = queue = new TrackCommandQueue(tracks[sampleKey]);

            // Once the audio thread falls behind, replaying every stale retrigger would produce
            // delayed duplicate sounds. BMS truncation semantics only require the latest request.
            queue.PendingCommand = command;

            if (!queue.IsProcessing)
            {
                queue.IsProcessing = true;
                startProcessing = true;
            }
        }

        if (startProcessing)
            _ = processCommands(queue, sampleKey);
    }

    private async Task processCommands(TrackCommandQueue queue, ushort sampleKey)
    {
        while (true)
        {
            TrackCommand command;

            lock (commandLock)
            {
                if (isDisposing || queue.PendingCommand is not { } pending)
                {
                    queue.IsProcessing = false;
                    return;
                }

                command = pending;
                queue.PendingCommand = null;
            }

            try
            {
                switch (command.Type)
                {
                    case TrackCommandType.Restart:
                        // Only one restart per Track can be in flight, so RestartPoint cannot be
                        // overwritten before the audio thread consumes this command.
                        queue.Track.RestartPoint = command.Offset;
                        await queue.Track.RestartAsync().ConfigureAwait(false);
                        break;

                    case TrackCommandType.Stop:
                        await queue.Track.StopAsync().ConfigureAwait(false);
                        break;

                    case TrackCommandType.Start:
                        await queue.Track.StartAsync().ConfigureAwait(false);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Failed to execute an audio command for BMS sample key {sampleKey:X2}.");
            }
        }
    }

    private bool hasPendingCommand(ushort sampleKey)
    {
        lock (commandLock)
            return commandQueues.TryGetValue(sampleKey, out var queue) && queue.IsProcessing;
    }

    private void bindTrackVolumeAdjustments(IAdjustableAudioComponent component, int volume)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));
        component.AddAdjustment(AdjustableProperty.Volume, audioManager.AggregateVolume);
    }

    [BackgroundDependencyLoader]
    private void load(CancellationToken? cancellationToken)
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return;

        audioResourceStore = new BmsAudioResourceStore(basePath, runtimeLoadCancellation.Token);
        var resources = new ResourceStore<byte[]>(audioResourceStore);
        BmsAudioResourceStore.AddExtensions(resources);
        trackStore = audioManager.GetTrackStore(resources);

        if (sampleUsages != null)
        {
            sampleLifetimes = sampleUsages
                .Where(usage => sampleDefinitions.ContainsKey(usage.SampleKey))
                .GroupBy(usage => usage.SampleKey)
                .Select(group => new SampleLifetime(group.Key, group.Min(usage => usage.Time) - track_prefetch_time))
                .OrderBy(lifetime => lifetime.LifetimeStart)
                .ToArray();
            while (nextLifetimeIndex < sampleLifetimes.Length
                   && sampleLifetimes[nextLifetimeIndex].LifetimeStart <= Time.Current)
            {
                nextLifetimeIndex++;
            }
        }

        // The first lifetime window must be ready before gameplay can leave its loading state.
        var initialKeys = sampleLifetimes == null
            ? sampleDefinitions.Keys
            : sampleLifetimes.Take(nextLifetimeIndex).Select(lifetime => lifetime.SampleKey);
        preloadTracks(initialKeys, cancellationToken ?? CancellationToken.None);
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

            // Bounded batches keep the audio update queue responsive enough to continue the
            // song-select preview while gameplay resources are prepared in the background.
            waitForTracks(tasks, cancellationToken, stopwatch);
            tasks.Clear();
        }

        // Dependency loaders are synchronous, so the initial window must finish before returning.
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

            BmsAudioLogger.LogLoadFailure(
                $"{(task.IsCompleted ? "Failed to load" : "Timed out while loading")} BMS sample track {track.Name}; this definition will be unavailable during gameplay.");
            discardFailedTrack(sampleKey);
        }
    }

    private enum TrackCommandType
    {
        Restart,
        Stop,
        Start,
    }

    private readonly record struct TrackCommand(TrackCommandType Type, double Offset = 0);

    private readonly record struct PendingPlay(int Volume, double Offset, double RequestedAt);

    private readonly record struct SampleLifetime(ushort SampleKey, double LifetimeStart);

    private sealed class TrackCommandQueue(Track track)
    {
        public readonly Track Track = track;

        public TrackCommand? PendingCommand;
        public bool IsProcessing;
    }
}
