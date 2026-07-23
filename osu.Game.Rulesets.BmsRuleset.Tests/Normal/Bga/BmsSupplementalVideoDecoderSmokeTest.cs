using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalVideoDecoderSmokeTest
{
    [Test]
    public void TestMalformedDataIsRejected()
    {
        Assert.That(BmsSupplementalVideoDecoder.TryCreate([1, 2, 3, 4], out var decoder, out var error), Is.False);
        Assert.That(decoder, Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void TestBundledMpegBgaDecodesFirstFrame()
    {
        requireNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using var activeDecoder = decoder!;
        for (int i = 0; i < 30; i++)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            Assert.That(frame, Is.Not.Null);

            using var upload = frame!.CreateUpload();
            if (upload.Data.ToArray().Any(p => p.R != 0 || p.G != 0 || p.B != 0))
                return;
        }

        Assert.Fail("The supplemental FFmpeg decoder did not produce a non-black frame in the first 30 frames.");
    }

    private static void requireNativeArtifacts()
    {
        if (!BmsSupplementalFFmpegFuncs.TryCreate(out _, out var error))
            Assert.Ignore(error);
    }

    private static string locateTestSongFile(string songFolder, string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "bms_test_songs", songFolder, fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{songFolder}/{fileName} was not found from {AppContext.BaseDirectory}");
    }
}
