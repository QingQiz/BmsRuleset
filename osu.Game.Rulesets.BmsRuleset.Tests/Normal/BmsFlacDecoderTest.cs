using System;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Audio.Decoding;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsFlacDecoderTest
{
    private const string silent_flac = "ZkxhQwAAACICQAJAAAAMAAAMAfQA8AAAAFDLQV4FuFvjFJSuG8IzvrWLhAAALAwAAABMYXZmNjEuNy4xMDABAAAAFAAAAGVuY29kZXI9TGF2ZjYxLjcuMTAw//hkCABPCQAAAHyn";

    [Test]
    public void DecodesFlacToWave()
    {
        if (!BmsSupplementalFFmpegFuncs.TryCreate(out _, out var availabilityError))
            if (availabilityError != null)
                Assert.Ignore(availabilityError);

        Assert.That(BmsFlacDecoder.TryDecodeToWave(Convert.FromBase64String(silent_flac), out var wave, out var error), Is.True, error);
        var decoded = wave!;

        Assert.That(Encoding.ASCII.GetString(decoded, 0, 4), Is.EqualTo("RIFF"));
        Assert.That(decoded.Length, Is.GreaterThan(44));
        Assert.That(Encoding.ASCII.GetString(decoded, 8, 4), Is.EqualTo("WAVE"));
    }
}
