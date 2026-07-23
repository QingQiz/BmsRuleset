using System;
using System.IO;
using System.Threading;
#nullable enable
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsBgaVideoPreloaderTest
{
    [Test]
    public void TestPreloadStartsFrameSourceSoDrawableDecodesBeforeLoad()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        using var preloader = new BmsBgaVideoPreloader();

        preloader.Preload("mpeg1.mpg", bytes);
        Assert.That(preloader.TryCreateDrawable("mpeg1.mpg", new ManualClock(), 0, out var drawable), Is.True);

        // The drawable's load() is never invoked. Any decoding can only come from the frame source
        // Preload already started — i.e. the video was warmed during the loading screen rather than
        // lazily on the first gameplay frame, which is exactly what removes the entry hitch.
        var d = (BmsSupplementalVideoDrawable)drawable!;
        Assert.That(waitUntil(() => d.Stats.DecodedFrames > 0, TimeSpan.FromSeconds(3)), Is.True);
        d.Dispose();
    }

    [Test]
    public void TestPreloadIsIdempotentAndConsumedOnce()
    {
        requireSupplementalNativeArtifacts();

        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        using var preloader = new BmsBgaVideoPreloader();

        preloader.Preload("mpeg1.mpg", bytes);
        preloader.Preload("mpeg1.mpg", bytes); // must not spawn a second warm source

        Assert.That(preloader.TryCreateDrawable("mpeg1.mpg", new ManualClock(), 0, out var first), Is.True);
        first!.Dispose();
        // One-shot: the warm source was handed off; a later request for the same path misses and
        // falls back to a fresh factory create (acceptable mid-game, never at gameplay entry).
        Assert.That(preloader.TryCreateDrawable("mpeg1.mpg", new ManualClock(), 0, out _), Is.False);
    }

    [Test]
    public void TestPreloadDoesNotWarmFilesTheProbeRejects()
    {
        requireSupplementalNativeArtifacts();

        using var preloader = new BmsBgaVideoPreloader();
        // Garbage bytes — the supplemental probe must reject so the file is left for the framework path.
        preloader.Preload("bogus.mp4", [1, 2, 3, 4]);
        Assert.That(preloader.TryCreateDrawable("bogus.mp4", new ManualClock(), 0, out _), Is.False);
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
