using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

// This host-only harness is copied unchanged to the baseline worktree; it never calls a particular judgement implementation.
[HeadlessTest, NonParallelizable, Explicit("Opt-in advancing Player measurements; benchmark approval is required."), Category("Performance")]
public partial class TestSceneBmsAdvancingJudgementPerformance : BmsPlayerTestScene
{
    private int count;
    private string profile = "tap";
    private double endTime;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => [.. new BmsAutoGenerator(beatmap).Generate().Frames]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 240,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
        };
        for (var i = 0; i < count; i++)
        {
            var time = 3000 + i * (profile == "dense" ? .5 : 4);
            var column = profile == "dense" ? 1 : i % 7 + 1;
            beatmap.HitObjects.Add(profile is "overlap" or "rewind" or "mine-hcn"
                ? new BmsLongNote { StartTime = time, Duration = 1000, Column = column }
                : new BmsNote { StartTime = time, Column = column });
        }
        endTime = beatmap.HitObjects.Max(n => n.GetEndTime()) + 500;
        if (profile == "mine-hcn")
        {
            beatmap.HitObjects.Add(new BmsLongNote { StartTime = 3000, Duration = endTime - 3500, Column = 0 });
            for (var time = 3201d; time < endTime - 500; time += 201)
                beatmap.HitObjects.Add(new BmsLandmine { StartTime = time, Column = 0, LandmineDamagePercent = .1 });
        }
        beatmap.HitObjects.Add(new BmsNote { StartTime = endTime + 5000, Column = 1 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(n => n.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void AdvancingPlayer([Values(64, 2048)] int objects, [Values("tap", "dense", "overlap", "mine-hcn", "rewind")] string workload)
    {
        var samples = new List<Sample>();
        for (var run = -2; run < 7; run++)
        {
            var sampleIndex = run;
            AddStep("load fresh sample " + run, () => { count = objects; profile = workload; LoadPlayer(); });
            AddUntilStep("sample loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
            AddStep("seek sample start", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2998); });
            AddUntilStep("sample at start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2998) < .001);
            AddStep("measure advancing sample " + run, () =>
            {
                var times = timeline();
                var frameTimes = new double[times.Length];
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var retainedBefore = GC.GetTotalMemory(false);
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var peakAlive = 0;
                for (var i = 0; i < times.Length; i++)
                {
                    var frameStart = Stopwatch.GetTimestamp();
                    Player.GameplayClockContainer.Seek(times[i]);
                    var attempts = 0;
                    do
                    {
                        Player.DrawableRuleset.UpdateSubTree();
                        if (++attempts > 1000) throw new InvalidOperationException("Replay catchup failed to reach the fixed input time.");
                    } while (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - times[i]) > .001);
                    frameTimes[i] = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                    var alive = 0;
                    foreach (var column in Playfield.Stage.Columns) alive += column.HitObjectContainer.AliveEntries.Count;
                    peakAlive = Math.Max(peakAlive, alive);
                }
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                var retained = GC.GetTotalMemory(true) - retainedBefore;
                var score = (BmsScoreProcessor)Player.ScoreProcessor;
                var health = (BmsHealthProcessor)Player.HealthProcessor;
                Assert.That(score.JudgementEvents.Count, Is.GreaterThanOrEqualTo(objects), "Every sample must execute new results instead of repeatedly timing completed notes.");
                Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(endTime).Within(.001));
                var verification = JsonSerializer.Serialize(new { score.JudgementEvents, health.GaugeHistory, Score = score.TotalScore.Value, Combo = score.Combo.Value });
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verification)));
                if (sampleIndex >= 0)
                    samples.Add(new Sample(elapsed, allocated, retained, peakAlive, frameTimes, score.JudgementEvents.Count, health.GaugeHistory.Count, hash));
            });
        }
        AddStep("write measured samples", () =>
        {
            Assert.That(samples, Has.Count.EqualTo(7));
            Assert.That(samples.Select(s => s.VerificationHash).Distinct().Count(), Is.EqualTo(1), "Fresh samples must execute the same ordered results and gauge history.");
            var frames = samples.SelectMany(s => s.FrameMilliseconds).Order().ToArray();
            var report = new
            {
                Test = $"Advancing-{workload}-{objects}", Objects = objects, Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                MedianMilliseconds = samples.Select(s => s.Milliseconds).Order().ElementAt(3),
                MedianAllocatedBytes = samples.Select(s => s.AllocatedBytes).Order().ElementAt(3),
                MedianRetainedBytes = samples.Select(s => s.RetainedBytes).Order().ElementAt(3),
                P95FrameMilliseconds = frames[(int)(frames.Length * .95)], P99FrameMilliseconds = frames[(int)(frames.Length * .99)],
                Samples = samples,
            };
            var json = JsonSerializer.Serialize(report);
            TestContext.Progress.WriteLine(json);
            var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, report.Test + ".json"), json);
            }
        });
    }

    private double[] timeline()
    {
        var times = new List<double>();
        append(3000);
        if (profile == "rewind")
        {
            var rewindTime = 3000 + (endTime - 3500) / 2;
            times.Add(rewindTime);
            append(rewindTime + 16);
        }
        return times.ToArray();

        void append(double from)
        {
            for (var time = from; time < endTime; time += 16) times.Add(time);
            times.Add(endTime);
        }
    }

    private sealed record Sample(double Milliseconds, long AllocatedBytes, long RetainedBytes, int PeakAlive, double[] FrameMilliseconds,
                                 int Results, int GaugeHistoryEntries, string VerificationHash);
}
