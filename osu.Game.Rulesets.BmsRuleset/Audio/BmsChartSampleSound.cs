using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     BMS chart sample playback. Samples are defined by the chart resources, not by the user skin.
/// </summary>
public partial class BmsChartSampleSound : SkinReloadableDrawable
{
    public override bool RemoveWhenNotAlive => false;

    public double Length => resolvedSample?.Sample.Length ?? 0;

    public ISampleInfo? SampleInfo
    {
        get => sampleInfo;
        set
        {
            if (ReferenceEquals(sampleInfo, value))
                return;

            sampleInfo = value;

            if (LoadState >= LoadState.Ready)
                updateSample();
        }
    }

    public bool RequestedPlaying { get; private set; }

    protected bool HasActiveChannels => activeChannels.Count > 0;

    private readonly List<ActiveChannel> activeChannels = [];
    private readonly IBindable<bool> samplePlaybackDisabled = new BindableBool();
    private readonly BindableDouble pauseFrequency = new(1);
    private readonly BindableDouble requestedVolume = new(1);

    private readonly record struct ResolvedSample(ISampleInfo Info, ISample Sample);

    private ISampleInfo? sampleInfo;
    private ResolvedSample? resolvedSample;

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

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

    public virtual void Play()
    {
        RequestedPlaying = true;

        if (samplePlaybackDisabled.Value)
            return;

        pauseFrequency.Value = 1;
        FlushPendingSkinChanges();

        if (resolvedSample is not { } resolved)
            return;

        var channel = resolved.Sample.GetChannel();
        channel.ManualFree = true;
        requestedVolume.Value = Math.Max(0, resolved.Info.Volume) / 100.0;
        channel.Play();

        // TODO FIXME 开头的sample 快进/skip时会停止播放，另外 review 快进到 某个 sample 的中间部分，sample是否会播放

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
        bindChartAudioAdjustments(channel);

        Action<ValueChangedEvent<double>>? isolateOnBind = null;
        isolateOnBind = _ =>
        {
            channel.AggregateVolume.ValueChanged -= isolateOnBind!;
            bindChartAudioAdjustments(channel);
        };
        channel.AggregateVolume.ValueChanged += isolateOnBind;

        channel.AddAdjustment(AdjustableProperty.Frequency, pauseFrequency);
        activeChannels.Add(new ActiveChannel(channel));
    }

    public virtual void Pause()
    {
        foreach (var activeChannel in activeChannels)
        {
            if (activeChannel.Channel.IsDisposed || !activeChannel.Channel.Playing)
                continue;

            pauseFrequency.Value = 0;
            activeChannel.Paused = true;
        }
    }

    public virtual void Resume()
    {
        if (!RequestedPlaying || samplePlaybackDisabled.Value)
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
            bindChartAudioAdjustments(activeChannel.Channel);
            activeChannel.Paused = false;
        }
    }

    public virtual void Stop()
    {
        RequestedPlaying = false;

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

        var sample = getBeatmapSample(sampleInfo);

        if (sample == null)
            return;

        bindChartAudioAdjustments(sample);
        resolvedSample = new ResolvedSample(sampleInfo, sample);
    }

    private ISample? getBeatmapSample(ISampleInfo info)
    {
        foreach (var skin in CurrentSkin.AllSources.Select(extractBeatmapSkin).Where(s => s != null))
        {
            var sample = skin!.GetSample(info);

            if (sample != null)
                return sample;
        }

        return null;
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
            RequestedPlaying = false;
    }

    private void bindChartAudioAdjustments(IAdjustableAudioComponent component)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.AddAdjustment(AdjustableProperty.Volume, requestedVolume);

        if (audioManager != null)
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.AggregateVolume);
    }

    private sealed class ActiveChannel(SampleChannel channel)
    {
        public SampleChannel Channel { get; } = channel;

        public bool Paused { get; set; }
    }
}
