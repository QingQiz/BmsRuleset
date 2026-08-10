using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

/// <summary>
///     Owns BMS playback state and serialises Track operations onto the audio thread.
/// </summary>
internal sealed class BmsSamplePlaybackController : IDisposable
{
    private readonly BmsSampleTrackRegistry trackRegistry;
    private readonly AudioMixer? mixer;
    private readonly IBindable<double> aggregateVolume;
    private readonly double rate;
    private readonly Func<double> currentTime;
    private readonly object commandLock = new();
    private readonly Dictionary<ushort, PendingPlay> pendingPlays = [];
    private readonly HashSet<ushort> activeKeys = [];
    private readonly HashSet<ushort> pausedKeys = [];
    private readonly HashSet<Track> pausedTracks = [];
    private readonly Dictionary<Track, TrackCommandQueue> commandQueues = [];
    private readonly Dictionary<ushort, Track> currentTracks = [];
    private readonly Dictionary<Track, double> trackAvailableTimes = [];
    private readonly BmsLiveKeysoundBatcher liveBatcher;
    private bool playbackBlocked;
    private bool disposed;

    public bool SupportsScheduling => BmsKeysoundMixerPatcher.IsInstalled && BmsKeysoundMixerPatcher.EnableNativeScheduling;

    public BmsSamplePlaybackController(
        BmsSampleTrackRegistry trackRegistry,
        AudioMixer? mixer,
        IBindable<double> aggregateVolume,
        double rate,
        Func<double> currentTime)
    {
        this.trackRegistry = trackRegistry;
        this.mixer = mixer;
        this.aggregateVolume = aggregateVolume;
        this.rate = rate;
        this.currentTime = currentTime;
        liveBatcher = new BmsLiveKeysoundBatcher(
            mixer,
            commandLock,
            () => playbackBlocked,
            () => SupportsScheduling,
            prepareLivePlay,
            (sampleKey, volume) => Play(sampleKey, volume),
            markLiveTrackStopped);
    }

    public void QueueLivePlay(ushort sampleKey, int volume = 100)
    {
        if (playbackBlocked || !trackRegistry.HasSampleDefinition(sampleKey))
            return;

        liveBatcher.Queue(sampleKey, volume);
    }

    public void SubmitLivePlayBatch() => liveBatcher.Submit(currentTime());

    public void Play(ushort sampleKey, int volume = 100, double offset = 0)
    {
        if (playbackBlocked || !trackRegistry.HasSampleDefinition(sampleKey))
            return;

        offset = Math.Max(0, offset);
        trackRegistry.EnsureTrackLoaded(sampleKey);

        if (trackRegistry.IsInitialising(sampleKey))
        {
            pendingPlays[sampleKey] = new PendingPlay(volume, offset, currentTime());
            return;
        }

        if (trackRegistry.GetTrack(sampleKey) is { } track)
            playLoadedTrack(sampleKey, track, volume, offset);
    }

    public bool CanSchedule(ushort sampleKey) =>
        BmsKeysoundMixerPatcher.EnableNativeScheduling
        && BmsKeysoundMixerPatcher.IsInstalled
        && tryGetSchedulableTrack(sampleKey, out _);

    public string GetScheduleDiagnostic(ushort sampleKey)
    {
        var candidates = trackRegistry.GetTracks(sampleKey)
            .Select(track =>
            {
                lock (commandLock)
                {
                    var available = trackAvailableTimes.TryGetValue(track, out var availableAt)
                        ? $"available={availableAt:0.###}/{currentTime():0.###}"
                        : "available=none";
                    return $"running={track.IsRunning} completed={track.HasCompleted} pending={hasPendingCommand(track)} {available}";
                }
            });

        return string.Join(" | ", candidates);
    }

    public void SchedulePlay(ushort sampleKey, int volume, double delay, double targetTime)
    {
        if (delay <= 0 || !CanSchedule(sampleKey) || !tryGetSchedulableTrack(sampleKey, out var track))
        {
            Play(sampleKey, volume);
            return;
        }

        currentTracks.TryGetValue(sampleKey, out var trackToStop);
        // A voice prepared earlier in the same live batch is not audible yet. Stopping it would
        // erase a simultaneous same-key trigger before it reaches the mixer.
        trackToStop = trackToStop is { IsRunning: true } && trackToStop != track ? trackToStop : null;
        bindTrackVolumeAdjustments(track, volume);
        pausedKeys.Remove(sampleKey);
        activeKeys.Add(sampleKey);
        currentTracks[sampleKey] = track;
        var localTargetTime = currentTime() + delay;

        lock (commandLock)
            trackAvailableTimes[track] = localTargetTime + track.Length / rate;

        queueCommand(sampleKey, track, new TrackCommand(
            TrackCommandType.ScheduledRestart,
            Delay: delay,
            TargetTime: targetTime,
            ScheduledAtTimestamp: Stopwatch.GetTimestamp(),
            TrackToStop: trackToStop,
            TrackToStopAvailableAt: localTargetTime));
    }

    public void SetPlaybackBlocked(bool blocked)
    {
        if (blocked == playbackBlocked)
            return;

        playbackBlocked = blocked;

        if (playbackBlocked)
            PauseAll();
    }

    public void PauseAll()
    {
        liveBatcher.Clear();
        pausedKeys.Clear();

        foreach (var sampleKey in activeKeys)
        {
            foreach (var track in trackRegistry.GetTracks(sampleKey).Where(track => track.IsRunning))
            {
                queueCommand(sampleKey, track, new TrackCommand(TrackCommandType.Stop));
                pausedTracks.Add(track);
            }

            pausedKeys.Add(sampleKey);
        }
    }

    public void ResumeAll()
    {
        if (playbackBlocked)
            return;

        foreach (var sampleKey in pausedKeys)
        {
            foreach (var track in trackRegistry.GetTracks(sampleKey).Where(pausedTracks.Contains))
                queueCommand(sampleKey, track, new TrackCommand(TrackCommandType.Start));
        }

        pausedKeys.Clear();
        pausedTracks.Clear();
    }

    public void StopAll()
    {
        liveBatcher.Clear();
        BmsKeysoundMixerPatcher.ResetTimeline(mixer);

        foreach (var sampleKey in activeKeys)
            queueAllTracks(sampleKey, new TrackCommand(TrackCommandType.Stop));

        activeKeys.Clear();
        pausedKeys.Clear();
        pausedTracks.Clear();
        currentTracks.Clear();

        lock (commandLock)
            trackAvailableTimes.Clear();
    }

    public void Update(IEnumerable<BmsTrackLoadResult> completedLoads, double now)
    {
        foreach (var result in completedLoads)
        {
            if (result.Track != null)
            {
                if (pendingPlays.Remove(result.SampleKey, out var pendingPlay))
                {
                    var elapsed = Math.Max(0, now - pendingPlay.RequestedAt);
                    playLoadedTrack(result.SampleKey, result.Track, pendingPlay.Volume, pendingPlay.Offset + elapsed);
                }
            }
            else
                pendingPlays.Remove(result.SampleKey);
        }

        activeKeys.RemoveWhere(sampleKey => !hasPendingCommand(sampleKey)
                                            && trackRegistry.GetTracks(sampleKey).All(track => track.HasCompleted || !track.IsRunning));
    }

    private BmsPreparedLivePlay? prepareLivePlay(ushort sampleKey, int volume)
    {
        if (!BmsKeysoundMixerPatcher.EnableNativeScheduling
            || !BmsKeysoundMixerPatcher.IsInstalled
            || !tryGetSchedulableTrack(sampleKey, out var track))
            return null;

        currentTracks.TryGetValue(sampleKey, out var trackToStop);
        trackToStop = trackToStop is { IsRunning: true } && trackToStop != track ? trackToStop : null;
        bindTrackVolumeAdjustments(track, volume);
        pausedKeys.Remove(sampleKey);
        activeKeys.Add(sampleKey);
        currentTracks[sampleKey] = track;

        lock (commandLock)
            trackAvailableTimes[track] = currentTime() + track.Length / rate;

        return new BmsPreparedLivePlay(track, trackToStop);
    }

    private void markLiveTrackStopped(Track track, double stoppedAt)
    {
        lock (commandLock)
            trackAvailableTimes[track] = stoppedAt;
    }

    private void playLoadedTrack(ushort sampleKey, Track track, int volume, double offset)
    {
        if (track.Length <= 0 || offset >= track.Length)
            return;

        bindTrackVolumeAdjustments(track, volume);
        pausedKeys.Remove(sampleKey);
        activeKeys.Add(sampleKey);
        currentTracks[sampleKey] = track;

        lock (commandLock)
            trackAvailableTimes[track] = currentTime() + (track.Length - offset) / rate;

        foreach (var otherTrack in trackRegistry.GetTracks(sampleKey).Where(otherTrack => !ReferenceEquals(otherTrack, track) && otherTrack.IsRunning))
            queueCommand(sampleKey, otherTrack, new TrackCommand(TrackCommandType.Stop));

        queueCommand(sampleKey, track, new TrackCommand(TrackCommandType.Restart, offset));
    }

    private void queueCommand(ushort sampleKey, Track track, TrackCommand command)
    {
        TrackCommandQueue queue;
        var startProcessing = false;

        lock (commandLock)
        {
            if (disposed)
                return;

            if (!commandQueues.TryGetValue(track, out queue!))
                commandQueues[track] = queue = new TrackCommandQueue(track);

            if (command.Type is TrackCommandType.Stop or TrackCommandType.Restart)
                queue.PendingCommands.Clear();

            queue.PendingCommands.Enqueue(command);

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
                if (disposed || queue.PendingCommands.Count == 0)
                {
                    queue.IsProcessing = false;
                    return;
                }

                command = queue.PendingCommands.Dequeue();
            }

            try
            {
                switch (command.Type)
                {
                    case TrackCommandType.Restart:
                        if (!await BmsKeysoundMixerPatcher.TryRestartTrackAsync(mixer, queue.Track, command.Offset).ConfigureAwait(false))
                        {
                            await queue.Track.SeekAsync(command.Offset).ConfigureAwait(false);
                            BmsKeysoundMixerPatcher.TryApplyTailRamp(mixer, queue.Track, command.Offset);
                            await queue.Track.StartAsync().ConfigureAwait(false);
                        }

                        break;

                    case TrackCommandType.ScheduledRestart:
                        var elapsed = Stopwatch.GetElapsedTime(command.ScheduledAtTimestamp).TotalMilliseconds;
                        var remainingDelay = Math.Max(0, command.Delay - elapsed);
                        var scheduleResult = await BmsKeysoundMixerPatcher.TryScheduleTrackAsync(
                            mixer,
                            queue.Track,
                            command.TrackToStop,
                            remainingDelay,
                            command.TargetTime).ConfigureAwait(false);

                        if (scheduleResult.TrackStopped && command.TrackToStop != null)
                        {
                            lock (commandLock)
                                trackAvailableTimes[command.TrackToStop] = command.TrackToStopAvailableAt;
                        }
                        else if (scheduleResult.TrackScheduled && command.TrackToStop != null)
                            await command.TrackToStop.StopAsync().ConfigureAwait(false);

                        if (!scheduleResult.TrackScheduled)
                        {
                            if (!await BmsKeysoundMixerPatcher.TryRestartTrackAsync(mixer, queue.Track, 0).ConfigureAwait(false))
                            {
                                await queue.Track.SeekAsync(0).ConfigureAwait(false);
                                BmsKeysoundMixerPatcher.TryApplyTailRamp(mixer, queue.Track, 0);
                                await queue.Track.StartAsync().ConfigureAwait(false);
                            }
                        }

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
                BmsLogger.Error(exception, $"Failed to execute an audio command for BMS sample key {sampleKey:X2}.");
            }
        }
    }

    private bool hasPendingCommand(ushort sampleKey)
    {
        lock (commandLock)
        {
            return trackRegistry.GetTracks(sampleKey).Any(hasPendingCommand);
        }
    }

    private bool hasPendingCommand(Track track)
    {
        lock (commandLock)
            return liveBatcher.IsTrackPending(track) || hasPendingTrackCommandUnsafe(track);
    }

    private bool hasPendingTrackCommand(Track track)
    {
        lock (commandLock)
            return hasPendingTrackCommandUnsafe(track);
    }

    private bool hasPendingTrackCommandUnsafe(Track track) =>
        commandQueues.TryGetValue(track, out var queue) && queue.IsProcessing;

    private bool tryGetSchedulableTrack(ushort sampleKey, out Track track)
    {
        track = null!;

        foreach (var candidate in trackRegistry.GetTracks(sampleKey))
        {
            if (isTrackSchedulable(candidate))
            {
                track = candidate;
                return true;
            }
        }

        return false;
    }

    private bool isTrackSchedulable(Track track)
    {
        if (hasPendingCommand(track))
            return false;

        lock (commandLock)
            return trackAvailableTimes.TryGetValue(track, out var availableAt) ? availableAt <= currentTime() : !track.IsRunning;
    }

    private void queueAllTracks(ushort sampleKey, TrackCommand command)
    {
        foreach (var track in trackRegistry.GetTracks(sampleKey))
            queueCommand(sampleKey, track, command);
    }

    private void bindTrackVolumeAdjustments(IAdjustableAudioComponent component, int volume) =>
        BindTrackVolumeAdjustments(component, volume, aggregateVolume);

    internal static void BindTrackVolumeAdjustments(IAdjustableAudioComponent component, int volume, IBindable<double> aggregateVolume)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));
        component.AddAdjustment(AdjustableProperty.Volume, aggregateVolume);
    }

    public void Dispose()
    {
        lock (commandLock)
        {
            disposed = true;

            foreach (var queue in commandQueues.Values)
                queue.PendingCommands.Clear();
        }

        activeKeys.Clear();
        pausedKeys.Clear();
        pausedTracks.Clear();
        commandQueues.Clear();
        currentTracks.Clear();
        trackAvailableTimes.Clear();
        pendingPlays.Clear();
        liveBatcher.Dispose();
    }

    private enum TrackCommandType
    {
        Restart,
        Stop,
        Start,
        ScheduledRestart,
    }

    private readonly record struct TrackCommand(
        TrackCommandType Type,
        double Offset = 0,
        double Delay = 0,
        double TargetTime = 0,
        long ScheduledAtTimestamp = 0,
        Track? TrackToStop = null,
        double TrackToStopAvailableAt = 0);

    private readonly record struct PendingPlay(int Volume, double Offset, double RequestedAt);

    private sealed class TrackCommandQueue(Track track)
    {
        public readonly Track Track = track;
        public readonly Queue<TrackCommand> PendingCommands = new();
        public bool IsProcessing;
    }
}
