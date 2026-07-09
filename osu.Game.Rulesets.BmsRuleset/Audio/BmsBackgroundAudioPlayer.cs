using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <inheritdoc />
/// <summary>
///     Single component that drives all BGM auto-play events.
/// </summary>
/// <remarks>
///     <para>
///         Forward (normal) playback is the hot path — BGM events fire many times per second — so
///         it uses lightweight <see cref="SampleChannel" />s obtained from samples that
///         <see cref="BmsSampleStore" /> decoded up-front. This is what fixes the per-event disk
///         I/O + decode cost of the old per-event <see cref="Track" /> approach.
///     </para>
///     <para>
///         <see cref="SampleChannel" /> cannot start from an arbitrary offset, so seeking into the
///         middle of a still-sounding sample (e.g. a 10s pad seeked to +6s) would otherwise be
///         silent. To stay correct, the rare seek path resumes such samples with a real
///         <see cref="Track" /> seeked to the right offset. Tracks are only ever created on seek,
///         keeping the forward hot path allocation- and decode-free.
///     </para>
/// </remarks>
public partial class BmsBackgroundAudioPlayer(
    IReadOnlyList<BmsBackgroundAudioPlayer.BgmEvent> sortedEvents,
    Bindable<bool> sourcePaused,
    double rate = 1.0)
    : Component
{
    public readonly record struct BgmEvent(double Time, string SamplePath, int Volume = 100);

    /// <summary>Maximum age of a BGM event that will still be played on a normal (non-seek) frame.</summary>
    private const double allowable_late_start = 100;

    private readonly BindableBool sourceIsPaused = new();
    private readonly IBindable<bool> samplePlaybackDisabled = new BindableBool();
    private readonly BindableDouble pauseFrequency = new(1);

    private readonly List<ActiveBgm> activeChannels = [];

    // One resolved track lookup name per unique sample path - resolved lazily on first seek that
    // needs an offset playback. Null value means "looked up but not found in the beatmap resources".
    private readonly Dictionary<string, string?> trackNames = new(StringComparer.OrdinalIgnoreCase);

    private int nextIndex;
    private double previousTime;
    private bool hasSeenFrame;
    private bool playbackBlocked;

    private ITrackStore? beatmapTrackStore;

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

    [Resolved(CanBeNull = true)]
    private BmsSampleStore? sampleCache { get; set; }

    [Resolved(CanBeNull = true)]
    private ISkinSource? skin { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        stopAll();
        trackNames.Clear();
        beatmapTrackStore?.Dispose();
        beatmapTrackStore = null;
        base.Dispose(isDisposing);
    }

    #endregion

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
            cleanupFinishedChannels();
            return;
        }

        var delta = Time.Current - previousTime;
        var seeked = Math.Abs(delta) > allowable_late_start;
        previousTime = Time.Current;

        if (seeked)
            handleSeek(Time.Current);

        while (nextIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextIndex];

            if (Time.Current < evt.Time)
                break;

            if (Time.Current - evt.Time < allowable_late_start)
                playSample(evt);

            nextIndex++;
        }

        cleanupFinishedChannels();
    }

    private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
    {
        LegacyBeatmapSkin beatmapSkin => beatmapSkin,
        SkinTransformer transformer => transformer.Skin as LegacyBeatmapSkin,
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

    /// <summary>
    ///     Re-seeds playback after a clock jump: drops everything currently sounding, then resumes
    ///     every sample whose [start, start+length) interval still contains the new time. Samples
    ///     that have only just begun are restarted cheaply as sample channels; those that need to
    ///     resume part-way through are played back with a seekable <see cref="Track" />.
    /// </summary>
    private void handleSeek(double currentTime)
    {
        stopAll();

        nextIndex = findFirstEventAfter(currentTime);

        var maxLength = sampleCache?.MaxSampleLengthMilliseconds ?? 0;

        for (var i = nextIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];
            var offset = currentTime - evt.Time;

            if (offset < 0)
                continue;

            // offset is chart-ms (currentTime is the mod-rate-advanced gameplay clock, evt.Time is
            // the original chart time). maxLength / length are the *stretched* samples' real-ms
            // durations (original / rate), so multiply by rate to compare in chart-ms (= original).
            // Without this, under DT the back portion of long samples is silently dropped on seek.
            if (maxLength > 0 && offset >= maxLength * rate)
                break;

            var length = sampleCache?.Get(evt.SamplePath)?.Length ?? 0;

            if (length > 0 && offset >= length * rate)
                continue;

            if (offset < allowable_late_start)
                playSample(evt);
            else
                playTrackAtOffset(evt, offset);
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

    private void playSample(BgmEvent evt)
    {
        var sample = sampleCache?.Get(evt.SamplePath);

        if (sample == null)
            return;

        var channel = sample.GetChannel();
        channel.ManualFree = true;
        channel.Play();

        // channel.Play() enqueues BindAdjustments on the audio thread that bind the channel to the
        // resolved sample's aggregates (which may carry the global effect volume). BGM must be
        // governed only by the requested chart volume and AudioManager aggregate, so we re-isolate
        // once after that bind lands to strip anything else back out.
        bindBgmVolumeAdjustments(channel, evt.Volume);

        Action<ValueChangedEvent<double>>? isolateOnBind = null;
        isolateOnBind = _ =>
        {
            channel.AggregateVolume.ValueChanged -= isolateOnBind!;
            bindBgmVolumeAdjustments(channel, evt.Volume);
        };
        channel.AggregateVolume.ValueChanged += isolateOnBind;

        channel.AddAdjustment(AdjustableProperty.Frequency, pauseFrequency);
        activeChannels.Add(ActiveBgm.ForChannel(channel));
    }

    private void playTrackAtOffset(BgmEvent evt, double offset)
    {
        var track = resolveTrack(evt);

        if (track == null)
            return;

        if (!track.Seek(offset) || (track.Length > 0 && offset >= track.Length))
        {
            track.Dispose();
            return;
        }

        // Tracks are not routed through the effect-volume sample chain, so a direct bind of the BGM
        // volume chain (requested chart volume × AudioManager aggregate) is sufficient.
        bindBgmVolumeAdjustments(track, evt.Volume);

        if (Math.Abs(rate - 1.0) > 0.001)
            track.AddAdjustment(AdjustableProperty.Tempo, new BindableDouble(rate));

        track.Start();

        activeChannels.Add(ActiveBgm.ForTrack(track));
    }

    private Track? resolveTrack(BgmEvent evt)
    {
        ensureTrackStore();

        if (beatmapTrackStore == null)
            return null;

        if (!trackNames.TryGetValue(evt.SamplePath, out var name))
        {
            name = null;

            foreach (var lookup in new BmsSampleInfo(evt.SamplePath).LookupNames)
            {
                using var stream = beatmapTrackStore.GetStream(lookup);

                if (stream != null)
                {
                    name = lookup;
                    break;
                }
            }

            trackNames[evt.SamplePath] = name;
        }

        return name == null ? null : beatmapTrackStore.Get(name);
    }

    private void ensureTrackStore()
    {
        if (beatmapTrackStore != null)
            return;

        // Tier 1 — filesystem-backed track store (external-audio import mode).
        if (sampleCache?.TrackStore != null)
        {
            beatmapTrackStore = sampleCache.TrackStore;
            return;
        }

        // Tier 2 — LegacyBeatmapSkin Realm-backed track store.
        if (audioManager == null || skin == null)
            return;

        var beatmapResources = skin.AllSources
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

    private void bindBgmVolumeAdjustments(IAdjustableAudioComponent component, int volume = 100)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));

        if (audioManager != null)
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.AggregateVolume);
    }

    private void stopAll()
    {
        foreach (var active in activeChannels)
            active.StopAndDispose();

        activeChannels.Clear();
        pauseFrequency.Value = 1;
    }

    private void pauseAll()
    {
        // Sample channels are frozen en masse via the shared frequency adjustment; tracks are
        // paused individually (retaining their position).
        pauseFrequency.Value = 0;

        foreach (var active in activeChannels)
            active.Pause();
    }

    private void resumeAll()
    {
        pauseFrequency.Value = 1;

        foreach (var active in activeChannels)
            active.Resume();
    }

    private void cleanupFinishedChannels()
    {
        for (var i = activeChannels.Count - 1; i >= 0; i--)
        {
            var active = activeChannels[i];

            if (active.IsDisposed)
            {
                activeChannels.RemoveAt(i);
                continue;
            }

            if (active.Paused)
                continue;

            if (active.HasFinished)
            {
                active.StopAndDispose();
                activeChannels.RemoveAt(i);
            }
        }
    }

    /// <summary>
    ///     A single active BGM playback, backed by either a <see cref="SampleChannel" /> (forward
    ///     play) or a <see cref="Track" /> (mid-sample resume after a seek).
    /// </summary>
    private sealed class ActiveBgm
    {

        public bool IsDisposed => channel?.IsDisposed ?? track!.IsDisposed;

        public bool HasFinished
        {
            get
            {
                if (channel != null)
                    return channel.Played && !channel.Playing;

                return track!.HasCompleted || (!track.IsRunning && track.Length > 0 && track.CurrentTime >= track.Length);
            }
        }

        public bool Paused { get; private set; }

        private readonly SampleChannel? channel;
        private readonly Track? track;

        private ActiveBgm(SampleChannel? channel, Track? track)
        {
            this.channel = channel;
            this.track = track;
        }

        public static ActiveBgm ForChannel(SampleChannel channel) => new(channel, null);

        public static ActiveBgm ForTrack(Track track) => new(null, track);

        public void Pause()
        {
            // Sample channels are paused collectively through the shared frequency adjustment.
            if (track is { IsDisposed: false })
                track.Stop();

            Paused = true;
        }

        public void Resume()
        {
            if (track is { IsDisposed: false })
                track.Start();

            Paused = false;
        }

        public void StopAndDispose()
        {
            if (channel is { IsDisposed: false })
            {
                channel.Stop();
                channel.Dispose();
            }

            if (track is { IsDisposed: false })
            {
                track.Stop();
                track.Dispose();
            }
        }
    }
}
