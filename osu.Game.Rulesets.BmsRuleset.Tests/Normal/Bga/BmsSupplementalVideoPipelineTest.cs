using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
#nullable enable
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalVideoPipelineTest
{
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

    [Test]
    public void TestFrameSourceDecodesOnWorkerAndReturnsLatestFrame()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0.5);

        BmsSupplementalVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestFrameSourceDisposalWaitsForWorker()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        var source = new BmsSupplementalVideoFrameSource(bytes);

        source.Start();
        Assert.That(waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) > 0, TimeSpan.FromSeconds(3)), Is.True);

        source.Dispose();

        Assert.That(BmsVideoTestAccess.IsWorkerCompleted(source), Is.True);
        Assert.DoesNotThrow(source.Dispose);
    }

    [Test]
    public void TestFrameSourceDecodesAlephOnWorkerAndReturnsLatestFrame()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("Aleph-0 (by LeaF)", "_bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0);

        BmsSupplementalVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestFrameSourceDoesNotReturnFramesAheadOfTargetTime()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(0);

        Assert.That(waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) >= 3, TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(source.TryTakeLatestFrame(out var frame), Is.True);
        Assert.That(frame!.Time, Is.LessThanOrEqualTo(0));
        frame.Dispose();
    }

    [Test]
    public void TestFrameSourceAdvancesWhenTargetTimeIncreasesGradually()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

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
    public void TestFrameSourceSkipsStaleFrames()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

        source.Start();
        source.SetTargetTime(2.0);

        Assert.That(waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) > 0, TimeSpan.FromSeconds(3)), Is.True);
        Thread.Sleep(100);
        Assert.That(source.TryTakeLatestFrame(out var frame), Is.True);
        Assert.That(frame!.Time, Is.InRange(1.8, 2.0));
        frame.Dispose();
    }

    [Test]
    public void TestFrameSourceReturnsInitialFrameWhenTargetAlreadyAhead()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var source = new BmsSupplementalVideoFrameSource(bytes);

        source.SetTargetTime(5.0);
        source.Start();

        BmsSupplementalVideoFrame? frame = null;
        Assert.That(waitUntil(() => source.TryTakeLatestFrame(out frame), TimeSpan.FromSeconds(3)), Is.True);
        Assert.That(frame, Is.Not.Null);
        frame!.Dispose();
    }

    [Test]
    public void TestDrawableReadsSeekableStreamFromBeginning()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        using var stream = new MemoryStream(bytes);
        stream.Position = stream.Length;

        using var drawable = new BmsSupplementalVideoDrawable(stream, new ManualClock(), 0);
        typeof(BmsSupplementalVideoDrawable).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(drawable, [null]);

        var source = BmsVideoTestAccess.GetFrameSource(drawable)!;
        Assert.That(waitUntil(() => BmsVideoTestAccess.GetQueuedFrameCount(source) > 0, TimeSpan.FromSeconds(3)), Is.True);
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

    private static void requireSupplementalNativeArtifacts()
    {
        if (!BmsSupplementalFFmpegFuncs.TryCreate(out _, out var error))
            Assert.Ignore(error ?? "Supplemental FFmpeg native artifacts are unavailable.");
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
}
