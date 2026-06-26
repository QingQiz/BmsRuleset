using System;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class TestBmsWavEncoder
{

    private static string ascii(byte[] b, int offset, int count)
        => Encoding.ASCII.GetString(b, offset, count);

    [Test]
    public void Encode_ProducesValidFloatWavHeader()
    {
        // 100 mono float samples = 400 bytes of PCM.
        var pcm = new byte[400];
        var wav = BmsWavEncoder.Encode(pcm, 44100, 1);

        Assert.That(ascii(wav, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(BitConverter.ToInt32(wav, 4), Is.EqualTo(36 + 400)); // riff size
        Assert.That(ascii(wav, 8, 4), Is.EqualTo("WAVE"));
        Assert.That(ascii(wav, 12, 4), Is.EqualTo("fmt "));
        Assert.That(BitConverter.ToInt32(wav, 16), Is.EqualTo(16));        // fmt subchunk size
        Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(3));        // WAVE_FORMAT_IEEE_FLOAT
        Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1));        // channels
        Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(44100));    // sample rate
        Assert.That(BitConverter.ToInt32(wav, 28), Is.EqualTo(44100 * 4));// byte rate
        Assert.That(BitConverter.ToInt16(wav, 32), Is.EqualTo(4));        // block align
        Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(32));       // bits per sample
        Assert.That(ascii(wav, 36, 4), Is.EqualTo("data"));
        Assert.That(BitConverter.ToInt32(wav, 40), Is.EqualTo(400));      // data size
        Assert.That(wav.Length, Is.EqualTo(44 + 400));
    }
}
