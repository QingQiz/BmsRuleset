#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalVideoPipelineTest
{

    private static string locateTestSongFile(string songFolder, string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "bms_test_songs", songFolder, fileName);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"{songFolder}/{fileName} was not found from {AppContext.BaseDirectory}");
    }

    private static void requireSupplementalNativeArtifacts()
    {
        if (!BmsSupplementalFFmpegFuncs.TryCreate(out _, out var error))
            Assert.Ignore(error ?? "Supplemental FFmpeg native artifacts are unavailable.");
    }

    private static BmsSupplementalVideoFrameSource createFrameSource(byte[] bytes)
    {
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);
        return new BmsSupplementalVideoFrameSource(decoder!);
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

    private static bool waitForFrame(BmsSupplementalVideoFrameSource source, TimeSpan timeout, out BmsSupplementalVideoFrame? frame)
    {
        var limit = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < limit)
        {
            if (source.TryTakeLatestFrame(out frame))
                return true;

            Thread.Sleep(10);
        }

        frame = null;
        return false;
    }

    private static bool waitForQueuedFrames(BmsSupplementalVideoFrameSource source, int minimumCount, TimeSpan timeout) =>
        waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) >= minimumCount, timeout);

    [Test]
    public void TestProviderTransfersOpenedDecoderWithoutReadingStreamAgain()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var stream = new CountingCopyStream(bytes);
        stream.Position = stream.Length;

        var request = new BmsBgaVideoRequest("bga.mpg", stream, new ManualFramedClock(), 0);
        Assert.That(new SupplementalBmsBgaVideoProvider().TryCreate(request, out var created), Is.True);
        using var drawable = (BmsSupplementalVideoDrawable)created!;
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        typeof(BmsSupplementalVideoDrawable).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawable, [null]);

        var source = BmsVideoTestAccess.GetFrameSource(drawable)!;
        Assert.That(waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) > 0, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(stream.CopyCount, Is.EqualTo(1));
    }

    [Test]
    public void TestOpenedDecoderIsReleasedWhenSourceNeverStarts()
    {
        requireSupplementalNativeArtifacts();
        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);
        var source = new BmsSupplementalVideoFrameSource(decoder!);

        source.Dispose();

        Assert.That(typeof(BmsSupplementalVideoDecoder).GetField("disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(decoder), Is.True);
        Assert.DoesNotThrow(source.Dispose);
    }

    [Test]
    public void TestRejectedCodecLeavesStreamPositionForTheNextProvider()
    {
        requireSupplementalNativeArtifacts();
        using var stream = new MemoryStream([1, 2, 3, 4]);
        stream.Position = 2;

        var accepted = new SupplementalBmsBgaVideoProvider().TryCreate(
            new BmsBgaVideoRequest("invalid.avi", stream, new ManualFramedClock(), 0), out var drawable);

        Assert.That(accepted, Is.False);
        Assert.That(drawable, Is.Null);
        Assert.That(stream.Position, Is.EqualTo(2));
    }

    [Test]
    public void TestFrameSourceAdvancesWhenTargetTimeIncreasesGradually()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = createFrameSource(bytes);

        source.Start();

        var lastFrameTime = double.NegativeInfinity;
        var uploadedFrameTimes = new List<double>();

        for (var i = 0; i < 12; i++)
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
    public void TestFrameSourceDecodesAlephOnWorkerAndReturnsLatestFrame()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("Aleph-0 (by LeaF)", "_bga.mpg"));
        using var source = createFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0);

        Assert.That(waitForFrame(source, TimeSpan.FromSeconds(3), out var frame), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestFrameSourceDecodesOnWorkerAndReturnsLatestFrame()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = createFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0.5);

        Assert.That(waitForFrame(source, TimeSpan.FromSeconds(3), out var frame), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestFrameSourceDisposalWaitsForWorker()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        var source = createFrameSource(bytes);

        source.Start();
        Assert.That(waitForQueuedFrames(source, 1, TimeSpan.FromSeconds(3)), Is.True);

        source.Dispose();

        Assert.That(BmsVideoTestAccess.IsWorkerCompleted(source), Is.True);
        Assert.DoesNotThrow(source.Dispose);
    }

    [Test]
    public void TestFrameSourceDoesNotReturnFramesAheadOfTargetTime()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = createFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0);

        Assert.That(waitForQueuedFrames(source, 3, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(source.TryTakeLatestFrame(out var frame), Is.True);
        Assert.That(frame!.Time, Is.LessThanOrEqualTo(0));
        frame.Dispose();
    }

    [Test]
    public void TestFrameSourceReturnsInitialFrameWhenTargetAlreadyAhead()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = createFrameSource(bytes);

        source.SetTargetTime(5.0);
        source.Start();

        Assert.That(waitForFrame(source, TimeSpan.FromSeconds(3), out var frame), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestFrameSourceSkipsStaleFrames()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = createFrameSource(bytes);

        source.Start();
        source.SetTargetTime(2.0);

        Assert.That(waitForQueuedFrames(source, 1, TimeSpan.FromSeconds(3)), Is.True);
        Thread.Sleep(100);
        Assert.That(source.TryTakeLatestFrame(out var frame), Is.True);
        Assert.That(frame!.Time, Is.InRange(1.8, 2.0));
        frame.Dispose();
    }

    [Test]
    public void TestRulesetDoesNotReferencePlmpegSharp()
    {
        // PLMpegSharp must be absent from the ruleset's dependency graph entirely. It used to be
        // internalized into the ruleset DLL by ILRepack; once removed there is nothing to merge,
        // so a stray ProjectReference would show up here as a real, unmerged reference on every
        // OS (not just the ones that skipped ILRepack).
        var referencedAssemblyNames = typeof(BmsRuleset).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        Assert.That(referencedAssemblyNames, Does.Not.Contain("PLMpegSharp"));
    }

    private sealed class CountingCopyStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int CopyCount { get; private set; }

        public override void CopyTo(Stream destination, int bufferSize)
        {
            CopyCount++;
            base.CopyTo(destination, bufferSize);
        }
    }
}
