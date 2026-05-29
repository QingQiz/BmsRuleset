using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <inheritdoc />
/// <summary>
///     Single component that drives all BGM auto-play events.
/// </summary>
public partial class BmsBackgroundAudioPlayer(IReadOnlyList<BmsBackgroundAudioPlayer.BgmEvent> sortedEvents, Bindable<bool> sourcePaused)
    : SkinReloadableDrawable
{
    /// <summary>Maximum age of a BGM event that will still be played on a normal (non-seek) frame.</summary>
    private const double allowable_late_start = 100;

    /// <summary>
    ///     On a seek (clock jumped by more than <see cref="allowable_late_start"/>), rewind
    ///     <see cref="nextIndex"/> this many ms before the target time so events near the
    ///     seek destination are replayed from their beginning (SampleChannel cannot seek).
    /// </summary>
    private const double seek_lookback = 3000;

    private readonly BindableBool isPaused = new();
    private readonly BindableDouble pauseFrequency = new(1);
    private readonly BindableDouble requestedVolume = new(1);

    // One ISample per unique sample key - resolved lazily on first play.
    // Null value means "looked up but not found in the beatmap skin".
    private readonly Dictionary<string, ISample?> samples = new();
    private readonly List<ActiveBgmChannel> activeChannels = [];

    private int nextIndex;
    private double previousTime;
    private bool hasSeenFrame;

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        stopAll();
        samples.Clear();
        base.Dispose(isDisposing);
    }

    #endregion

    protected override void SkinChanged(ISkinSource skin)
    {
        base.SkinChanged(skin);

        // Invalidate the sample cache so stale ISample references aren't used after a skin change.
        // Actual audio loading is deferred to the first time each event fires.
        samples.Clear();
    }

    protected override void LoadAsyncComplete()
    {
        base.LoadAsyncComplete();

        isPaused.BindTo(sourcePaused);
        isPaused.BindValueChanged(v =>
        {
            if (v.NewValue)
                pauseAll();
            else
                resumeAll();
        }, true);
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

        if (!hasSeenFrame)
        {
            hasSeenFrame = true;
            previousTime = Time.Current;
        }

        var delta = Time.Current - previousTime;
        previousTime = Time.Current;

        var seeked = Math.Abs(delta) > allowable_late_start;

        if (seeked)
        {
            stopAll();
            nextIndex = 0;

            while (nextIndex < sortedEvents.Count
                   && sortedEvents[nextIndex].Time < Time.Current - seek_lookback)
                nextIndex++;
        }

        if (isPaused.Value)
            return;

        var tolerance = seeked ? seek_lookback : allowable_late_start;

        while (nextIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextIndex];
            if (Time.Current < evt.Time) break;

            if (Time.Current - evt.Time < tolerance)
                playEvent(evt);

            nextIndex++;
        }

        cleanupFinishedChannels();
    }

    private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
    {
        LegacyBeatmapSkin s => s,
        SkinTransformer t => t.Skin as LegacyBeatmapSkin,
        _ => null,
    };

    private ISample? resolveSample(BgmEvent evt)
    {
        if (samples.TryGetValue(evt.SampleKey, out var cached))
            return cached;

        ISample? sample = null;

        foreach (var source in CurrentSkin.AllSources.Select(extractBeatmapSkin).Where(s => s != null))
        {
            sample = source!.GetSample(evt.SampleInfo);
            if (sample != null) break;
        }

        samples[evt.SampleKey] = sample;
        return sample;
    }

    private void playEvent(BgmEvent evt)
    {
        var sample = resolveSample(evt);
        if (sample == null)
            return;

        var channel = sample.GetChannel();
        channel.ManualFree = true;
        channel.AddAdjustment(AdjustableProperty.Frequency, pauseFrequency);

        // Play() enqueues framework volume bindings, so strip sample/effect volume after it.
        channel.Play();

        bindBgmVolumeAdjustments(channel);

        Action<ValueChangedEvent<double>>? isolateOnBind = null;
        isolateOnBind = _ =>
        {
            channel.AggregateVolume.ValueChanged -= isolateOnBind!;
            bindBgmVolumeAdjustments(channel);
        };
        channel.AggregateVolume.ValueChanged += isolateOnBind;

        activeChannels.Add(new ActiveBgmChannel(channel));
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
        foreach (var ac in activeChannels)
        {
            if (!ac.Channel.IsDisposed)
            {
                ac.Channel.Stop();
                ac.Channel.Dispose();
            }
        }

        activeChannels.Clear();
        pauseFrequency.Value = 1;
    }

    private void pauseAll()
    {
        pauseFrequency.Value = 0;

        foreach (var ac in activeChannels)
            ac.Paused = true;
    }

    private void resumeAll()
    {
        pauseFrequency.Value = 1;

        foreach (var ac in activeChannels)
        {
            if (ac.Paused && !ac.Channel.IsDisposed)
            {
                ac.Channel.Play();
                ac.Paused = false;
            }
        }
    }

    private void cleanupFinishedChannels()
    {
        for (var i = activeChannels.Count - 1; i >= 0; i--)
        {
            var ac = activeChannels[i];

            if (ac.Channel.IsDisposed)
            {
                activeChannels.RemoveAt(i);
                continue;
            }

            if (ac.Paused)
                continue;

            if (ac.Channel.Played && !ac.Channel.Playing)
            {
                ac.Channel.Dispose();
                activeChannels.RemoveAt(i);
            }
        }
    }

    public readonly record struct BgmEvent(double Time, string SampleKey, BmsSampleInfo SampleInfo);

    private sealed class ActiveBgmChannel(SampleChannel channel)
    {
        public SampleChannel Channel { get; } = channel;

        public bool Paused { get; set; }
    }
}
