#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[HeadlessTest, NonParallelizable, Explicit("Opt-in hit error meter measurements; benchmark approval is required."), Category("Performance")]
public partial class TestSceneBmsHitErrorMeterPerformance : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void SteadyTimingLines()
    {
        var onJudgement = typeof(BmsHitErrorMeter).GetMethod("OnNewJudgement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<BmsHitErrorMeter, JudgementResult>>();
        // Use the framework's normal draw-state snapshots to include cache invalidation costs
        // without timing the graphics driver or unrelated HUD components.
        var generateDrawNodes = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode>>();
        var clock = new ManualClock();
        BmsHitErrorMeter meter = null!;

        AddStep("load player", LoadPlayer);
        AddUntilStep("meter loaded", () => Player.IsLoaded
            && (meter = Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().SingleOrDefault()!)?.IsLoaded == true);
        AddStep("freeze gameplay", () => Player.GameplayClockContainer.Stop());
        AddStep("measure steady timing lines", () =>
        {
            meter.Clock = new FramedClock(clock);
            meter.Clear();
            meter.UpdateSubTree();
            var note = Playfield.Beatmap.HitObjects[0];
            var results = Enumerable.Range(0, 20).Select(i =>
            {
                var result = new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect };
                typeof(JudgementResult).GetProperty(nameof(JudgementResult.TimeOffset))!.SetValue(result, (double)i - 10);
                return result;
            }).ToArray();
            var samples = new List<Sample>();
            var drawFrame = 0UL;
            var drawAccumulator = 0;
            DrawNode? lastDrawNode = null;
            using var process = Process.GetCurrentProcess();

            advance(12000);
            for (var sample = 0; sample < 5; sample++)
            {
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var cpuBefore = process.TotalProcessorTime;
                var started = Stopwatch.GetTimestamp();
                advance(20000);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                Assert.That(lastDrawNode, Is.Not.Null);
                var lines = meter.Judgements.Lines.ToArray();
                Assert.That(lines.Length, Is.InRange(500, 501));
                Assert.That(lines.All(line => line.AlphaAt(meter.Judgements.DisplayTime) is >= 0 and <= 0.4f), Is.True);
                var state = JsonSerializer.Serialize(lines.Select(line => new { line.X, Alpha = line.AlphaAt(meter.Judgements.DisplayTime), Width = meter.JudgementLineThickness.Value, Height = 1 }));
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
                samples.Add(new Sample(elapsed, cpu, allocated, lines.Length, hash));
            }

            Assert.That(samples.Select(sample => sample.StateHash).Distinct().Count(), Is.EqualTo(1));
            var report = JsonSerializer.Serialize(new
            {
                UpdatesPerSample = 20000, DrawSnapshotsPerSample = 4800, JudgementsPerSecond = 50,
                MedianMilliseconds = samples.Select(sample => sample.Milliseconds).Order().ElementAt(2),
                MedianCpuMilliseconds = samples.Select(sample => sample.CpuMilliseconds).Order().ElementAt(2),
                MedianAllocatedBytes = samples.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
                Samples = samples,
            });
            TestContext.Progress.WriteLine(report);
            var path = Environment.GetEnvironmentVariable("BMS_METER_PERFORMANCE_REPORT");
            if (!string.IsNullOrEmpty(path))
                File.WriteAllText(path, report);

            void advance(int frames)
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    clock.CurrentTime++;
                    if ((int)clock.CurrentTime % 20 == 0)
                        onJudgement(meter, results[((int)clock.CurrentTime / 20) % results.Length]);
                    meter.UpdateSubTree();
                    drawAccumulator += 240;
                    if (drawAccumulator >= 1000)
                    {
                        drawAccumulator -= 1000;
                        meter.UpdateSubTreeMasking();
                        lastDrawNode = generateDrawNodes(meter, ++drawFrame, (int)(drawFrame % 3), false);
                    }
                }
            }
        });
    }

    private sealed record Sample(double Milliseconds, double CpuMilliseconds, long AllocatedBytes, int Lines, string StateHash);
}
