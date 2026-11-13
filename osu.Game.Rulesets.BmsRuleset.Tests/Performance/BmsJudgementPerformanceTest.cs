using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

internal static class BmsJudgementPerformanceTest
{
    internal static void Measure(string name, int operations, Func<Measurement> setup)
    {
        var elapsed = new double[7];
        var allocations = new long[7];
        object verification = null;
        for (var run = -2; run < elapsed.Length; run++)
        {
            var measurement = setup();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            measurement.Run();
            var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            verification = measurement.Verify();
            if (run < 0)
                continue;
            elapsed[run] = milliseconds;
            allocations[run] = allocated;
        }

        var report = new
        {
            Test = name, Operations = operations,
            Runtime = RuntimeInformation.FrameworkDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Milliseconds = elapsed, AllocatedBytes = allocations,
            MedianMilliseconds = elapsed.Order().ElementAt(elapsed.Length / 2),
            MedianAllocatedBytes = allocations.Order().ElementAt(allocations.Length / 2),
            Verification = verification,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Progress.WriteLine(json);
        var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".json"), json);
        }
    }

    internal sealed record Measurement(Action Run, Func<object> Verify);
}
