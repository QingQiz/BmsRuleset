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

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Owns one preloaded, seekable <see cref="Track" /> for every chart sample definition.
/// </summary>
/// <remarks>
///     Track identity follows the BMS definition key rather than the resolved file path. Reusing a
///     key therefore truncates and restarts its existing playback, while distinct keys can overlap
///     even when they reference the same file.
/// </remarks>
public partial class BmsSampleStore : Component
{
    private static readonly TimeSpan track_load_timeout = TimeSpan.FromSeconds(30);

    public double MaxTrackLengthMilliseconds => tracks.Count == 0 ? 0 : tracks.Values.Max(track => track.Length);

    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly string? basePath;
    private readonly double rate;

    private readonly Dictionary<ushort, Track> tracks = [];
    private readonly HashSet<ushort> activeKeys = [];
    private readonly HashSet<ushort> pausedKeys = [];
    private readonly Dictionary<ushort, TrackCommandQueue> commandQueues = [];
    private readonly object commandLock = new();

    private ITrackStore? trackStore;
    private bool playbackBlocked;
    private bool isDisposing;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public BmsSampleStore(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath = null,
        double rate = 1.0)
    {
        this.sampleDefinitions = sampleDefinitions
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        this.basePath = basePath;
        this.rate = rate;
    }

    protected override void Dispose(bool isDisposing)
    {
        lock (commandLock)
        {
            this.isDisposing = true;

            foreach (var queue in commandQueues.Values)
                queue.PendingCommand = null;
        }

        activeKeys.Clear();
        pausedKeys.Clear();
        commandQueues.Clear();
        tracks.Clear();
        trackStore?.Dispose();
        trackStore = null;
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
        if (playbackBlocked || !tracks.TryGetValue(sampleKey, out var track))
            return;

        offset = Math.Max(0, offset);

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
        activeKeys.RemoveWhere(sampleKey => !hasPendingCommand(sampleKey) && tracks[sampleKey].HasCompleted);
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

        var resources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
        resources.AddExtension("wav");
        resources.AddExtension("mp3");
        resources.AddExtension("ogg");
        trackStore = audioManager.GetTrackStore(resources);

        foreach (var (sampleKey, path) in sampleDefinitions)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            foreach (var lookup in new BmsSampleInfo(path).LookupNames)
            {
                var track = trackStore.Get(lookup);

                if (track == null)
                    continue;

                if (Math.Abs(rate - 1.0) > 0.001)
                    track.AddAdjustment(AdjustableProperty.Tempo, new BindableDouble(rate));

                tracks[sampleKey] = track;
                break;
            }
        }

        // Dependency loaders are invoked synchronously, so returning a Task here would let the
        // drawable become loaded before the audio thread has initialised these Tracks.
        waitForTracks(cancellationToken ?? CancellationToken.None);
    }

    private void waitForTracks(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (tracks.Values.Any(track => !track.IsLoaded) && stopwatch.Elapsed < track_load_timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(10);
        }

        var failedKeys = tracks
            .Where(pair => !pair.Value.IsLoaded)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in failedKeys)
        {
            tracks[key].Dispose();
            tracks.Remove(key);
        }

        if (failedKeys.Length > 0)
            Logger.Log($"Timed out while loading {failedKeys.Length} BMS sample tracks; those definitions will be unavailable during gameplay.", LoggingTarget.Runtime, LogLevel.Important);
    }

    private enum TrackCommandType
    {
        Restart,
        Stop,
        Start,
    }

    private readonly record struct TrackCommand(TrackCommandType Type, double Offset = 0);

    private sealed class TrackCommandQueue(Track track)
    {
        public readonly Track Track = track;

        public TrackCommand? PendingCommand;
        public bool IsProcessing;
    }
}
