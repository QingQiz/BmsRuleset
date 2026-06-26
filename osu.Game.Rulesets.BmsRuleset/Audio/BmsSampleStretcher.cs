using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Pre-renders a pitch-preserving time-stretch of a source audio file at load time, exposing
///     the result as a framework <see cref="ISample"/>. Runtime SampleChannel playback then plays
///     the already-sped-up sample at native rate — no per-playback tempo overhead, and no framework
///     internals touched (sample channels cannot pitch-preserve at runtime: SampleChannelBass only
///     applies AggregateFrequency, never AggregateTempo).
/// </summary>
/// <remarks>
///     Uses BASS_FX tempo (the same WSOLA time-stretcher osu!'s TrackBass uses) once per unique
///     sample at load, renders the stretched float PCM, re-encodes to WAV, and feeds it back
///     through an in-memory ISampleStore so the framework decodes it into a normal ISample.
/// </remarks>
public sealed class BmsSampleStretcher : IDisposable
{
    private readonly Dictionary<string, byte[]> stretchedWav = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISampleStore stretchedSampleStore;

    public BmsSampleStretcher(AudioManager audioManager)
    {
        var stretchedByteStore1 = new ResourceStore<byte[]>(new MemoryByteStore(stretchedWav));
        stretchedByteStore1.AddExtension("wav");
        stretchedSampleStore = audioManager.GetSampleStore(stretchedByteStore1);
    }

    #region Disposal

    public void Dispose()
    {
        stretchedSampleStore.Dispose();
        stretchedWav.Clear();
    }

    #endregion

    /// <summary>
    ///     Stretches the sample resolved as <paramref name="lookupName"/> from <paramref name="source"/>
    ///     by <paramref name="rate"/> (pitch-preserving). Returns null if the source is absent or
    ///     BASS processing fails.
    /// </summary>
    public ISample? Stretch(IResourceStore<byte[]> source, string lookupName, double rate)
    {
        var src = resolveSourceBytes(source, lookupName);
        if (src == null)
            return null;

        var wav = renderStretchedWav(src, rate);
        if (wav == null)
            return null;

        stretchedWav[lookupName] = wav;
        return stretchedSampleStore.Get(lookupName);
    }

    private static byte[]? resolveSourceBytes(IResourceStore<byte[]> source, string path)
    {
        foreach (var lookup in new BmsSampleInfo(path).LookupNames)
        {
            var b = source.Get(lookup);
            if (b != null)
                return b;
        }

        return null;
    }

    private static byte[]? renderStretchedWav(byte[] src, double rate)
    {
        var pin = GCHandle.Alloc(src, GCHandleType.Pinned);
        var decodeStream = 0;
        var tempoStream = 0;

        try
        {
            decodeStream = Bass.CreateStream(pin.AddrOfPinnedObject(), 0, src.Length, BassFlags.Decode | BassFlags.Float);
            if (decodeStream == 0)
                return null;

            tempoStream = BassFx.TempoCreate(decodeStream, BassFlags.Decode | BassFlags.FxFreeSource);
            if (tempoStream == 0)
                return null;

            // TempoCreate with FxFreeSource owns decodeStream now.
            decodeStream = 0;

            if (!Bass.ChannelSetAttribute(tempoStream, ChannelAttribute.Tempo, (rate - 1) * 100))
                return null;

            if (!Bass.ChannelGetInfo(tempoStream, out ChannelInfo info))
                return null;

            var pcm = readFloatPcm(tempoStream);
            if (pcm == null)
                return null;

            return BmsWavEncoder.Encode(pcm, info.Frequency, info.Channels);
        }
        finally
        {
            if (tempoStream != 0) Bass.StreamFree(tempoStream);
            if (decodeStream != 0) Bass.StreamFree(decodeStream);
            pin.Free();
        }
    }

    private static byte[]? readFloatPcm(int stream)
    {
        var result = new MemoryStream();
        var buffer = new float[8192];
        var chunkBytes = buffer.Length * 4;

        while (true)
        {
            var read = Bass.ChannelGetData(stream, buffer, chunkBytes);
            if (read <= 0)
                break;

            // read is in bytes; copy that many bytes out of the float buffer.
            var asBytes = new byte[read];
            Buffer.BlockCopy(buffer, 0, asBytes, 0, read);
            result.Write(asBytes, 0, read);

            if (read < chunkBytes)
                break;
        }

        return result.Length == 0 ? null : result.ToArray();
    }
}
