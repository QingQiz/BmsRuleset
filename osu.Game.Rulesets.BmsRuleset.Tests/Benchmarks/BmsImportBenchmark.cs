using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Benchmarks;

/// <summary>
/// Benchmarks the chart processing portion of the BMS import pipeline:
/// file reading → BMS parsing → star rating computation.
/// All charts are measured in a single parallel test case with aggregate reporting.
/// </summary>
[TestFixture]
public class BmsImportBenchmark
{
    private const int iterations = 5;

    private static double processChart(byte[] content, string path)
    {
        var lines = BmsChartParser.ReadAllLines(content);
        var parsed = BmsChartParser.Parse(lines, path, _ => 1);

        if (parsed.HitObjects.Count == 0)
            return 0;

        var noteTimings = parsed.HitObjects
            .Where(h => !h.IsMine)
            .Select(h => new BmsNoteTiming(h.Column, h.StartTime, h.IsLongNote ? h.StartTime + h.Duration : h.StartTime))
            .ToList();

        return new BmsStarRatingProcessorV2()
            .Compute(noteTimings, parsed.TotalColumns, parsed.Rank)
            .StarRating;
    }

    [Test]
    public void MeasureAllCharts()
    {
        var files = BmsBenchmarkHelper.DiscoverBmsFiles();
        if (files.Length == 0)
        {
            Assert.Warn("No BMS files found for import benchmark.");
            return;
        }

        Console.WriteLine($"Measuring {files.Length} charts, {iterations} iterations each...");
        Console.WriteLine();

        var results = new ConcurrentBag<(string name, double time, double mem)>();
        var consoleLock = new object();

        Parallel.ForEach(files.Take(1000), bmsPath =>
        {
            var content = File.ReadAllBytes(bmsPath);

            // Warmup (excluded from measurements)
            processChart(content, bmsPath);

            // Measure N iterations
            var (sr, tAvg, bytesAvg, gc0Avg, gc1Avg, gc2Avg, tMin, tMax) =
                BmsBenchmarkHelper.MeasureAvg(() => processChart(content, bmsPath), iterations);

            var chartName = Path.GetFileNameWithoutExtension(bmsPath);
            var dirName = Path.GetFileName(Path.GetDirectoryName(bmsPath));

            lock (consoleLock)
            {
                Console.WriteLine($"Chart: {dirName}/{chartName}");
                Console.WriteLine($"  Time avg (ms)    {tAvg,10:F2}");
                Console.WriteLine($"  Memory avg (KB)  {bytesAvg / 1024,10:F0}");
                Console.WriteLine($"  GC Gen0 avg      {gc0Avg,10:F1}");
                Console.WriteLine($"  SR value         {sr,10:F6}");
            }

            results.Add(($"{dirName}/{chartName}", tAvg, bytesAvg));
        });

        // Aggregate summary
        var finalResults = results.ToList();
        var times = finalResults.Select(r => r.time).ToArray();
        var mems = finalResults.Select(r => r.mem).ToArray();

        Console.WriteLine();
        Console.WriteLine("=== AGGREGATE ===");
        Console.WriteLine($"Charts:           {finalResults.Count,10}");
        Console.WriteLine($"Iterations:       {iterations,10}");
        Console.WriteLine($"Time avg (ms)     {times.Average(),10:F2}");
        Console.WriteLine($"Time p50 (ms)     {BmsBenchmarkHelper.Percentile(times, 50),10:F2}");
        Console.WriteLine($"Time p95 (ms)     {BmsBenchmarkHelper.Percentile(times, 95),10:F2}");
        Console.WriteLine($"Time min (ms)     {times.Min(),10:F2}");
        Console.WriteLine($"Time max (ms)     {times.Max(),10:F2}");
        Console.WriteLine($"Total time (ms)   {times.Sum(),10:F0}");
        Console.WriteLine($"Throughput (ch/s) {finalResults.Count / (times.Sum() / 1000),10:F1}");
        Console.WriteLine($"Mem avg (KB)      {mems.Average() / 1024,10:F0}");
    }
}
