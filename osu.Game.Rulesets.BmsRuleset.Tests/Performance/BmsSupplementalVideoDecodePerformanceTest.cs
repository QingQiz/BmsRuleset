#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Video.Supplemental;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture]
public class BmsSupplementalVideoDecodePerformanceTest
{
    [Test]
    public void TestDecodeCpu()
    {
        var reportPath = Environment.GetEnvironmentVariable("BMS_VIDEO_DECODE_REPORT");
        if (string.IsNullOrWhiteSpace(reportPath))
            Assert.Ignore("Set BMS_VIDEO_DECODE_REPORT to run the video decode measurement.");

        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        string? videoPath = null;

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "bms_test_songs", "103_outlaw_ogg", "bga.mpg");
            if (File.Exists(candidate))
            {
                videoPath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        Assert.That(videoPath, Is.Not.Null);
        var bytes = File.ReadAllBytes(videoPath!);
        Assert.That(BmsSupplementalVideoDecoder.TryCreate(bytes, out var decoder, out var error), Is.True, error);

        using var activeDecoder = decoder!;
        for (var i = 0; i < 100; i++)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            frame!.Dispose();
        }

        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var timer = Stopwatch.StartNew();

        for (var i = 0; i < 3000; i++)
        {
            Assert.That(activeDecoder.TryDecodeNextFrame(out var frame, out error), Is.True, error);
            frame!.Dispose();
        }

        timer.Stop();
        var cpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        File.WriteAllText(reportPath!, JsonSerializer.Serialize(new
        {
            CpuMilliseconds = cpuMilliseconds,
            WallMilliseconds = timer.Elapsed.TotalMilliseconds,
            Frames = 3000,
        }));
    }
}
