using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public class BmsStarRatingBenchmark
{
    /// <summary>Number of iterations per measurement to reduce noise.</summary>
    private const int iterations = 10;

    private static readonly string songs_root = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "bms_test_songs"));

    private const string extra_songs_root = @"D:\BMS\CYTUS Collection";

    private static readonly string data_dir = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "benchmark_data"));

    private static readonly JsonSerializerOptions json_options = new()
    {
        WriteIndented = false,
    };

    #region Data source

    private static readonly object data_gen_lock = new();

    /// <summary>
    /// Returns benchmark data file paths, generating them from BMS test songs if needed.
    /// </summary>
    public static IEnumerable<string> BenchmarkDataSource()
    {
        if (!Directory.Exists(data_dir) || Directory.GetFiles(data_dir, "*.json").Length == 0)
        {
            lock (data_gen_lock)
            {
                // Double-check after acquiring lock
                if (!Directory.Exists(data_dir) || Directory.GetFiles(data_dir, "*.json").Length == 0)
                {
                    Console.WriteLine("Generating benchmark data...");
                    generateBenchmarkData();
                }
            }
        }

        var files = Directory.GetFiles(data_dir, "*.json");
        return files.Length > 0 ? files : ["__NO_DATA__"];
    }

    #endregion

    #region Old vs new comparison (with iterations)

    [Test]
    [TestCaseSource(nameof(BenchmarkDataSource))]
    public void CompareOldVsNew(string dataPath)
    {
        if (dataPath == "__NO_DATA__")
        {
            Assert.Warn("No benchmark data found.");
            return;
        }

        var input = loadInput(dataPath);
        var hitObjects = toHitObjects(input);

        if (!v2Available())
        {
            Assert.Warn("BmsStarRatingProcessorV2 not found — skipping.");
            return;
        }

        // Warmup (not measured)
        new BmsStarRatingProcessor().Compute(hitObjects, input.TotalColumns, input.Rank);
        new BmsStarRatingProcessorV2().Compute(hitObjects, input.TotalColumns, input.Rank);

        // Measure old × N iterations
        var (srOld, tOld, bytesOld, gc0Old, gc1Old, gc2Old, tMinOld, tMaxOld) =
            measureAvg(() => new BmsStarRatingProcessor().Compute(hitObjects, input.TotalColumns, input.Rank).StarRating);

        // Measure new × N iterations
        var (srNew, tNew, bytesNew, gc0New, gc1New, gc2New, tMinNew, tMaxNew) =
            measureAvg(() => new BmsStarRatingProcessorV2().Compute(hitObjects, input.TotalColumns, input.Rank).StarRating);

        // Result consistency (check first iteration SR — already checked inside measureAvg)
        var diff = Math.Abs(srNew - srOld);
        Assert.That(diff, Is.LessThan(1e-9),
            $"SR mismatch for {Path.GetFileName(dataPath)}: old={srOld:F10} new={srNew:F10}");

        // Report
        var chartName = Path.GetFileNameWithoutExtension(dataPath);
        Console.WriteLine($"Chart: {chartName}");
        Console.WriteLine($"  Notes:    {input.HitObjects.Count,8}");
        Console.WriteLine($"  Columns:  {input.TotalColumns,8}");
        Console.WriteLine($"  Rank:     {input.Rank,8}");
        Console.WriteLine($"  Iters:    {iterations,8}");
        Console.WriteLine($"  {"",30} {"Old",16} {"New",16} {"Improvement",12}");
        Console.WriteLine($"  {"Time avg (ms)",-30} {tOld,16:F2} {tNew,16:F2} {tOld / tNew,10:F2}x");
        Console.WriteLine($"  {"Time min (ms)",-30} {tMinOld,16:F2} {tMinNew,16:F2} {tMinOld / tMinNew,10:F2}x");
        Console.WriteLine($"  {"Time max (ms)",-30} {tMaxOld,16:F2} {tMaxNew,16:F2}");
        Console.WriteLine($"  {"Memory avg (KB)",-30} {bytesOld / 1024,16:F0} {bytesNew / 1024,16:F0} {100.0 * bytesNew / bytesOld,10:F1}%");
        Console.WriteLine($"  {"GC Gen0 avg",-30} {gc0Old,16:F1} {gc0New,16:F1}");
        Console.WriteLine($"  {"GC Gen1 avg",-30} {gc1Old,16:F1} {gc1New,16:F1}");
        Console.WriteLine($"  {"SR value",-30} {srOld,16:F10} {srNew,16:F10} {(diff < 1e-12 ? "✓ identical" : diff < 1e-9 ? "≈ within 1e-9" : "✗")}");
    }

    #endregion

    #region Helpers

    private static void generateBenchmarkData()
    {
        Directory.CreateDirectory(data_dir);

        var dirs = new List<string> { songs_root };
        if (Directory.Exists(extra_songs_root))
            dirs.Add(extra_songs_root);
        else
            Console.WriteLine($"  (extra source not found: {extra_songs_root})");

        var bmsFiles = dirs
            .SelectMany(d => Directory.GetFiles(d, "*.bms", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"  Found {bmsFiles.Length} BMS files");

        foreach (var file in bmsFiles)
        {
            try
            {
                var content = File.ReadAllBytes(file);
                var lines = BmsChartParser.PreprocessLines(BmsChartParser.ReadAllLines(content));
                var parsed = BmsChartParser.Parse(lines, file, _ => 1);

                var input = new SrBenchmarkInput(
                    parsed.TotalColumns,
                    parsed.Rank,
                    parsed.HitObjects.Select(h => new HitObjectData(
                        h.Column, h.StartTime, h.Duration, h.IsLongNote
                    )).ToList()
                );

                var name = Path.GetFileNameWithoutExtension(file) + ".json";
                // Avoid name collision across directories
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

    private static bool v2Available()
    {
        try
        {
            _ = Activator.CreateInstance(typeof(BmsStarRatingProcessorV2));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run func N times. Report average time/memory/GC and min/max time.
    /// SR value is from the first iteration.
    /// </summary>
    private static (T sr, double avgMs, double avgBytes, double avgGc0, double avgGc1, double avgGc2,
        double minMs, double maxMs) measureAvg<T>(Func<T> func)
    {
        T? firstResult = default;
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
            firstResult!,
            (double)totalTicks / iterations / TimeSpan.TicksPerMillisecond,
            (double)totalBytes / iterations,
            (double)totalGc0 / iterations,
            (double)totalGc1 / iterations,
            (double)totalGc2 / iterations,
            (double)minTicks / TimeSpan.TicksPerMillisecond,
            (double)maxTicks / TimeSpan.TicksPerMillisecond);
    }

    #endregion

}

/// <summary>
/// Minimal input data for BmsStarRatingProcessor.Compute(), serialized as JSON.
/// Only the fields actually used by the SR algorithm are included.
/// </summary>
public sealed record SrBenchmarkInput(
    [property: JsonPropertyName("tc")] int TotalColumns,
    [property: JsonPropertyName("rk")] int Rank,
    [property: JsonPropertyName("ho")] List<HitObjectData> HitObjects);

public sealed record HitObjectData(
    [property: JsonPropertyName("c")] int Column,
    [property: JsonPropertyName("st")] double StartTime,
    [property: JsonPropertyName("d")] double Duration,
    [property: JsonPropertyName("ln")] bool IsLongNote);
