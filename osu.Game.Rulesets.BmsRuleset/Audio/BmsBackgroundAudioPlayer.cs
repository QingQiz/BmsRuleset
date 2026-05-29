using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <inheritdoc />
/// <summary>
///     Single component that drives all BGM auto-play events.
/// </summary>
public partial class BmsBackgroundAudioPlayer(IReadOnlyList<BmsBackgroundAudioPlayer.BgmEvent> sortedEvents, Bindable<bool> sourcePaused)
    : SkinReloadableDrawable
{

    public readonly record struct BgmEvent(double Time, string SampleKey, BmsSampleInfo SampleInfo);

    /// <summary>Maximum age of a BGM event that will still be played on a normal (non-seek) frame.</summary>
    private const double allowable_late_start = 100;

    /// <summary>Maximum number of previous events to inspect when reconstructing seek state.</summary>
    private const int max_seek_event_scan = 32;

    private readonly BindableBool sourceIsPaused = new();
    private readonly IBindable<bool> samplePlaybackDisabled = new BindableBool();
    private readonly BindableDouble requestedVolume = new(1);

    // One resolved lookup name per unique sample key - resolved lazily on first play.
    // Null value means "looked up but not found in the beatmap resources".
    private readonly Dictionary<string, string?> trackNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> trackLengths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ActiveBgmTrack> activeTracks = [];

    private int nextIndex;
    private double previousTime;
    private bool hasSeenFrame;
    private bool playbackBlocked;

    private ITrackStore? beatmapTrackStore;

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        stopAll();
        trackNames.Clear();
        trackLengths.Clear();
        beatmapTrackStore?.Dispose();
        base.Dispose(isDisposing);
    }

    #endregion

    protected override void SkinChanged(ISkinSource skin)
    {
        base.SkinChanged(skin);

        stopAll();
        trackNames.Clear();
        beatmapTrackStore?.Dispose();
        beatmapTrackStore = null;
        nextIndex = 0;
        hasSeenFrame = false;

        ensureTrackStore();
    }

    protected override void LoadAsyncComplete()
    {
        base.LoadAsyncComplete();

        sourceIsPaused.BindTo(sourcePaused);
        sourceIsPaused.BindValueChanged(_ => updatePlaybackBlocked(), true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        LifetimeStart = double.MinValue;
        LifetimeEnd = double.MaxValue;
    }

    protected override void Update()
    {
        base.Update();

        if (playbackBlocked)
            return;

        if (!hasSeenFrame)
        {
            hasSeenFrame = true;
            previousTime = Time.Current;
            handleSeek(Time.Current);
            cleanupFinishedTracks();
            return;
        }

        var delta = Time.Current - previousTime;
        var seeked = Math.Abs(delta) > allowable_late_start;

        if (seeked)
            handleSeek(Time.Current);

        previousTime = Time.Current;

        while (nextIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextIndex];
            if (Time.Current < evt.Time) break;

            if (Time.Current - evt.Time < allowable_late_start)
                playEvent(evt, 0);

            nextIndex++;
        }

        cleanupFinishedTracks();
    }

    private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
    {
        LegacyBeatmapSkin s => s,
        SkinTransformer t => t.Skin as LegacyBeatmapSkin,
        _ => null,
    };

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
        samplePlaybackDisabled.BindValueChanged(_ => updatePlaybackBlocked(), true);
    }

    private string? resolveTrackName(BgmEvent evt)
    {
        if (trackNames.TryGetValue(evt.SampleKey, out var cached))
            return cached;

        ensureTrackStore();

        if (beatmapTrackStore == null)
            return null;

        foreach (var lookup in evt.SampleInfo.LookupNames)
        {
            using var stream = beatmapTrackStore.GetStream(lookup);

            if (stream != null)
                return trackNames[evt.SampleKey] = lookup;
        }

        return trackNames[evt.SampleKey] = null;
    }

    private void ensureTrackStore()
    {
        if (beatmapTrackStore != null || audioManager == null)
            return;

        var beatmapResources = CurrentSkin.AllSources
            .Select(extractBeatmapSkin)
            .FirstOrDefault(s => s?.BeatmapSetResources != null)
            ?.BeatmapSetResources;

        if (beatmapResources == null)
            return;

        var resources = new ResourceStore<byte[]>(beatmapResources);
        resources.AddExtension("wav");
        resources.AddExtension("mp3");
        resources.AddExtension("ogg");

        beatmapTrackStore = audioManager.GetTrackStore(resources);
    }

    private void playEvent(BgmEvent evt, double offset)
    {
        var trackName = resolveTrackName(evt);
        if (trackName == null || beatmapTrackStore == null)
            return;

        var track = beatmapTrackStore.Get(trackName);
        if (track == null)
            return;

        if (offset > 0 && !track.Seek(offset))
        {
            track.Dispose();
            return;
        }

        if (track.Length > 0)
            trackLengths[evt.SampleKey] = track.Length;

        if (track.Length > 0 && offset >= track.Length)
        {
            track.Dispose();
            return;
        }

        bindBgmVolumeAdjustments(track);
        track.Start();

        activeTracks.Add(new ActiveBgmTrack(evt, track));
    }

    private void handleSeek(double currentTime)
    {
        seekExistingTracks(currentTime);

        nextIndex = findFirstEventAfter(currentTime);

        for (var i = nextIndex - 1; i >= 0 && nextIndex - i <= max_seek_event_scan; i--)
        {
            var evt = sortedEvents[i];

            if (activeTracks.Any(t => t.Event.Equals(evt)))
                continue;

            var offset = currentTime - evt.Time;

            if (offset < 0)
                continue;

            if (trackLengths.TryGetValue(evt.SampleKey, out var length) && length > 0 && offset >= length)
                continue;

            playEvent(evt, offset);
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

    private void seekExistingTracks(double currentTime)
    {
        for (var i = activeTracks.Count - 1; i >= 0; i--)
        {
            var activeTrack = activeTracks[i];
            var offset = currentTime - activeTrack.Event.Time;

            if (activeTrack.Track.IsDisposed || offset < 0 || activeTrack.Track.Length > 0 && offset >= activeTrack.Track.Length)
            {
                if (!activeTrack.Track.IsDisposed)
                {
                    activeTrack.Track.Stop();
                    activeTrack.Track.Dispose();
                }

                activeTracks.RemoveAt(i);
                continue;
            }

            if (!activeTrack.Track.Seek(offset))
            {
                activeTrack.Track.Dispose();
                activeTracks.RemoveAt(i);
                continue;
            }

            if (activeTrack.Track.Length > 0)
                trackLengths[activeTrack.Event.SampleKey] = activeTrack.Track.Length;

            activeTrack.Track.Start();
            activeTrack.Paused = false;
        }
    }

    private void updatePlaybackBlocked()
    {
        var blocked = sourceIsPaused.Value || samplePlaybackDisabled.Value;

        if (blocked == playbackBlocked)
            return;

        playbackBlocked = blocked;

        if (playbackBlocked)
        {
            pauseAll();
            return;
        }

        // Let Update() re-seed from the new gameplay time instead of briefly resuming stale audio.
        if (!hasSeenFrame || Math.Abs(Time.Current - previousTime) > allowable_late_start)
            return;

        resumeAll();
    }

    private void bindBgmVolumeAdjustments(IAdjustableAudioComponent component)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, requestedVolume);

        if (audioManager != null)
        {
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.Volume);
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.VolumeTrack);
        }
    }

    private void stopAll()
    {
        foreach (var activeTrack in activeTracks)
        {
            if (!activeTrack.Track.IsDisposed)
            {
                activeTrack.Track.Stop();
                activeTrack.Track.Dispose();
            }
        }

        activeTracks.Clear();
    }

    private void pauseAll()
    {
        foreach (var activeTrack in activeTracks)
        {
            if (!activeTrack.Track.IsDisposed)
                activeTrack.Track.Stop();

            activeTrack.Paused = true;
        }
    }

    private void resumeAll()
    {
        foreach (var activeTrack in activeTracks)
        {
            if (activeTrack.Paused && !activeTrack.Track.IsDisposed)
            {
                activeTrack.Track.Start();
                activeTrack.Paused = false;
            }
        }
    }

    private void cleanupFinishedTracks()
    {
        for (var i = activeTracks.Count - 1; i >= 0; i--)
        {
            var activeTrack = activeTracks[i];

            if (activeTrack.Track.IsDisposed)
            {
                activeTracks.RemoveAt(i);
                continue;
            }

            if (activeTrack.Paused)
                continue;

            if (activeTrack.Track.HasCompleted || (!activeTrack.Track.IsRunning && activeTrack.Track.Length > 0 && activeTrack.Track.CurrentTime >= activeTrack.Track.Length))
            {
                activeTrack.Track.Dispose();
                activeTracks.RemoveAt(i);
            }
        }
    }

    private sealed class ActiveBgmTrack(BgmEvent evt, Track track)
    {
        public BgmEvent Event { get; } = evt;

        public Track Track { get; } = track;

        public bool Paused { get; set; }
    }
}
