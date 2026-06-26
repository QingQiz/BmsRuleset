using System.IO;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Encodes raw 32-bit float PCM into a WAV byte[] (WAVE_FORMAT_IEEE_FLOAT).
///     Used to feed BASS-rendered stretched PCM back through the framework's ISampleStore,
///     which only decodes file-format bytes (WAV/MP3/OGG) — there is no public API to build
///     a framework ISample from raw PCM or a BASS handle (SampleBass is internal).
/// </summary>
public static class BmsWavEncoder
{
    public static byte[] Encode(byte[] floatPcm, int sampleRate, int channels)
    {
        const int bits_per_sample = 32;
        var byteRate = sampleRate * channels * bits_per_sample / 8;
        var blockAlign = channels * bits_per_sample / 8;
        var dataSize = floatPcm.Length;

        using var ms = new MemoryStream(44 + dataSize);
        using var bw = new BinaryWriter(ms);

        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8.ToArray());

        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)3);              // WAVE_FORMAT_IEEE_FLOAT
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bits_per_sample);

        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
        bw.Write(floatPcm);

        return ms.ToArray();
    }
}
