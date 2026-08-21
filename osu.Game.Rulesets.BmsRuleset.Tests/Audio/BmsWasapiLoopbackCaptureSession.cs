#nullable enable

using System;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Wasapi;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

internal sealed class BmsWasapiLoopbackCaptureSession : IDisposable
{
    private static readonly WasapiProcedure capture_procedure = capture;

    private readonly int deviceIndex;
    private readonly CaptureState state;
    private bool disposed;

    public int SampleRate { get; }
    public int Channels { get; }

    private BmsWasapiLoopbackCaptureSession(int deviceIndex, int sampleRate, int channels, int maximumFrames)
    {
        this.deviceIndex = deviceIndex;
        SampleRate = sampleRate;
        Channels = channels;
        state = new CaptureState(new float[checked(maximumFrames * channels)]);
        state.Handle = GCHandle.Alloc(state);

        if (!BassWasapi.Init(
                deviceIndex,
                sampleRate,
                channels,
                WasapiInitFlags.Shared,
                0.1f,
                0,
                capture_procedure,
                GCHandle.ToIntPtr(state.Handle)))
        {
            state.Handle.Free();
            throw new InvalidOperationException($"Failed to initialise WASAPI loopback device {deviceIndex} ({Bass.LastError}).");
        }

        if (!BassWasapi.Start())
        {
            BassWasapi.Free();
            state.Handle.Free();
            throw new InvalidOperationException($"Failed to start WASAPI loopback device {deviceIndex} ({Bass.LastError}).");
        }
    }

    public static BmsWasapiLoopbackCaptureSession Start(string device, TimeSpan maximumDuration)
    {
        var deviceIndex = findDevice(device);
        if (!BassWasapi.GetDeviceInfo(deviceIndex, out var info)
            || !info.IsEnabled
            || !info.IsLoopback
            || info.MixFrequency <= 0
            || info.MixChannels <= 0)
            throw new InvalidOperationException($"WASAPI device '{device}' is not an enabled loopback endpoint.");

        var maximumFrames = checked((int)Math.Ceiling(maximumDuration.TotalSeconds * info.MixFrequency));
        return new BmsWasapiLoopbackCaptureSession(deviceIndex, info.MixFrequency, info.MixChannels, maximumFrames);
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
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = checked((samples.Length - skipSamples) * sizeof(float));

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
        BassWasapi.CurrentDevice = deviceIndex;
        BassWasapi.Stop();
        BassWasapi.Free();

        if (state.Handle.IsAllocated)
            state.Handle.Free();
    }

    private static int findDevice(string requested)
    {
        if (int.TryParse(requested, out var requestedIndex))
            return requestedIndex;

        for (var index = 0; BassWasapi.GetDeviceInfo(index, out var info); index++)
        {
            if (info.IsLoopback && info.Name.Contains(requested, StringComparison.OrdinalIgnoreCase))
                return index;
        }

        throw new InvalidOperationException($"No WASAPI loopback endpoint matches '{requested}'.");
    }

    private static unsafe int capture(IntPtr buffer, int length, IntPtr user)
    {
        var handle = GCHandle.FromIntPtr(user);
        if (handle.Target is not CaptureState state)
            return 0;

        var source = new ReadOnlySpan<float>((void*)buffer, length / sizeof(float));
        var destinationOffset = state.SampleCount;
        var writable = Math.Min(source.Length, state.Samples.Length - destinationOffset);

        if (writable > 0)
            source[..writable].CopyTo(state.Samples.AsSpan(destinationOffset));

        state.SampleCount = Math.Min(state.Samples.Length, destinationOffset + source.Length);
        return 1;
    }

    private sealed class CaptureState(float[] samples)
    {
        public readonly float[] Samples = samples;
        public GCHandle Handle;
        public int SampleCount;
    }
}
