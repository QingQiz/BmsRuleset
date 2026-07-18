using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

public enum BmsPreviewTrackPlaybackMode
{
    Preview,
    GameplayClockOnly,
}

internal readonly record struct BmsPreviewSampleEvent(BmsSampleEvent Event, bool ResumeAfterSeek);

public class BmsPreviewTrack : Track
{
    private const double restore_fade_duration = 20;

    public override bool IsRunning
    {
        get
        {
            lock (clock) return clock.IsRunning;
        }
    }

    public override bool IsDummyDevice => false;

    /// <summary>
    /// Exposes the resolved source so callers can verify that the dedicated-track loading path was skipped.
    /// </summary>
    public bool UsesDedicatedPreviewAudio => previewTrack != null;

    public override double CurrentTime
    {
        get
        {
            lock (clock) return Math.Min(Length, seekOffset + clock.CurrentTime);
        }
    }

    /// <summary>
    ///     Gameplay seeks and starts the beatmap track as a clock source, but BMS gameplay audio is
    ///     driven by chart events elsewhere; clock-only mode prevents those framework clock calls
    ///     from replaying song-select preview audio.
    /// </summary>
    public BmsPreviewTrackPlaybackMode PlaybackMode
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            previewOutputVolume.Value = field == BmsPreviewTrackPlaybackMode.Preview ? 1 : 0;

            if (field == BmsPreviewTrackPlaybackMode.Preview)
            {
                // Gameplay advances this clock while BGM/key sample events are muted, so restoring
                // preview must resume from the current position rather than replaying the muted gap.
                nextEventIndex = findFirstEventAfter(CurrentTime);
                eventResyncRequired = previewTrack == null;
            }
            else
            {
                eventResyncRequired = false;
                // A preview event may already have passed the mode check on the audio thread. Run
                // cleanup after that frame so it also catches any track the frame creates.
                EnqueueAction(stopPreviewPlayback);
            }
        }
    }

    private readonly StopwatchClock clock = new();
    private readonly List<BgmEvent> sortedEvents = [];
    private readonly ITrackStore? previewTrackStore;
    private readonly Track? previewTrack;
    private readonly List<Track> activeTracks = [];
    private readonly BindableDouble previewOutputVolume = new(1);
    private readonly BindableDouble restoreFadeVolume = new(1);

    private readonly record struct BgmEvent(double Time, ushort SampleKey, string SamplePath, int Volume, bool ResumeAfterSeek);

    private int nextEventIndex;
    private double seekOffset;
    private bool eventResyncRequired;
    private long restoreFadeStart;
    private bool restoreFadeInProgress;

    /// <param name="sampleEvents">BGM and keysound events from the parsed BMS chart.</param>
    /// <param name="sampleDefinitions">Maps sample keys to filenames from the BMS chart.</param>
    /// <param name="basePath">
    ///     The chart directory on disk (from <c>BeatmapInfo.Metadata.Source</c>).
    ///     May be <c>null</c> in fully-imported mode (no filesystem fallback available).
    /// </param>
    /// <param name="audioManager">Framework audio manager, used to create filesystem-backed audio stores.</param>
    /// <param name="previewFile"></param>
    public BmsPreviewTrack(
        IReadOnlyList<BmsSampleEvent> sampleEvents,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager,
        string? previewFile = null)
        : this(() => createPreviewEvents(sampleEvents), sampleDefinitions, basePath, audioManager, previewFile)
    {
    }

    internal BmsPreviewTrack(
        Func<IReadOnlyList<BmsPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager,
        string? previewFile = null)
        : base("bms-preview")
    {
        // Propagate the track's aggregate rate (populated by AdjustmentsFromMods in gameplay,
        // or by MusicController mod-track-adjustments elsewhere) into the inner StopwatchClock.
        // BmsPreviewTrack.CurrentTime is driven by this StopwatchClock, not by Track.Rate, so
        // without this the gameplay clock would not advance at the mod rate in the
        // DecouplingFramedClock's source-running branch.
        AggregateFrequency.ValueChanged += _ => updateClockRate();
        AggregateTempo.ValueChanged += _ => updateClockRate();
        updateClockRate();

        if (basePath == null) return;

        var fileResources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
        fileResources.AddExtension("wav");
        fileResources.AddExtension("mp3");
        fileResources.AddExtension("ogg");

        if (BmsRulesetRuntime.UseDedicatedPreviewAudio)
        {
            foreach (var candidate in getPreviewCandidates(basePath, previewFile))
            {
                previewTrackStore ??= audioManager.GetTrackStore(fileResources);
                previewTrack = resolvePreviewTrack(candidate);

                if (previewTrack == null)
                    continue;

                break;
            }
        }

        if (previewTrack == null)
        {
            previewTrackStore ??= audioManager.GetTrackStore(fileResources);

            foreach (var previewEvent in sampleEventFactory())
            {
                var evt = previewEvent.Event;

                if (sampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath))
                    sortedEvents.Add(new BgmEvent(evt.Time, evt.SampleKey, samplePath, evt.Volume, previewEvent.ResumeAfterSeek));
            }

            sortedEvents.Sort((a, b) => a.Time.CompareTo(b.Time));

            if (sortedEvents.Count > 0 && sortedEvents[0].Time > 0)
            {
                var leadIn = sortedEvents[0].Time;

                for (var i = 0; i < sortedEvents.Count; i++)
                {
                    var evt = sortedEvents[i];
                    sortedEvents[i] = evt with { Time = evt.Time - leadIn };
                }
            }

            var length = sortedEvents.Count > 0
                ? sortedEvents[^1].Time + 5000
                : 30000;
            Length = length;
        }
        else
            Length = 30000;
    }

    #region Disposal

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            lock (clock) clock.Stop();
            stopPreviewPlayback();
            previewTrack?.Dispose();
            previewTrackStore?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion

    public override void Start()
    {
        if (Length == 0 || CurrentTime >= Length)
            return;

        if (PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
        {
            startDedicatedPreviewTrack();
            eventResyncRequired = previewTrack == null && activeTracks.Count == 0;

            if (eventResyncRequired)
                nextEventIndex = findFirstEventAfter(CurrentTime);
        }

        lock (clock) clock.Start();
    }

    public override void Stop()
    {
        lock (clock) clock.Stop();
        stopPreviewPlayback();
    }

    public override bool Seek(double seek)
    {
        seekOffset = Math.Clamp(seek, 0, Length);

        var success = seekOffset == seek;
        var wasRunning = IsRunning;

        lock (clock)
        {
            if (success && wasRunning)
                clock.Restart();
            else
                clock.Reset();
        }

        stopPreviewPlayback();

        if (PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
        {
            nextEventIndex = seekOffset == 0 ? 0 : findFirstEventAfter(seekOffset);
            eventResyncRequired = previewTrack == null && seekOffset > 0;
        }

        if (previewTrack != null && wasRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            startDedicatedPreviewTrack();

        return success;
    }

    public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));

    public override Task StartAsync()
    {
        Start();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Stop();
        return Task.CompletedTask;
    }

    public override void Reset()
    {
        lock (clock) clock.Reset();
        seekOffset = 0;
        nextEventIndex = 0;
        restoreFadeInProgress = false;
        restoreFadeVolume.Value = 1;
        stopPreviewPlayback();

        base.Reset();
    }

    protected override void UpdateState()
    {
        base.UpdateState();
        updateRestoreFade();

        if (previewTrack is { Length: > 0 } && Length != previewTrack.Length)
            Length = previewTrack.Length;

        lock (clock)
        {
            if (clock.IsRunning && CurrentTime >= Length)
            {
                if (Looping)
                    Restart();
                else
                {
                    Stop();
                    RaiseCompleted();
                }
            }
        }

        if (!IsRunning || PlaybackMode != BmsPreviewTrackPlaybackMode.Preview)
            return;

        if (previewTrack != null)
            return;

        var currentTime = CurrentTime;

        if (eventResyncRequired)
        {
            eventResyncRequired = false;
            resumeEventTracks(currentTime);
        }

        while (nextEventIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextEventIndex];

            if (currentTime < evt.Time)
                break;

            playTrack(evt);
            nextEventIndex++;
        }

        cleanupTracks();
    }

    private static IEnumerable<string> getPreviewCandidates(string basePath, string? previewFile)
    {
        if (!string.IsNullOrWhiteSpace(previewFile))
            yield return previewFile;

        if (!Directory.Exists(basePath))
            yield break;

        foreach (var file in Directory.EnumerateFiles(basePath)
                     .Select(Path.GetFileName)
                     .Where(static name => name != null
                                           && name.StartsWith("preview", StringComparison.OrdinalIgnoreCase)
                                           && isSupportedPreviewExtension(name))
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            yield return file!;
        }

        yield break;

        static bool isSupportedPreviewExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void updateClockRate()
    {
        lock (clock)
            clock.Rate = Rate;
    }

    private void playTrack(BgmEvent evt)
    {
        var track = resolvePreviewTrack(evt.SamplePath);

        if (track == null)
            return;

        bindPreviewAdjustments(track, evt.Volume);
        track.Start();
        activeTracks.Add(track);
    }

    private void resumeEventTracks(double currentTime)
    {
        HashSet<ushort> resumedKeys = [];

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];

            if (!evt.ResumeAfterSeek || !resumedKeys.Add(evt.SampleKey))
                continue;

            var track = resolvePreviewTrack(evt.SamplePath);

            if (track == null)
                continue;

            var offset = currentTime - evt.Time;
            track.Seek(offset);

            if (track.Length <= 0 || offset >= track.Length)
            {
                track.Dispose();
                continue;
            }

            bindPreviewAdjustments(track, evt.Volume);
            track.Start();
            activeTracks.Add(track);
        }
    }

    private Track? resolvePreviewTrack(string samplePath)
    {
        if (previewTrackStore == null)
            return null;

        foreach (var lookup in new BmsSampleInfo(samplePath).LookupNames)
        {
            var track = previewTrackStore.Get(lookup);

            if (track != null)
                return track;
        }

        return null;
    }

    private void bindPreviewAdjustments(IAdjustableAudioComponent component, int volume = 100)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.RemoveAllAdjustments(AdjustableProperty.Balance);
        component.RemoveAllAdjustments(AdjustableProperty.Frequency);
        component.RemoveAllAdjustments(AdjustableProperty.Tempo);
        component.BindAdjustments(this);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));
        component.AddAdjustment(AdjustableProperty.Volume, previewOutputVolume);
        component.AddAdjustment(AdjustableProperty.Volume, restoreFadeVolume);
    }

    internal void BeginRestoreFade()
    {
        restoreFadeVolume.Value = 0;
        restoreFadeStart = Stopwatch.GetTimestamp();
        restoreFadeInProgress = true;
    }

    private void updateRestoreFade()
    {
        if (!restoreFadeInProgress)
            return;

        var progress = Stopwatch.GetElapsedTime(restoreFadeStart).TotalMilliseconds / restore_fade_duration;
        restoreFadeVolume.Value = Math.Min(1, progress);
        restoreFadeInProgress = progress < 1;
    }

    private void startDedicatedPreviewTrack()
    {
        if (previewTrack == null || previewTrack.IsRunning)
            return;

        bindPreviewAdjustments(previewTrack);
        previewTrack.Seek(CurrentTime);
        previewTrack.Start();
    }

    private void stopPreviewPlayback()
    {
        for (var i = 0; i < activeTracks.Count; i++)
        {
            var track = activeTracks[i];

            if (!track.IsDisposed)
            {
                track.Stop();
                track.Dispose();
            }
        }

        activeTracks.Clear();
        previewTrack?.Stop();
    }

    private void cleanupTracks()
    {
        for (var i = activeTracks.Count - 1; i >= 0; i--)
        {
            var track = activeTracks[i];

            if (track.IsDisposed)
            {
                activeTracks.RemoveAt(i);
                continue;
            }

            if (track.HasCompleted)
            {
                track.Dispose();
                activeTracks.RemoveAt(i);
            }
        }
    }

    private int findFirstEventAfter(double time)
    {
        var low = 0;
        var high = sortedEvents.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (sortedEvents[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static IReadOnlyList<BmsPreviewSampleEvent> createPreviewEvents(IReadOnlyList<BmsSampleEvent> sampleEvents)
    {
        var previewEvents = new BmsPreviewSampleEvent[sampleEvents.Count];

        for (var i = 0; i < sampleEvents.Count; i++)
            previewEvents[i] = new BmsPreviewSampleEvent(sampleEvents[i], true);

        return previewEvents;
    }
}
