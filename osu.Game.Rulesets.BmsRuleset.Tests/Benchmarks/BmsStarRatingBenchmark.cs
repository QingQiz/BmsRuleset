using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Benchmarks;

/// <summary>
/// Compares V1 vs V2 star rating computation performance across all benchmark charts.
/// All charts are measured in a single parallel test case with aggregate reporting.
/// </summary>
[TestFixture]
public class BmsStarRatingBenchmark
{
    private const int iterations = 10;

    private static readonly string data_dir = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "benchmark_data"));

    private static readonly JsonSerializerOptions json_options = new()
    {
        WriteIndented = false,
    };

    private static readonly object data_gen_lock = new();

    private void ensureBenchmarkData()
    {
        if (Directory.Exists(data_dir) && Directory.GetFiles(data_dir, "*.json").Length > 0)
            return;

        lock (data_gen_lock)
        {
            if (Directory.Exists(data_dir) && Directory.GetFiles(data_dir, "*.json").Length > 0)
                return;

            Console.WriteLine("Generating benchmark data...");
            Directory.CreateDirectory(data_dir);

            var bmsFiles = BmsBenchmarkHelper.DiscoverBmsFiles();
            Console.WriteLine($"  Found {bmsFiles.Length} BMS files");

            foreach (var file in bmsFiles)
            {
                try
                {
                    var content = File.ReadAllBytes(file);
                    var lines = BmsChartParser.ReadAllLines(content);
                    var parsed = BmsChartParser.Parse(lines, file, _ => 1);

                    var input = new SrBenchmarkInput(
                        parsed.TotalColumns,
                        parsed.Rank,
                        parsed.HitObjects.Select(h => new HitObjectData(
                            h.Column, h.StartTime, h.Duration, h.IsLongNote
                        )).ToList()
                    );

                    var name = Path.GetFileNameWithoutExtension(file) + ".json";
                    var dataFile = Path.Combine(data_dir, name);
                    var counter = 1;
                    while (File.Exists(dataFile))
                        dataFile = Path.Combine(data_dir, $"{Path.GetFileNameWithoutExtension(name)}_{counter++}.json");

                    var json = JsonSerializer.Serialize(input, json_options);
                    File.WriteAllText(dataFile, json);

                    Console.WriteLine($"  {Path.GetFileName(dataFile)}: {parsed.HitObjects.Count,6} notes, "
                                      + $"{parsed.TotalColumns,2} cols, Rank={parsed.Rank}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  SKIP {Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }

    private static SrBenchmarkInput loadInput(string dataPath)
    {
        var json = File.ReadAllText(dataPath);
        var input = JsonSerializer.Deserialize<SrBenchmarkInput>(json);
        Assert.That(input, Is.Not.Null);
        return input!;
    }

    private static List<BmsHitObject> toHitObjects(SrBenchmarkInput input)
    {
        return input.HitObjects.Select(h => new BmsHitObject
        {
            Column = h.Column,
            StartTime = h.StartTime,
            Duration = h.Duration,
            IsLongNote = h.IsLongNote,
        }).ToList();
    }

    [Test]
    public void CompareOldVsNewAll()
    {
        ensureBenchmarkData();

        var dataPaths = Directory.GetFiles(data_dir, "*.json");
        if (dataPaths.Length == 0)
        {
            Assert.Warn("No benchmark data found.");
            return;
        }

        Console.WriteLine($"Comparing V1 vs V2 across {dataPaths.Length} charts, {iterations} iterations each...");
        Console.WriteLine();

        var results = new ConcurrentBag<(string name, double oldTime, double newTime, double speedup, double memPct, double memOld, double memNew, string srStatus, double srOld, double srNew)>();
        var failures = new ConcurrentBag<string>();
        var consoleLock = new object();

        Parallel.ForEach(dataPaths, dataPath =>
        {
            var input = loadInput(dataPath);
            var hitObjects = toHitObjects(input);

            // Warmup (not measured)
            new BmsStarRatingProcessor().Compute(hitObjects, input.TotalColumns, input.Rank);
            new BmsStarRatingProcessorV2().Compute(hitObjects, input.TotalColumns, input.Rank);

            // Measure old × N iterations
            var (srOld, tOld, bytesOld, gc0Old, gc1Old, gc2Old, tMinOld, tMaxOld) =
                BmsBenchmarkHelper.MeasureAvg(() => new BmsStarRatingProcessor().Compute(hitObjects, input.TotalColumns, input.Rank).StarRating, iterations);

            // Measure new × N iterations
            var (srNew, tNew, bytesNew, gc0New, gc1New, gc2New, tMinNew, tMaxNew) =
                BmsBenchmarkHelper.MeasureAvg(() => new BmsStarRatingProcessorV2().Compute(hitObjects, input.TotalColumns, input.Rank).StarRating, iterations);

            // SR consistency check
            var diff = Math.Abs(srNew - srOld);
            var srStatus = diff < 1e-12 ? "✓" : diff < 1e-9 ? "≈" : "✗";
            if (diff >= 1e-9)
                failures.Add($"{Path.GetFileName(dataPath)}: old={srOld:F10} new={srNew:F10}");

            var chartName = Path.GetFileNameWithoutExtension(dataPath);
            var speedup = tOld / tNew;
            var memPct = 100.0 * bytesNew / bytesOld;

            lock (consoleLock)
            {
                Console.WriteLine($"Chart: {chartName}");
                Console.WriteLine($"  Notes:    {input.HitObjects.Count,8}");
                Console.WriteLine($"  Columns:  {input.TotalColumns,8}");
                Console.WriteLine($"  Time avg  {tOld,8:F2}ms -> {tNew,8:F2}ms ({speedup,5:F2}x)");
                Console.WriteLine($"  Memory    {bytesOld / 1024,8:F0}KB -> {bytesNew / 1024,8:F0}KB ({memPct,5:F1}%)");
                Console.WriteLine($"  GC Gen0   {gc0Old,8:F1} -> {gc0New,8:F1}");
                Console.WriteLine($"  SR         {srOld,10:F6} -> {srNew,10:F6}  {srStatus}");
            }

            results.Add((chartName, tOld, tNew, speedup, memPct, bytesOld, bytesNew, srStatus, srOld, srNew));
        });

        // Aggregate summary
        var finalResults = results.ToList();
        Console.WriteLine();
        Console.WriteLine("=== AGGREGATE ===");
        Console.WriteLine($"Charts:    {finalResults.Count,10}");
        Console.WriteLine($"Iterations:{iterations,10}");

        var oldTimes = finalResults.Select(r => r.oldTime).ToArray();
        var newTimes = finalResults.Select(r => r.newTime).ToArray();
        var speedups = finalResults.Select(r => r.speedup).ToArray();
        var memPcts = finalResults.Select(r => r.memPct).ToArray();

        Console.WriteLine($"{"",30} {"V1",16} {"V2",16} {"Change",12}");
        Console.WriteLine($"{"Time avg (ms)",-30} {oldTimes.Average(),16:F2} {newTimes.Average(),16:F2} {speedups.Average(),10:F2}x");
        Console.WriteLine($"{"Time p50 (ms)",-30} {BmsBenchmarkHelper.Percentile(oldTimes, 50),16:F2} {BmsBenchmarkHelper.Percentile(newTimes, 50),16:F2} {BmsBenchmarkHelper.Percentile(speedups, 50),10:F2}x");
        Console.WriteLine($"{"Time p95 (ms)",-30} {BmsBenchmarkHelper.Percentile(oldTimes, 95),16:F2} {BmsBenchmarkHelper.Percentile(newTimes, 95),16:F2}");
        Console.WriteLine($"{"Total time (ms)",-30} {oldTimes.Sum(),16:F0} {newTimes.Sum(),16:F0} {oldTimes.Sum() / newTimes.Sum(),10:F2}x");
        Console.WriteLine($"{"Memory (KB) avg",-30} {finalResults.Average(r => r.memOld) / 1024,16:F0} {finalResults.Average(r => r.memNew) / 1024,16:F0} {memPcts.Average(),10:F1}%");
        Console.WriteLine();
        Console.WriteLine($"Speedup distribution:");
        var buckets = new Dictionary<string, int>();
        foreach (var s in speedups)
        {
            var key = s < 1.5 ? "<1.5x" : s < 2 ? "1.5-2x" : s < 3 ? "2-3x" : s < 4 ? "3-4x" : s < 5 ? "4-5x" : s < 7 ? "5-7x" : "7x+";
            buckets[key] = buckets.GetValueOrDefault(key) + 1;
        }
        foreach (var (k, v) in buckets.OrderBy(kv => kv.Key))
            Console.WriteLine($"  {k}: {v} charts ({100.0 * v / finalResults.Count:F1}%)");

        // Fail if any SR mismatches
        var failureList = failures.ToList();
        if (failureList.Count > 0)
        {
            foreach (var f in failureList)
                Console.WriteLine($"  SR MISMATCH: {f}");
            Assert.Fail($"{failureList.Count} SR value mismatches detected.");
        }
    }
}

/// <summary>Minimal input data for BmsStarRatingProcessor.Compute(), serialized as JSON.</summary>
public sealed record SrBenchmarkInput(
    [property: JsonPropertyName("tc")] int TotalColumns,
    [property: JsonPropertyName("rk")] int Rank,
    [property: JsonPropertyName("ho")] List<HitObjectData> HitObjects);

public sealed record HitObjectData(
    [property: JsonPropertyName("c")] int Column,
    [property: JsonPropertyName("st")] double StartTime,
    [property: JsonPropertyName("d")] double Duration,
    [property: JsonPropertyName("ln")] bool IsLongNote);
