using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in localisation CPU measurements; benchmark approval is required."), Category("Performance")]
public class BmsLocalisationPerformanceTest
{
    [Test]
    public void RepeatedScoreGraphText()
    {
        // Retain a bounded set of independently formatted values so the measurement cannot
        // benefit from caching scores or from discarding unused string descriptors.
        var texts = new LocalisableString[500];
        var samples = new List<Sample>();
        var started = Stopwatch.GetTimestamp();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        texts[0] = BmsStrings.ScoreGraphExScore(0);
        var firstAccessMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var firstAccessAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        refresh(10000);
        using var process = Process.GetCurrentProcess();

        for (var sample = 0; sample < 5; sample++)
        {
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var cpuBefore = process.TotalProcessorTime;
            started = Stopwatch.GetTimestamp();
            refresh(200000);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var state = string.Join("\n", texts.Select(text => text.ToString()));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
            Assert.That(texts.Distinct().Count(), Is.GreaterThan(100));
            samples.Add(new Sample(elapsed, cpu, allocated, hash));
        }

        Assert.That(samples.Select(sample => sample.StateHash).Distinct().Count(), Is.EqualTo(1));
        var report = JsonSerializer.Serialize(new
        {
            TextsPerSample = 1000000, FirstAccessMilliseconds = firstAccessMilliseconds,
            FirstAccessAllocatedBytes = firstAccessAllocatedBytes,
            MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
            MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
            MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
            Samples = samples,
        });
        TestContext.Progress.WriteLine(report);
        var path = Environment.GetEnvironmentVariable("BMS_LOCALISATION_PERFORMANCE_REPORT");
        if (!string.IsNullOrEmpty(path))
            File.WriteAllText(path, report);

        void refresh(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var slot = i % 100 * 5;
                texts[slot] = BmsStrings.ScoreGraphExScore(i);
                texts[slot + 1] = BmsStrings.ScoreGraphExScore(i / 2);
                texts[slot + 2] = BmsStrings.ScoreGraphDifference(i - i / 2);
                texts[slot + 3] = BmsStrings.ScoreGraphExScore(i * 2 / 3);
                texts[slot + 4] = BmsStrings.ScoreGraphDifference(i - i * 2 / 3);
            }
        }
    }

    private sealed record Sample(double Milliseconds, double CpuMilliseconds, long AllocatedBytes, string StateHash);
}
