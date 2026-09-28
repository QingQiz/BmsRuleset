using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in judgement measurements; select with a FullyQualifiedName filter."), Category("Performance")]
public class BmsJudgementPerformanceTest
{
    [TestCase("steady")]
    [TestCase("fractional")]
    [TestCase("jitter")]
    [TestCase("backlog")]
    public void RecordHcnHistory(string profile)
    {
        const int count = 100000;
        var random = new Random(4139);
        var elapsed = Enumerable.Range(0, count).Select(i => profile switch
        {
            "fractional" => 1000.0 / 240,
            "jitter" => .5 + random.NextDouble(),
            "backlog" => i % 100 == 0 ? 650 : 1,
            _ => 1,
        }).ToArray();
        Measure("HCN-" + profile, count, () =>
        {
            var tracker = new BmsHellChargeBodyTracker();
            var ticks = 0;
            var signedTicks = 0;
            Action<bool, double> tick = (holding, _) => { ticks++; signedTicks += holding ? 1 : -1; };
            return new Measurement(() =>
            {
                var time = 0d;
                for (var i = 0; i < count; i++)
                {
                    time += elapsed[i];
                    tracker.Update(elapsed[i], profile != "backlog" || i % 1000 < 700, tick, time);
                }
            }, () =>
            {
                Assert.That(ticks, Is.GreaterThan(0));
                return new { Ticks = ticks, SignedTicks = signedTicks };
            });
        });
    }

    [Test]
    public void RewindHcnHistory()
    {
        const int count = 100000;
        Measure("HCN-rewind", 100, () =>
        {
            var tracker = new BmsHellChargeBodyTracker();
            for (var i = 1; i <= count; i++)
                tracker.Update(1, true, static (_, _) => { }, i);
            return new Measurement(() =>
            {
                for (var i = 99; i >= 0; i--)
                    tracker.Rewind(i * 1000 + .5);
            }, () =>
            {
                var ticks = 0;
                tracker.Update(200, true, (_, _) => ticks++, 200);
                Assert.That(ticks, Is.Zero);
                tracker.Update(1, true, (_, _) => ticks++, 201);
                Assert.That(ticks, Is.EqualTo(1));
                return new { Ticks = ticks };
            });
        });
    }

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
