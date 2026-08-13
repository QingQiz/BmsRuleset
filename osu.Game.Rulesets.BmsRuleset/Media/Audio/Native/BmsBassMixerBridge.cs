using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using ManagedBass;
using ManagedBass.Mix;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;

internal sealed class BmsBassMixerBridge : IDisposable
{
    private readonly AudioMixer mixer;
    private readonly BmsPcmVoiceMixer voiceMixer;
    private readonly PropertyInfo handleProperty;
    private readonly MethodInfo enqueueActionMethod;
    private readonly StreamProcedure streamProcedure;
    private readonly object lifecycleLock = new();

    private Task pendingLifecycleAction = Task.CompletedTask;
    private int streamHandle;
    private int attachedMixerHandle;
    private bool disposed;

    internal BmsBassMixerBridge(AudioMixer mixer, BmsPcmVoiceMixer voiceMixer)
    {
        ArgumentNullException.ThrowIfNull(mixer);
        ArgumentNullException.ThrowIfNull(voiceMixer);

        var mixerType = typeof(AudioMixer).Assembly.GetType("osu.Framework.Audio.Mixing.Bass.BassAudioMixer")
                        ?? throw new NotSupportedException("The framework BASS mixer type is unavailable.");
        handleProperty = AccessTools.Property(mixerType, "Handle")
                         ?? throw new NotSupportedException("The framework BASS mixer handle is unavailable.");
        enqueueActionMethod = AccessTools.Method(typeof(AudioComponent), "EnqueueAction", [typeof(Action)])
                              ?? throw new NotSupportedException("The framework audio action queue is unavailable.");

        if (!mixerType.IsInstanceOfType(mixer))
            throw new NotSupportedException("The BMS PCM bridge requires framework's BASS mixer.");

        this.mixer = mixer;
        this.voiceMixer = voiceMixer;
        streamProcedure = render;
    }

    internal void EnsureAttached()
    {
        lock (lifecycleLock)
        {
            if (disposed || !pendingLifecycleAction.IsCompleted)
                return;

            var currentHandle = getMixerHandle();
            if (currentHandle != 0 && currentHandle == attachedMixerHandle && streamHandle != 0)
                return;

            pendingLifecycleAction = enqueueAudioAction(attachToCurrentMixer);
        }
    }

    public void Dispose()
    {
        lock (lifecycleLock)
        {
            if (disposed)
                return;

            disposed = true;
            pendingLifecycleAction = enqueueAudioAction(detachAndFree);
        }
    }

    private void attachToCurrentMixer()
    {
        if (disposed)
            return;

        var currentMixerHandle = getMixerHandle();
        if (currentMixerHandle == 0)
            return;

        if (streamHandle == 0)
        {
            streamHandle = Bass.CreateStream(
                BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE,
                BmsFixedRatePcmProcessor.OUTPUT_CHANNELS,
                BassFlags.Float | BassFlags.Decode,
                streamProcedure,
                IntPtr.Zero);

            if (streamHandle == 0)
                throw new InvalidOperationException($"BASS failed to create the BMS PCM stream: {Bass.LastError}.");
        }

        if (attachedMixerHandle != 0)
            BassMix.MixerRemoveChannel(streamHandle);

        if (!BassMix.MixerAddChannel(currentMixerHandle, streamHandle, BassFlags.MixerChanBuffer | BassFlags.MixerChanNoRampin))
            throw new InvalidOperationException($"BASSmix failed to attach the BMS PCM stream: {Bass.LastError}.");

        attachedMixerHandle = currentMixerHandle;
    }

    private void detachAndFree()
    {
        if (streamHandle == 0)
            return;

        if (attachedMixerHandle != 0)
            BassMix.MixerRemoveChannel(streamHandle);

        Bass.StreamFree(streamHandle);
        streamHandle = 0;
        attachedMixerHandle = 0;
    }

    private int getMixerHandle() => handleProperty.GetValue(mixer) as int? ?? 0;

    private Task enqueueAudioAction(Action action) =>
        enqueueActionMethod.Invoke(mixer, [action]) as Task ?? Task.CompletedTask;

    private unsafe int render(int handle, IntPtr buffer, int length, IntPtr user)
    {
        try
        {
            var sampleCount = length / sizeof(float);
            var samples = new Span<float>((void*)buffer, sampleCount);

            if (sampleCount % BmsFixedRatePcmProcessor.OUTPUT_CHANNELS != 0)
                samples.Clear();
            else
                voiceMixer.Render(samples);

            return sampleCount * sizeof(float);
        }
        catch
        {
            new Span<byte>((void*)buffer, length).Clear();
            return length;
        }
    }
}
