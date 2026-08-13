#nullable enable

using System;
using System.IO;
using System.Reflection;
using ManagedBass;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal sealed class BmsAudioCaptureSession : IDisposable
{
    private const int capture_dsp_priority = -1000;
    private const int mute_dsp_priority = -2000;

    private static readonly DSPProcedure capture_dsp = captureDsp;
    private static readonly DSPProcedure mute_dsp = muteDsp;

    private readonly int channelHandle;
    private readonly CaptureState state;
    private readonly int dspHandle;
    private readonly int muteDspHandle;
    private bool disposed;

    public int SampleRate { get; }
    public int Channels { get; }
    public int FrameCount => state.SampleCount / Channels;

    private BmsAudioCaptureSession(int channelHandle, int sampleRate, int channels, int maximumFrames, bool muteOutput, int capturePriority)
    {
        this.channelHandle = channelHandle;
        SampleRate = sampleRate;
        Channels = channels;
        state = new CaptureState(new float[checked(maximumFrames * channels)]);

        var stateHandle = System.Runtime.InteropServices.GCHandle.Alloc(state);
        state.StateHandle = stateHandle;
        dspHandle = Bass.ChannelSetDSP(channelHandle, capture_dsp, System.Runtime.InteropServices.GCHandle.ToIntPtr(stateHandle), capturePriority);

        if (dspHandle == 0)
        {
            stateHandle.Free();
            throw new InvalidOperationException($"Failed to install audio capture DSP ({Bass.LastError}).");
        }

        if (muteOutput)
        {
            muteDspHandle = Bass.ChannelSetDSP(channelHandle, mute_dsp, IntPtr.Zero, mute_dsp_priority);

            if (muteDspHandle == 0)
            {
                Bass.ChannelRemoveDSP(channelHandle, dspHandle);
                stateHandle.Free();
                throw new InvalidOperationException($"Failed to install diagnostic mute DSP ({Bass.LastError}).");
            }
        }
    }

    public static BmsAudioCaptureSession Start(AudioMixer mixer, TimeSpan maximumDuration, bool muteOutput, int capturePriority = capture_dsp_priority)
    {
        var handle = getMixerHandle(mixer);

        if (handle == 0 || !Bass.ChannelGetInfo(handle, out var info))
            throw new InvalidOperationException($"The active audio mixer has no valid BASS channel ({Bass.LastError}).");

        var maximumFrames = checked((int)Math.Ceiling(maximumDuration.TotalSeconds * info.Frequency));
        return new BmsAudioCaptureSession(handle, info.Frequency, info.Channels, maximumFrames, muteOutput, capturePriority);
    }

    public static (BmsAudioCaptureSession Before, BmsAudioCaptureSession After) StartPair(
        AudioMixer mixer,
        TimeSpan maximumDuration,
        bool muteOutput)
    {
        var handle = getMixerHandle(mixer);
        return startPair(handle, maximumDuration, muteOutput);
    }

    public static BmsAudioCaptureSession StartGlobal(AudioManager audioManager, TimeSpan maximumDuration, bool muteOutput, int capturePriority = capture_dsp_priority)
    {
        var handle = getGlobalMixerHandle(audioManager);
        if (!Bass.ChannelGetInfo(handle, out var info))
            throw new InvalidOperationException("The Windows shared-mode global mixer is unavailable.");

        var maximumFrames = checked((int)Math.Ceiling(maximumDuration.TotalSeconds * info.Frequency));
        return new BmsAudioCaptureSession(handle, info.Frequency, info.Channels, maximumFrames, muteOutput, capturePriority);
    }

    public static (BmsAudioCaptureSession Before, BmsAudioCaptureSession After) StartGlobalPair(
        AudioManager audioManager,
        TimeSpan maximumDuration,
        bool muteOutput) =>
        startPair(getGlobalMixerHandle(audioManager), maximumDuration, muteOutput);

    private static (BmsAudioCaptureSession Before, BmsAudioCaptureSession After) startPair(
        int handle,
        TimeSpan maximumDuration,
        bool muteOutput)
    {
        if (!Bass.ChannelGetInfo(handle, out var info))
            throw new InvalidOperationException($"The capture channel is unavailable ({Bass.LastError}).");

        var maximumFrames = checked((int)Math.Ceiling(maximumDuration.TotalSeconds * info.Frequency));
        if (!Bass.ChannelLock(handle))
            throw new InvalidOperationException($"Failed to lock the capture channel ({Bass.LastError}).");

        BmsAudioCaptureSession? before = null;

        try
        {
            before = new BmsAudioCaptureSession(handle, info.Frequency, info.Channels, maximumFrames, false, 1000);
            var after = new BmsAudioCaptureSession(handle, info.Frequency, info.Channels, maximumFrames, muteOutput, capture_dsp_priority);
            return (before, after);
        }
        catch
        {
            before?.Dispose();
            throw;
        }
        finally
        {
            Bass.ChannelLock(handle, false);
        }
    }

    private static int getMixerHandle(AudioMixer mixer)
    {
        var handleProperty = mixer.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                             ?? throw new InvalidOperationException("The active audio mixer does not expose a BASS handle.");
        return (int)(handleProperty.GetValue(mixer) ?? 0);
    }

    private static int getGlobalMixerHandle(AudioManager audioManager)
    {
        var field = typeof(AudioManager).GetField("GlobalMixerHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("The audio manager does not expose its global mixer handle.");

        return field.GetValue(audioManager) is IBindable<int?> { Value: int handle }
            ? handle
            : throw new InvalidOperationException("The Windows shared-mode global mixer is unavailable.");
    }

    public float[] GetSamples()
    {
        var count = Math.Min(state.SampleCount, state.Samples.Length);
        var result = new float[count];
        Array.Copy(state.Samples, result, count);
        return result;
    }

    public void WriteWave(string path, int skipFrames = 0)
    {
        var samples = GetSamples();
        var skipSamples = Math.Clamp(skipFrames * Channels, 0, samples.Length);
        var outputSamples = samples.Length - skipSamples;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = checked(outputSamples * sizeof(float));

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)3);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * sizeof(float));
        writer.Write((short)(Channels * sizeof(float)));
        writer.Write((short)(sizeof(float) * 8));
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (var i = skipSamples; i < samples.Length; i++)
            writer.Write(samples[i]);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        if (muteDspHandle != 0)
            Bass.ChannelRemoveDSP(channelHandle, muteDspHandle);

        Bass.ChannelRemoveDSP(channelHandle, dspHandle);

        if (state.StateHandle.IsAllocated)
            state.StateHandle.Free();
    }

    private static unsafe void captureDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        var stateHandle = System.Runtime.InteropServices.GCHandle.FromIntPtr(user);
        if (stateHandle.Target is not CaptureState capture)
            return;

        var source = new ReadOnlySpan<float>((void*)buffer, length / sizeof(float));
        var destinationOffset = capture.SampleCount;
        var writable = Math.Min(source.Length, capture.Samples.Length - destinationOffset);

        if (writable > 0)
            source[..writable].CopyTo(capture.Samples.AsSpan(destinationOffset));

        capture.SampleCount = Math.Min(capture.Samples.Length, destinationOffset + source.Length);
    }

    private static unsafe void muteDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user) =>
        new Span<byte>((void*)buffer, length).Clear();

    private sealed class CaptureState(float[] samples)
    {
        public readonly float[] Samples = samples;
        public System.Runtime.InteropServices.GCHandle StateHandle;
        public int SampleCount;
    }
}
