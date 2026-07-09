using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <inheritdoc />
/// <summary>
///     BMS chart sample playback. Samples are defined by the chart resources, not by the user skin.
/// </summary>
public sealed partial class BmsChartSampleSound : SkinReloadableDrawable
{
    public override bool RemoveWhenNotAlive => false;

    public ISampleInfo? SampleInfo
    {
        set
        {
            if (ReferenceEquals(sampleInfo, value))
                return;

            sampleInfo = value;

            if (LoadState >= LoadState.Ready)
                updateSample();
        }
    }

    private readonly List<ActiveChannel> activeChannels = [];
    private readonly IBindable<bool> samplePlaybackDisabled = new BindableBool();
    private readonly BindableDouble pauseFrequency = new(1);

    private readonly record struct ResolvedSample(ISampleInfo Info, ISample Sample);

    private ISampleInfo? sampleInfo;
    private ResolvedSample? resolvedSample;

    private bool requestedPlaying { get; set; }

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

    [Resolved]
    private BmsSampleStore sampleCache { get; set; } = null!;

    public BmsChartSampleSound()
    {
    }

    public BmsChartSampleSound(ISampleInfo sample)
    {
        SampleInfo = sample;
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        Stop();
        base.Dispose(isDisposing);
    }

    #endregion

    public void ClearSample() => SampleInfo = null;

    public void Play()
    {
        requestedPlaying = true;

        if (samplePlaybackDisabled.Value)
            return;

        pauseFrequency.Value = 1;
        FlushPendingSkinChanges();

        if (resolvedSample is not { } resolved)
            return;

        var channel = resolved.Sample.GetChannel();
        channel.ManualFree = true;
        channel.Play();

        // channel.Play() enqueues two BindAdjustments calls on the audio thread via
        // AudioCollectionManager.AddItem: one that binds channel ← sampleBass, and one
        // that binds sampleBass ← factory (which carries VolumeSample / effect-volume).
        // Those run after this game-thread code, so a plain synchronous cleanup here
        // would be silently overwritten.
        //
        // Strategy:
        //   1. Apply the synchronous cleanup now (covers the case where all volumes are
        //      1.0 and the aggregate never changes, so the handler below never fires –
        //      but in that case the extra factor of 1.0 from sampleBass is harmless).
        //   2. Subscribe a one-shot handler to channel.AggregateVolume.ValueChanged.
        //      It fires the instant BindAdjustments (audio thread) changes the aggregate,
        //      unsubscribes itself, and re-applies the clean isolation.
        //
        // The event add/remove uses Interlocked (compiler-generated), so game-thread add
        // and audio-thread invoke/remove are both safe.
        bindChartAudioAdjustments(channel, resolved.Info.Volume);

        Action<ValueChangedEvent<double>>? isolateOnBind = null;
        isolateOnBind = _ =>
        {
            channel.AggregateVolume.ValueChanged -= isolateOnBind!;
            bindChartAudioAdjustments(channel, resolved.Info.Volume);
        };
        channel.AggregateVolume.ValueChanged += isolateOnBind;

        channel.AddAdjustment(AdjustableProperty.Frequency, pauseFrequency);
        activeChannels.Add(new ActiveChannel(channel, resolved.Info.Volume));
    }

    public void Pause()
    {
        foreach (var activeChannel in activeChannels)
        {
            if (activeChannel.Channel.IsDisposed || !activeChannel.Channel.Playing)
                continue;

            pauseFrequency.Value = 0;
            activeChannel.Paused = true;
        }
    }

    public void Resume()
    {
        if (!requestedPlaying || samplePlaybackDisabled.Value)
            return;

        FlushPendingSkinChanges();
        pauseFrequency.Value = 1;

        if (activeChannels.Count == 0)
        {
            Play();
            return;
        }

        foreach (var activeChannel in activeChannels)
        {
            if (activeChannel.Channel.IsDisposed || !activeChannel.Paused)
                continue;

            activeChannel.Channel.Play();
            bindChartAudioAdjustments(activeChannel.Channel, activeChannel.Volume);
            activeChannel.Paused = false;
        }
    }

    public void Stop()
    {
        requestedPlaying = false;

        foreach (var activeChannel in activeChannels)
        {
            if (activeChannel.Channel.IsDisposed)
                continue;

            activeChannel.Channel.Stop();
            activeChannel.Channel.Dispose();
        }

        activeChannels.Clear();
        pauseFrequency.Value = 1;
    }

    protected override void SkinChanged(ISkinSource skin)
    {
        base.SkinChanged(skin);
        updateSample();
    }

    protected override void Update()
    {
        base.Update();

        cleanupStoppedChannels();
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
        samplePlaybackDisabled.BindValueChanged(disabled =>
        {
            if (disabled.NewValue)
                Pause();
            else
                Resume();
        });
    }

    private void updateSample()
    {
        resolvedSample = null;

        if (sampleInfo == null)
            return;

        // Pull the already-decoded sample from the in-memory preload cache. Do NOT bind any
        // adjustments to the sample itself: the cache instance is shared across every playback,
        // so volume/frequency isolation is applied per-channel in Play() instead.
        var sample = sampleCache.Get(sampleInfo);

        if (sample == null)
            return;

        resolvedSample = new ResolvedSample(sampleInfo, sample);
    }

    private void cleanupStoppedChannels()
    {
        for (var i = activeChannels.Count - 1; i >= 0; i--)
        {
            var activeChannel = activeChannels[i];

            if (activeChannel.Channel.IsDisposed)
            {
                activeChannels.RemoveAt(i);
                continue;
            }

            if (activeChannel.Paused)
                continue;

            if (!activeChannel.Channel.Played || activeChannel.Channel.Playing)
                continue;

            activeChannel.Channel.Dispose();
            activeChannels.RemoveAt(i);
        }

        if (activeChannels.Count == 0)
            requestedPlaying = false;
    }

    private void bindChartAudioAdjustments(IAdjustableAudioComponent component, int volume = 100)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));

        if (audioManager != null)
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.AggregateVolume);
    }

    private sealed class ActiveChannel(SampleChannel channel, int volume)
    {
        public SampleChannel Channel { get; } = channel;

        public int Volume { get; } = volume;

        public bool Paused { get; set; }
    }
}
