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
///     Skinnable BMS sample playback which treats chart samples as chart audio, not osu! effects.
/// </summary>
public partial class BmsSkinnableSound : SkinReloadableDrawable
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

    private readonly record struct ResolvedSample(ISampleInfo Info, ISample Sample);

    private ISampleInfo? sampleInfo;
    private ResolvedSample? resolvedSample;

    [Resolved(CanBeNull = true)]
    private AudioManager? audioManager { get; set; }

    public BmsSkinnableSound()
    {
    }

    public BmsSkinnableSound(ISampleInfo sample)
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

        FlushPendingSkinChanges();
        cleanupStoppedChannels();

        if (resolvedSample is not { } resolved)
            return;

        bindToUniversalVolume(resolved.Sample);

        var channel = resolved.Sample.GetChannel();
        channel.ManualFree = true;
        channel.Volume.Value = Math.Max(0, resolved.Info.Volume) / 100.0;
        channel.Play();
        bindToUniversalVolume(channel);

        activeChannels.Add(new ActiveChannel(channel));
    }

    public virtual void Pause()
    {
        foreach (var activeChannel in activeChannels)
        {
            if (activeChannel.Channel.IsDisposed || !activeChannel.Channel.Playing)
                continue;

            activeChannel.Channel.Stop();
            activeChannel.Paused = true;
        }
    }

    public virtual void Resume()
    {
        if (!RequestedPlaying || samplePlaybackDisabled.Value)
            return;

        FlushPendingSkinChanges();

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
            bindToUniversalVolume(activeChannel.Channel);
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

        var sample = CurrentSkin.GetSample(sampleInfo);

        if (sample == null)
            return;

        bindToUniversalVolume(sample);
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
            RequestedPlaying = false;
    }

    private void bindToUniversalVolume(IAdjustableAudioComponent component)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);

        if (audioManager != null)
            component.AddAdjustment(AdjustableProperty.Volume, audioManager.Volume);
    }

    private sealed class ActiveChannel(SampleChannel channel)
    {
        public SampleChannel Channel { get; } = channel;

        public bool Paused { get; set; }
    }
}
