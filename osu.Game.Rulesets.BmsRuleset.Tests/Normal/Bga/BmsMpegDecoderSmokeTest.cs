using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
#nullable enable
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Mpeg;
using PLMpegSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsMpegDecoderSmokeTest
{
    [Test]
    public void TestBundledMpegBgaDecodesFirstFrame()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        var player = new Player(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(player.Width, Is.EqualTo(480));
            Assert.That(player.Height, Is.EqualTo(480));
            Assert.That(player.Framerate, Is.GreaterThan(0));
        });

        for (int i = 0; i < 30; i++)
        {
            var frame = player.DecodeVideo();
            Assert.That(frame, Is.Not.Null);
            var decodedFrame = frame!;
            Assert.That(decodedFrame.Width, Is.EqualTo(480));
            Assert.That(decodedFrame.Height, Is.EqualTo(480));

            var pixels = new Rgba32[decodedFrame.Width * decodedFrame.Height];
            var rgbaBytes = new byte[decodedFrame.Width * decodedFrame.Height * 4];
            for (int j = 3; j < rgbaBytes.Length; j += 4)
                rgbaBytes[j] = 255;

            decodedFrame.ToRGBA(rgbaBytes, decodedFrame.Width * 4);
            MemoryMarshal.Cast<byte, Rgba32>(rgbaBytes).CopyTo(pixels);

            if (pixels.Any(p => p.R != 0 || p.G != 0 || p.B != 0))
                return;
        }

        Assert.Fail("The MPEG decoder did not produce a non-black frame in the first 30 frames.");
    }

    [Test]
    public void TestDecoderAssemblyIsNotDiscoverableAsRuleset()
    {
        Assert.That(typeof(Player).Assembly, Is.SameAs(typeof(BmsRuleset).Assembly));

        var referencedAssemblyNames = typeof(BmsRuleset).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        Assert.That(referencedAssemblyNames, Does.Not.Contain("PLMpegSharp"));
    }

    [Test]
    public void TestAlephMpegBgaDecodesFirstFrame()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("Aleph-0 (by LeaF)", "_bga.mpg"));
        Assert.That(BmsMpegVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using (var activeDecoder = decoder!)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            Assert.That(frame, Is.Not.Null);
            var decodedFrame = frame!;
            Assert.That(decodedFrame.Width, Is.GreaterThan(0));
            Assert.That(decodedFrame.Height, Is.GreaterThan(0));
            decodedFrame.Dispose();
        }
    }

    [Test]
    public void TestWrapperRentsRgbaFrameAndReturnsUpload()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        Assert.That(BmsMpegVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using (var activeDecoder = decoder!)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            Assert.That(frame, Is.Not.Null);
            var decodedFrame = frame!;
            Assert.That(decodedFrame.Width, Is.EqualTo(480));
            Assert.That(decodedFrame.Height, Is.EqualTo(480));
            Assert.That(decodedFrame.PixelCount, Is.EqualTo(480 * 480));

            using var upload = decodedFrame.CreateUpload();
            Assert.That(upload.Bounds.Width, Is.EqualTo(480));
            Assert.That(upload.Bounds.Height, Is.EqualTo(480));
            Assert.That(upload.Data.Length, Is.EqualTo(480 * 480));
            Assert.That(upload.Data.ToArray().Any(p => p.A == 255), Is.True);
        }
    }

    [Test]
    public void TestWrapperRejectsMalformedData()
    {
        Assert.That(BmsMpegVideoDecoder.TryCreate([1, 2, 3, 4], out var decoder, out var error), Is.False);
        Assert.That(decoder, Is.Null);
        Assert.That(error, Does.Contain("MPEG"));
    }

    [Test]
    public void TestPlayerParsesRawMpeg1VideoElementaryStream()
    {
        // Some BGA files (e.g. B.B.K.K.B.K.K/01.mpg) are raw MPEG-1 video elementary
        // streams: they begin with the sequence-header start code (00 00 01 B3) and carry
        // no MPEG-PS Pack/System wrapper (00 00 01 BA/BB) that the Demuxer requires. With
        // only a sequence header and no picture data this still proves the Player falls
        // back to the raw-ES path and parses dimensions/framerate instead of faulting.
        var bytes = makeRawMpeg1ElementaryStream();

        var player = new Player(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(player.Width, Is.EqualTo(256));
            Assert.That(player.Height, Is.EqualTo(256));
            Assert.That(player.Framerate, Is.EqualTo(30.0));
        });
    }

    [Test]
    public void TestWrapperAcceptsRawMpeg1VideoElementaryStream()
    {
        var bytes = makeRawMpeg1ElementaryStream();

        Assert.That(BmsMpegVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);
        Assert.That(decoder, Is.Not.Null);
        decoder!.Dispose();
    }

    [Test]
    public void TestFrameSourceDecodesOnWorkerAndReturnsLatestFrame()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0.5);

        BmsMpegVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();

        var stats = source.Stats;
        Assert.That(stats.DecodedFrames, Is.GreaterThan(0));
        Assert.That(stats.IsFaulted, Is.False);
    }

    [Test]
    public void TestFrameSourceDecodesAlephOnWorkerAndReturnsLatestFrame()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("Aleph-0 (by LeaF)", "_bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0);

        BmsMpegVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();

        var stats = source.Stats;
        Assert.That(stats.DecodedFrames, Is.GreaterThan(0));
        Assert.That(stats.IsFaulted, Is.False);
    }

    [Test]
    public void TestFrameSourceDoesNotReturnFramesAheadOfTargetTime()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes, maxQueuedFrames: 10);

        source.Start();
        source.SetTargetTime(0);

        Assert.That(waitUntil(() => source.Stats.DecodedFrames >= 3, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(source.TryTakeLatestFrame(out var frame), Is.True);
        Assert.That(frame!.Time, Is.LessThanOrEqualTo(0));
        frame.Dispose();
    }

    [Test]
    public void TestFrameSourceAdvancesWhenTargetTimeIncreasesGradually()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes, maxQueuedFrames: 3);

        source.Start();

        double lastFrameTime = double.NegativeInfinity;
        var uploadedFrameTimes = new List<double>();

        for (int i = 0; i < 12; i++)
        {
            source.SetTargetTime(i / 30.0);
            Thread.Sleep(20);

            if (!source.TryTakeLatestFrame(out var frame))
                continue;

            try
            {
                Assert.That(frame!.Time, Is.GreaterThanOrEqualTo(lastFrameTime));
                Assert.That(frame.Time, Is.LessThanOrEqualTo(i / 30.0));
                lastFrameTime = frame.Time;
                uploadedFrameTimes.Add(frame.Time);
            }
            finally
            {
                frame?.Dispose();
            }
        }

        Assert.That(uploadedFrameTimes.Distinct().Count(), Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public void TestFrameSourceDropsStaleFrames()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes, maxQueuedFrames: 1);

        source.Start();
        source.SetTargetTime(2.0);

        Assert.That(waitUntil(() => source.Stats.DecodedFrames >= 3, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(source.Stats.DroppedFrames, Is.GreaterThan(0));
    }

    [Test]
    public void TestFrameSourceReturnsInitialFrameWhenTargetAlreadyAhead()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsMpegVideoFrameSource(bytes, maxQueuedFrames: 1);

        source.SetTargetTime(5.0);
        source.Start();

        BmsMpegVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
        Assert.That(source.Stats.IsFaulted, Is.False);
    }

    [Test]
    public void TestDrawableReadsSeekableStreamFromBeginning()
    {
        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var stream = new MemoryStream(bytes);
        stream.Position = stream.Length;

        using var drawable = new BmsMpegVideoDrawable(stream, new ManualClock(), 0);
        typeof(BmsMpegVideoDrawable).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawable, [null]);

        Assert.That(waitUntil(() => drawable.Stats.DecodedFrames > 0 || drawable.Stats.IsFaulted, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(drawable.Stats.IsFaulted, Is.False, drawable.Stats.FaultMessage);
        Assert.That(drawable.Stats.DecodedFrames, Is.GreaterThan(0));
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

    private static bool waitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var limit = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < limit)
        {
            if (condition())
                return true;

            Thread.Sleep(10);
        }

        return false;
    }

    // Minimal raw MPEG-1 video elementary stream: a sequence header (start code 00 00 01 B3)
    // describing 256x256 @ 30fps with default quantiser matrices, then zero padding. There
    // are no MPEG-PS Pack/System headers and no picture data — enough to exercise the raw-ES
    // detection path without shipping a real (copyrighted) BGA fixture alongside the tests.
    private static byte[] makeRawMpeg1ElementaryStream()
    {
        var bytes = new byte[256];
        bytes[0] = 0x00; bytes[1] = 0x00; bytes[2] = 0x01; bytes[3] = 0xB3; // sequence_header_code
        bytes[4] = 0x10; bytes[5] = 0x01; bytes[6] = 0x00;                  // horizontal=256, vertical=256
        bytes[7] = 0x15;                                                    // aspect_ratio=1, frame_rate_code=5 (30fps)
        return bytes;
    }

}
