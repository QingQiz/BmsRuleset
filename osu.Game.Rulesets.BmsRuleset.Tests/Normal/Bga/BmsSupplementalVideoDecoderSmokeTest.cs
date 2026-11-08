#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.FFmpeg;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsSupplementalVideoDecoderSmokeTest
{

    private static void requireNativeArtifacts()
    {
        if (!BmsSupplementalFFmpegFuncs.TryCreate(out var _, out var error))
            Assert.Ignore(error ?? "Supplemental FFmpeg native backend is unavailable.");
    }

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

    [Test]
    public void TestBundledMpegBgaDecodesFirstFrame()
    {
        requireNativeArtifacts();

        var bytes = File.ReadAllBytes(locateTestSongFile("103_outlaw_ogg", "bga.mpg"));
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using var activeDecoder = decoder!;
        for (var i = 0; i < 30; i++)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            Assert.That(frame, Is.Not.Null);

            using var upload = frame!.CreateUpload();
            if (upload.Data.ToArray().Any(p => p.R != 0 || p.G != 0 || p.B != 0))
                return;
        }

        Assert.Fail("The supplemental FFmpeg decoder did not produce a non-black frame in the first 30 frames.");
    }

    [Test]
    public void TestMalformedDataIsRejected()
    {
        Assert.That(BmsSupplementalVideoDecoder.TryCreate([1, 2, 3, 4], out var decoder, out var error), Is.False);
        Assert.That(decoder, Is.Null);
        Assert.That(error, Is.Not.Empty);
    }

    [Test]
    public void TestLocalVideoCompatibilityCorpus()
    {
        var manifest = Environment.GetEnvironmentVariable("BMS_BGA_CORPUS");
        if (string.IsNullOrEmpty(manifest))
            Assert.Ignore("Set BMS_BGA_CORPUS to a text file listing local video paths.");

        requireNativeArtifacts();
        var failures = new List<string>();
        var count = 0;
        foreach (var path in File.ReadLines(manifest!))
        {
            count++;
            if (!BmsSupplementalVideoDecoder.TryCreate(File.ReadAllBytes(path), out var decoder, out var error))
            {
                failures.Add($"{path}: {error}");
                continue;
            }

            using (decoder)
            {
                if (!decoder.TryDecodeNextFrame(out var frame, out error))
                    failures.Add($"{path}: {error ?? "No decoded frame."}");
                frame?.Dispose();
            }
        }

        TestContext.Progress.WriteLine($"Decoded first frames from {count - failures.Count}/{count} local videos.");
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(failures, Is.Empty);
    }

    [Test]
    public void TestBundledMpegDecodesThroughEndOfStream()
    {
        requireNativeArtifacts();

        var bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "bga_fixtures", "mpeg1.mpg"));
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using var activeDecoder = decoder!;
        var frameCount = 0;
        var previousTime = double.NegativeInfinity;
        Assert.That(activeDecoder.IsFullyDrained, Is.False);

        while (activeDecoder.TryDecodeNextFrame(out var frame, out error))
        {
            Assert.That(frame, Is.Not.Null);
            using (frame)
            {
                Assert.That(frame!.Width, Is.EqualTo(64));
                Assert.That(frame.Height, Is.EqualTo(64));
                Assert.That(frame.Time, Is.GreaterThanOrEqualTo(previousTime));
                previousTime = frame.Time;
            }

            frameCount++;
        }

        Assert.That(error, Is.Null);
        Assert.That(frameCount, Is.EqualTo(13));
        Assert.That(activeDecoder.IsFullyDrained, Is.True);
        Assert.That(activeDecoder.TryDecodeNextFrame(out var exhaustedFrame, out error), Is.False);
        Assert.That(exhaustedFrame, Is.Null);
        Assert.That(error, Is.Null);
    }
}
