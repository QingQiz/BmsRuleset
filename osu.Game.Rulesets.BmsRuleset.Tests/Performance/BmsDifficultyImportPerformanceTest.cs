using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in performance measurements; run with a FullyQualifiedName filter."), Category("Performance")]
public class BmsDifficultyImportPerformanceTest
{
    [TestCase(2500)]
    [TestCase(20000)]
    public void ComputeStarRating(int count)
    {
        var notes = BmsDifficultyPerformanceRegressionTest.CreateBeatmap(count).HitObjects
            .Select(n => new BmsNoteTiming(n.Column, n.StartTime, n is BmsLongNote ln ? ln.EndTime : n.StartTime)).ToArray();
        measure(() =>
        {
            var result = new BmsStarRatingProcessor().ComputeStarRating(notes, 8, 2);
            return result;
        });
    }

    [Test]
    public void PreprocessTwentyThousandNotes()
    {
        var beatmap = BmsDifficultyPerformanceRegressionTest.CreateBeatmap(20000);
        var calculator = new BmsDifficultyPerformanceRegressionTest.InspectableCalculator(beatmap);
        measure(() => calculator.Preprocess(beatmap).Length);
    }

    [Test]
    public void LoadUnsupportedPerformanceCounter()
    {
        using var probe = new BmsPerformanceCounterRegressionTest.CounterProbe(1000);
        measure(() =>
        {
            var before = probe.Requests;
            probe.Load();
            return probe.Requests - before;
        });
    }

    [TestCase(8, 8)]
    [TestCase(1, 64)]
    public void ImportSixtyFourCharts(int directories, int charts)
    {
        var samples = new Sample[5];
        for (var run = -1; run < samples.Length; run++)
        {
            BmsFileImporterTest.RunIsolatedImportTest(async (realm, storage) =>
            {
                var root = BmsFileImporterTest.CreateImportFixture(storage, directories, charts, 64);
                using var probe = new BmsFileImporterTest.ComputationProbe();
                collect();
                var before = GC.GetTotalAllocatedBytes(true);
                var started = Stopwatch.GetTimestamp();
                await new BmsFileImporter(realm, storage).Import(root).ConfigureAwait(false);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var allocated = GC.GetTotalAllocatedBytes(true) - before;
                var imported = realm.Run(r => r.All<BeatmapInfo>().ToArray().Select(b => b.StarRating).ToArray());
                Assert.That(imported, Has.Length.EqualTo(64));
                Assert.That(imported.All(sr => sr > 0 && double.IsFinite(sr)), Is.True);
                if (run >= 0)
                    samples[run] = new Sample(elapsed, allocated, imported.Sum(), probe.Peak);
            });
        }
        report(samples);
    }

    private static void measure(Func<double> action)
    {
        var samples = new Sample[7];
        for (var run = -2; run < samples.Length; run++)
        {
            collect();
            var before = GC.GetTotalAllocatedBytes(true);
            var started = Stopwatch.GetTimestamp();
            var result = action();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var allocated = GC.GetTotalAllocatedBytes(true) - before;
            if (run >= 0)
                samples[run] = new Sample(elapsed, allocated, result, 0);
        }
        report(samples);
    }

    private static void collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void report(Sample[] samples)
    {
        var report = new
        {
            Test = TestContext.CurrentContext.Test.Name,
            Runtime = RuntimeInformation.FrameworkDescription,
            Processors = Environment.ProcessorCount,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Samples = samples,
            MedianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(samples.Length / 2),
            MedianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(samples.Length / 2),
            PeakConcurrentCalculations = samples.Max(s => s.PeakConcurrentCalculations),
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Progress.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, report.Test + ".json"), json);
        }
    }

    private sealed record Sample(double Milliseconds, long AllocatedBytes, double Result, int PeakConcurrentCalculations);
}
