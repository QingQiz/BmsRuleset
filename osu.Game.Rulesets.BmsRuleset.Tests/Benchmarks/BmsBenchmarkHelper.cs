#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Benchmarks;

/// <summary>Shared utilities for BMS benchmarks.</summary>
internal static class BmsBenchmarkHelper
{
    public static readonly string SONGS_ROOT = Path.GetFullPath(Path.Combine(
        TestContext.CurrentContext.TestDirectory, "..", "..", "..", "bms_test_songs"));

    public const string EXTRA_SONGS_ROOT = @"D:\BMS Song Pack\Normal";

    private static string cacheFilePath => ".bms_file_cache.json";

    private static string[]? cachedFiles;

    /// <summary>Discover up to <paramref name="maxCount"/> BMS chart files. Cached to disk between runs.</summary>
    public static string[] DiscoverBmsFiles(int maxCount = 10000)
    {
        if (cachedFiles != null)
            return cachedFiles;

        // Try loading from disk cache first
        var cachePath = cacheFilePath;
        if (File.Exists(cachePath))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<string[]>(File.ReadAllBytes(cachePath));
                if (cached != null && cached.Length > 0)
                {
                    // Verify at least one root still exists (quick staleness check)
                    if (Directory.Exists(SONGS_ROOT))
                    {
                        cachedFiles = cached;
                        return cachedFiles;
                    }
                }
            }
            catch
            {
                // Corrupted cache, fall through to re-scan
            }
        }

        var dirs = new List<string> { SONGS_ROOT };
        if (Directory.Exists(EXTRA_SONGS_ROOT))
            dirs.Add(EXTRA_SONGS_ROOT);

        cachedFiles = dirs
            .SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories))
            .Where(Constant.IsChartFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .ToArray();

        // Write to disk cache
        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllBytes(cachePath, JsonSerializer.SerializeToUtf8Bytes(cachedFiles));
        }
        catch
        {
            // Non-critical; next run will re-scan
        }

        return cachedFiles;
    }

    /// <summary>
    /// Run <paramref name="func"/> N times. Reports average time/memory/GC and min/max time.
    /// Return value is from the first iteration.
    /// </summary>
    public static (T sr, double avgMs, double avgBytes, double avgGc0, double avgGc1, double avgGc2,
        double minMs, double maxMs) MeasureAvg<T>(Func<T> func, int iterations)
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

    /// <summary>Compute the p-th percentile of a value array.</summary>
    public static double Percentile(double[] values, int p)
    {
        Array.Sort(values);
        var i = Math.Clamp((int)Math.Ceiling(p / 100.0 * values.Length) - 1, 0, values.Length - 1);
        return values[i];
    }
}
