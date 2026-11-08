using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Video;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;
using osu.Framework.Timing;
using FrameworkVideo = osu.Framework.Graphics.Video.Video;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsBgaVideoFactoryTest
{
    // Content probing keeps loading independent of misleading suffixes in older packs.

    private static string fixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", name);

    private static string selectBackendName(string path)
    {
        using var stream = File.OpenRead(path);
        using var drawable = BmsBgaVideoFactory.Create(stream, new ManualFramedClock(), 0);
        return drawable switch
        {
            BmsSupplementalVideoDrawable => "supplemental-ffmpeg",
            FrameworkVideo => "framework",
            null => "missing",
            _ => throw new System.InvalidOperationException("Unexpected BGA video drawable."),
        };
    }

    [TestCase("mpeg1.mpg", Description = "Legacy MPEG-1 — supplemental owns (framework lacks the decoder)")]
    public void TestLegacyMpegRoutesToSupplemental(string fixture)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg backend is not embedded in this build.");

        Assert.That(selectBackendName(fixturePath(fixture)), Is.EqualTo("supplemental-ffmpeg"));
    }

    [TestCase("wmv1.wmv", Description = "WMV family the framework can't decode — claimed by codec probe, not extension")]
    public void TestWmvFamilyRoutesToSupplementalByCodec(string fixture)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg backend is not embedded in this build.");

        Assert.That(selectBackendName(fixturePath(fixture)), Is.EqualTo("supplemental-ffmpeg"));
    }

    [TestCase("cinepak.avi", Description = ".avi is codec-ambiguous — cinepak goes to supplemental, not framework")]
    public void TestAviLegacyCodecRoutesToSupplemental(string fixture)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg backend is not embedded in this build.");

        Assert.That(selectBackendName(fixturePath(fixture)), Is.EqualTo("supplemental-ffmpeg"));
    }

    [Test]
    public void TestWmv2StaysWithFramework()
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg backend is not embedded in this build.");

        Assert.That(selectBackendName(fixturePath("wmv2.wmv")), Is.EqualTo("framework"));
    }

    [TestCase("h264.mp4")]
    [TestCase("mpeg4.avi")]
    public void TestExistingFrameworkFormatsKeepTheirDecoder(string fixture)
        => Assert.That(selectBackendName(fixturePath(fixture)), Is.EqualTo("framework"));

    [TestCase("av1.mp4")]
    [TestCase("mjpeg.wmv")]
    [TestCase("rawvideo.avi")]
    [TestCase("dv.avi")]
    [TestCase("raw-h264.mpg")]
    public void TestExpandedFormatsDecodeFirstFrame(string fixture)
    {
        if (!BmsSupplementalFFmpegFuncs.IsAvailable)
            Assert.Ignore("Supplemental FFmpeg backend is not embedded in this build.");

        using var stream = File.OpenRead(fixturePath(fixture));
        using var drawable = BmsBgaVideoFactory.Create(stream, new ManualFramedClock(), 0);
        Assert.That(drawable, Is.TypeOf<BmsSupplementalVideoDrawable>());

        var bytes = File.ReadAllBytes(fixturePath(fixture));
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);
        using (decoder)
        {
            Assert.That(decoder!.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            using (frame)
                Assert.That(frame!.Width, Is.GreaterThan(0));
        }
    }

    [Test]
    public void TestUnsupportedContentFallsThroughWithOriginalStreamPosition()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        stream.Position = 2;
        using var drawable = BmsBgaVideoFactory.Create(stream, new ManualFramedClock(), 0);

        Assert.That(drawable, Is.TypeOf<FrameworkVideo>());
        Assert.That(stream.Position, Is.EqualTo(2));
    }

    [Test]
    public void TestLocalFrameworkVideoCorpus()
    {
        var manifest = Environment.GetEnvironmentVariable("BMS_BGA_FRAMEWORK_CORPUS");
        if (string.IsNullOrEmpty(manifest))
            Assert.Ignore("Set BMS_BGA_FRAMEWORK_CORPUS to a text file listing local video paths.");

        var count = 0;
        foreach (var path in File.ReadLines(manifest!))
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var drawable = BmsBgaVideoFactory.Create(stream, new ManualFramedClock(), 0);
            Assert.That(drawable, Is.TypeOf<FrameworkVideo>(), path);

            using var decoder = new VideoDecoder(new DummyRenderer(), stream);
            decoder.TargetHardwareVideoDecoders.Value = HardwareVideoDecoder.None;
            decoder.StartDecoding();
            DecodedFrame[] frames = [];
            Assert.That(SpinWait.SpinUntil(() =>
            {
                frames = decoder.GetDecodedFrames().ToArray();
                return frames.Length > 0 || decoder.IsFaulted;
            }, TimeSpan.FromSeconds(5)), Is.True, path);
            Assert.That(frames, Is.Not.Empty, path);
            decoder.ReturnFrames(frames);
            count++;
        }

        TestContext.Progress.WriteLine($"Framework decoded first frames from {count} local videos.");
        Assert.That(count, Is.GreaterThan(0));
    }
}
