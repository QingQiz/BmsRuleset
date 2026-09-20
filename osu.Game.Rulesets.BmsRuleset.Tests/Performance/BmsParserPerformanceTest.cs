using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in parser measurements; run with a FullyQualifiedName filter."), Category("Performance")]
public class BmsParserPerformanceTest
{
    [TestCase("dense", false)]
    [TestCase("timing", false)]
    [TestCase("longnotes", false)]
    [TestCase("dense", true)]
    [TestCase("timing", true)]
    public void ParseGeneratedChart(string scenario, bool summary)
    {
        var lines = BmsParserPerformanceRegressionTest.CreateChart(scenario);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        if (summary)
            measure(() => BmsChartParser.ParseImportSummary(bytes, "fixture.bme", _ => 1), r => JsonSerializer.Serialize(r));
        else
            measure(() => BmsChartParser.Parse(BmsChartParser.ReadAllLines(bytes), "fixture.bme", _ => 1), BmsParserPerformanceRegressionTest.Fingerprint);
    }

    [Test]
    public void ParseRealChart()
    {
        var path = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Destr0yer (by 削除 feat. Nikki Simmons)", "destr0yer_starnother.bms");
        var bytes = File.ReadAllBytes(path);
        measure(() => BmsChartParser.Parse(BmsChartParser.ReadAllLines(bytes), path, _ => 1), BmsParserPerformanceRegressionTest.Fingerprint);
    }

    [Test]
    public void ShiftTimingMap()
    {
        var map = BmsParserPerformanceRegressionTest.CreateTimingMap(4096);
        measure(() => map.ShiftedBy(1000), r => JsonSerializer.Serialize(r));
    }

    private static void measure<T>(Func<T> run, Func<T, string> fingerprint)
    {
        var elapsed = new double[7];
        var allocations = new long[7];
        string expected = null;
        for (var i = -2; i < elapsed.Length; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            var result = run();
            var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var value = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint(result))));
            expected ??= value;
            Assert.That(value, Is.EqualTo(expected));
            if (i >= 0)
            {
                elapsed[i] = milliseconds;
                allocations[i] = allocated;
            }
        }
        var report = new
        {
            Test = TestContext.CurrentContext.Test.Name,
            Runtime = RuntimeInformation.FrameworkDescription,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Milliseconds = elapsed,
            AllocatedBytes = allocations,
            MedianMilliseconds = elapsed.Order().ElementAt(3),
            MedianAllocatedBytes = allocations.Order().ElementAt(3),
            Fingerprint = expected,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Progress.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, report.Test.Replace('"', '_') + ".json"), json);
        }
    }
}
