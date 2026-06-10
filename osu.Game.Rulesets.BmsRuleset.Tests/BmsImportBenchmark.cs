using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

/// <summary>
/// Benchmarks the chart processing portion of the BMS import pipeline:
/// file reading → BMS parsing → star rating computation.
/// This is the hot path shown in the import performance trace (readPreparedDirectory).
/// </summary>
[TestFixture]
public class BmsImportBenchmark
{
    private const int iterations = 5;

    private static readonly string songs_root = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "bms_test_songs"));

    private const string extra_songs_root = @"D:\BMS\CYTUS Collection";

    private static readonly HashSet<string> reported_warnings = new();

    public static IEnumerable<string> BmsFileDataSource()
    {
        var dirs = new List<string> { songs_root };
        if (Directory.Exists(extra_songs_root))
            dirs.Add(extra_songs_root);
        else if (reported_warnings.Add("extra"))
            Console.WriteLine($"  (extra source not found: {extra_songs_root})");

        var files = dirs
            .SelectMany(d => Directory.GetFiles(d, "*.bms", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return files.Length > 0 ? files : ["__NO_DATA__"];
    }

    [Test]
    [TestCaseSource(nameof(BmsFileDataSource))]
    public void MeasureChartProcessing(string bmsPath)
    {
        if (bmsPath == "__NO_DATA__")
        {
            Assert.Warn("No BMS files found for import benchmark.");
            return;
        }

        var content = File.ReadAllBytes(bmsPath);

        // Warmup (excluded from measurements)
        processChart(content, bmsPath);

        // Measure N iterations
        var (sr, tAvg, bytesAvg, gc0Avg, gc1Avg, gc2Avg, tMin, tMax) =
            measureAvg(() => processChart(content, bmsPath));

        var chartName = Path.GetFileNameWithoutExtension(bmsPath);
        var dirName = Path.GetFileName(Path.GetDirectoryName(bmsPath));

        Console.WriteLine($"Chart: {dirName}/{chartName}");
        Console.WriteLine($"  Time avg (ms)    {tAvg,10:F2}");
        Console.WriteLine($"  Time min (ms)    {tMin,10:F2}");
        Console.WriteLine($"  Time max (ms)    {tMax,10:F2}");
        Console.WriteLine($"  Memory avg (KB)  {bytesAvg / 1024,10:F0}");
        Console.WriteLine($"  GC Gen0 avg      {gc0Avg,10:F1}");
        Console.WriteLine($"  GC Gen1 avg      {gc1Avg,10:F1}");
        Console.WriteLine($"  GC Gen2 avg      {gc2Avg,10:F1}");
        Console.WriteLine($"  SR value         {sr,10:F6}");
    }

    /// <summary>
    /// Processes a single BMS chart through the same pipeline as readPreparedDirectory:
    /// read → preprocess → parse → create hit objects → compute star rating.
    /// Returns the star rating (for verification).
    /// </summary>
    private static double processChart(byte[] content, string path)
    {
        var lines = BmsChartParser.PreprocessLines(BmsChartParser.ReadAllLines(content));
        var parsed = BmsChartParser.Parse(lines, path, _ => 1);

        if (parsed.HitObjects.Count == 0)
            return 0;

        var hitObjects = parsed.HitObjects
            .Select(BmsBeatmapDecoder.CreateHitObject)
            .ToList();

        return new BmsStarRatingProcessorV2()
            .Compute(hitObjects, parsed.TotalColumns, parsed.Rank)
            .StarRating;
    }

    private static (double sr, double avgMs, double avgBytes, double avgGc0, double avgGc1, double avgGc2,
        double minMs, double maxMs) measureAvg(Func<double> func)
    {
        double firstResult = 0;
        var totalBytes = 0L;
        var totalGc0 = 0;
        var totalGc1 = 0;
        var totalGc2 = 0;
        var minTicks = long.MaxValue;
        var maxTicks = 0L;
        var totalTicks = 0L;

        for (var i = 0; i < iterations; i++)
        {
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            var result = func();
            sw.Stop();

            if (i == 0) firstResult = result;

            totalBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
            totalGc0 += GC.CollectionCount(0) - gc0;
            totalGc1 += GC.CollectionCount(1) - gc1;
            totalGc2 += GC.CollectionCount(2) - gc2;

            var ticks = sw.Elapsed.Ticks;
            totalTicks += ticks;
            if (ticks < minTicks) minTicks = ticks;
            if (ticks > maxTicks) maxTicks = ticks;
        }

        return (
            firstResult,
            (double)totalTicks / iterations / TimeSpan.TicksPerMillisecond,
            (double)totalBytes / iterations,
            (double)totalGc0 / iterations,
            (double)totalGc1 / iterations,
            (double)totalGc2 / iterations,
            (double)minTicks / TimeSpan.TicksPerMillisecond,
            (double)maxTicks / TimeSpan.TicksPerMillisecond);
    }
}
