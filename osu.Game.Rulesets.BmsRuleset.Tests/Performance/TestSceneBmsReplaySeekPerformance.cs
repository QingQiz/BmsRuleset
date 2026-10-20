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
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[HeadlessTest, NonParallelizable, Explicit("Opt-in replay seek measurements; select with a FullyQualifiedName filter."), Category("Performance")]
public partial class TestSceneBmsReplaySeekPerformance : BmsPlayerTestScene
{
    private int count;
    private string profile = "tap";
    private double sourceTime;
    private int frameCount;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new SeekPlayer(BmsTestSkins.CreateSkinSource(BmsTestSkins.SkinKind.Argon, this),
            frames => frameCount = frames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 240,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
        };
        var nextTimes = Enumerable.Range(0, 8).Select(column => 3000d + column * 2).ToArray();
        for (var i = 0; i < count; i++)
        {
            var column = i % 8;
            var time = nextTimes[column];
            var hold = profile == "hcn" && i % 11 == 0;
            beatmap.HitObjects.Add(hold
                ? new BmsLongNote { StartTime = time, Duration = 400, Column = column }
                : new BmsNote { StartTime = time, Column = column });
            if (hold && i % 33 == 0)
                beatmap.HitObjects.Add(new BmsLandmine { StartTime = time + 201, Column = column, LandmineDamagePercent = .1 });
            nextTimes[column] = time + (hold ? 464 : 64);
        }

        sourceTime = beatmap.HitObjects.Max(note => note.GetEndTime()) + 500;
        beatmap.HitObjects.Add(new BmsNote { StartTime = sourceTime + 10000, Column = 1 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(note => note.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(2000, "tap")]
    [TestCase(20000, "tap")]
    [TestCase(2000, "hcn")]
    [TestCase(20000, "hcn")]
    public void MeasureReplaySeeking(int objects, string workload)
    {
        var reports = new List<object>();
        var reference = new Dictionary<double, string>();
        const string mode = "rebuild";
        AddStep("load " + mode, () =>
        {
            count = objects;
            profile = workload;
            LoadPlayer([new BmsModAutoGauge()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("measure " + mode, () =>
        {
            seek(0);
            double[] targets = [3000 + (sourceTime - 3500) * .1, 3000 + (sourceTime - 3500) * .5,
                3000 + (sourceTime - 3500) * .9, sourceTime - 500];
            string[] scenarios = ["early", "middle", "late", "back-500ms"];
            collect();
            var retainedBefore = GC.GetTotalMemory(false);
            var forwardMilliseconds = 0d;
            var forwardAllocated = 0L;
            foreach (var time in targets.Append(sourceTime))
            {
                var forwardAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                var forwardStart = Stopwatch.GetTimestamp();
                seek(time);
                forwardMilliseconds += Stopwatch.GetElapsedTime(forwardStart).TotalMilliseconds;
                forwardAllocated += GC.GetAllocatedBytesForCurrentThread() - forwardAllocatedBefore;
                verify(time);
            }
            collect();
            var forwardRetained = GC.GetTotalMemory(false) - retainedBefore;
            var forwardHash = snapshot();

            var samples = new List<SeekSample>();
            TestContext.Progress.WriteLine($"{mode}: {objects} {workload}, {frameCount} replay frames");
            for (var run = -1; run < 5; run++)
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    collect();
                    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    var start = Stopwatch.GetTimestamp();
                    var updates = seek(targets[i]);
                    var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                    verify(targets[i]);
                    collect();
                    var forwardAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    var forwardStart = Stopwatch.GetTimestamp();
                    var forwardUpdates = seek(sourceTime);
                    var forwardElapsed = Stopwatch.GetElapsedTime(forwardStart).TotalMilliseconds;
                    var forwardAllocation = GC.GetAllocatedBytesForCurrentThread() - forwardAllocatedBefore;
                    Assert.That(snapshot(), Is.EqualTo(forwardHash), "Continued replay must match uninterrupted playback.");
                    if (run >= 0)
                    {
                        samples.Add(new SeekSample(scenarios[i], targets[i], milliseconds, allocated, updates));
                        samples.Add(new SeekSample("forward-from-" + scenarios[i], sourceTime, forwardElapsed, forwardAllocation, forwardUpdates));
                    }
                }
            }

            var report = new
            {
                Mode = mode, Objects = objects, Profile = workload, ReplayFrames = frameCount, SourceTime = sourceTime,
                ForwardMilliseconds = forwardMilliseconds, ForwardAllocatedBytes = forwardAllocated,
                ForwardRetainedBytes = forwardRetained, VerificationHash = forwardHash,
                Seeks = samples.GroupBy(sample => sample.Scenario).Select(group => new
                {
                    Scenario = group.Key, Target = group.First().Target,
                    MedianMilliseconds = group.Select(sample => sample.Milliseconds).Order().ElementAt(2),
                    MaximumMilliseconds = group.Max(sample => sample.Milliseconds),
                    MedianAllocatedBytes = group.Select(sample => sample.AllocatedBytes).Order().ElementAt(2),
                    MaximumUpdates = group.Max(sample => sample.Updates),
                }).ToArray(),
                Samples = samples,
            };
            reports.Add(report);
        });

        AddStep("write report", () =>
        {
            var report = new
            {
                Test = $"ReplaySeek-{workload}-{objects}", Runtime = RuntimeInformation.FrameworkDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(), Configuration =
#if DEBUG
                    "Debug",
#else
                    "Release",
#endif
                TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), Reports = reports,
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            TestContext.Progress.WriteLine(json);
            var directory = Environment.GetEnvironmentVariable("BMS_PERFORMANCE_RESULTS");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, report.Test + ".json"), json);
            }
        });

        void verify(double time)
        {
            var hash = snapshot();
            if (!reference.TryGetValue(time, out var expected))
                reference.Add(time, hash);
            else
                Assert.That(hash, Is.EqualTo(expected), $"State at {time} must match the reference seek.");
        }
    }

    private int seek(double time)
    {
        Player.GameplayClockContainer.Stop();
        Player.GameplayClockContainer.Seek(time);
        var updates = 0;
        do
        {
            Player.DrawableRuleset.UpdateSubTree();
            if (++updates > 100000)
                throw new InvalidOperationException("Replay did not reach the requested time.");
        } while (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) > .001);
        return updates;
    }

    private string snapshot()
    {
        var score = (BmsScoreProcessor)Player.ScoreProcessor;
        var health = (BmsHealthProcessor)Player.HealthProcessor;
        var state = JsonSerializer.Serialize(new
        {
            Statistics = score.Statistics.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key).ToArray(),
            Combo = score.Combo.Value, HighestCombo = score.HighestCombo.Value, Score = score.TotalScore.Value,
            UnmodifiedScore = score.TotalScoreWithoutMods.Value, Accuracy = score.Accuracy.Value, Rank = score.Rank.Value,
            score.ScoringJudgementEventCount, score.JudgementEvents, Gauges = health.GaugeHistory.ToArray(),
            health.CurrentGaugeStates, health.HasFailed, health.HasEverFailed,
            Columns = Playfield.Stage.Columns.Cast<BmsColumn>().Select(column => new
            {
                column.Judgements.PressedDirections,
                Holds = Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().Where(note => note.Column == column.Index)
                    .Select(column.Judgements.GetLongNoteState).ToArray(),
            }).ToArray(),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    }

    private static void collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record SeekSample(string Scenario, double Target, double Milliseconds, long AllocatedBytes, int Updates);

    private sealed partial class SeekPlayer(ISkinSource skin, Action<int> recordFrameCount)
        : BmsTestSkins.SkinnedTestPlayer(skin, null)
    {
        protected override void PrepareReplay()
        {
            var frames = new BmsAutoGenerator((BmsBeatmap)GameplayState.Beatmap).Generate().Frames;
            recordFrameCount(frames.Count);
            DrawableRuleset.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = frames },
            });
        }
    }
}
