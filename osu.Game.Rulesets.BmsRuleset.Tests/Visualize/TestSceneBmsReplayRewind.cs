using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsReplayRewind : BmsPlayerTestScene
{
    private bool missHead;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ =>
    [
        new BmsReplayFrame(0),
        // An early press produces a real Empty POOR which must itself be undone by seeking.
        new BmsReplayFrame(500, BmsAction.Key1),
        new BmsReplayFrame(510),
        new BmsReplayFrame(missHead ? 1500 : 1000, BmsAction.Key1),
        new BmsReplayFrame(1700),
        new BmsReplayFrame(2000, BmsAction.Key1),
        new BmsReplayFrame(3000),
        new BmsReplayFrame(4000, BmsAction.Key2),
        new BmsReplayFrame(4010),
        new BmsReplayFrame(5000),
    ]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 20,
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Duration = 2000, Column = 1 },
                new BmsNote { StartTime = 4000, Column = 2 },
            },
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestImperfectLongNoteReplay(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(false, true)] bool missedHead)
    {
        var snapshots = new Dictionary<double, Snapshot>();
        AddStep("load native long note", () =>
        {
            missHead = missedHead;
            Mod modeMod = mode switch
            {
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                _ => new BmsModLongNote(),
            };
            LoadPlayer([modeMod]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("track all gauge layers", () =>
            ((BmsHealthProcessor)Player.HealthProcessor).SetGaugeTypes([BmsGaugeType.Hard, BmsGaugeType.Normal], replaceExisting: true));

        double[] checkpoints = [0, 1550, 1685, 1800, 2200, 4500];
        foreach (var time in checkpoints)
        {
            seek(time);
            AddStep($"snapshot at {time}", () => snapshots[time] = snapshot());
        }
        AddAssert("fixture includes Empty POOR and POOR", () =>
            Player.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Miss) > 0
            && Player.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Meh) > 0);

        double[] rewindTargets = [2200, 1800, 1685, 1550, 0];
        foreach (var target in rewindTargets)
        {
            seek(target);
            assertSnapshot($"restore snapshot at {target}", () => snapshots[target]);
            // HCN samples holding once per simulation frame. Use the same checkpoints so a
            // changed frame boundary at key-up does not change the forward integration itself.
            foreach (var time in checkpoints.Where(time => time > target))
            {
                seek(time);
                assertSnapshot($"replay from {target} at {time}", () => snapshots[time]);
            }
        }
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }

    private Snapshot snapshot()
    {
        var score = (BmsScoreProcessor)Player.ScoreProcessor;
        var health = (BmsHealthProcessor)Player.HealthProcessor;
        return new Snapshot(score.Statistics.OrderBy(p => p.Key).Where(p => p.Value != 0).ToArray(), score.Combo.Value,
            score.HighestCombo.Value, score.ScoringJudgementEventCount, score.JudgementEvents.SelectMany(e => e.TimingObservations).ToArray(),
            health.CurrentGaugeStates.ToArray(), health.GaugeHistory.Count, health.HasFailed, health.HasEverFailed);
    }

    private void assertSnapshot(string label, Func<Snapshot> expected)
        => AddAssert(label, () =>
        {
            var actual = snapshot();
            var previous = expected();
            Assert.Multiple(() =>
            {
                Assert.That(actual.Statistics, Is.EqualTo(previous.Statistics));
                Assert.That(actual.Combo, Is.EqualTo(previous.Combo));
                Assert.That(actual.HighestCombo, Is.EqualTo(previous.HighestCombo));
                Assert.That(actual.ScoringEvents, Is.EqualTo(previous.ScoringEvents));
                Assert.That(actual.Observations, Is.EqualTo(previous.Observations));
                Assert.That(actual.Gauges, Is.EqualTo(previous.Gauges), label);
                Assert.That(actual.GaugeEvents, Is.EqualTo(previous.GaugeEvents), label);
                Assert.That(actual.Failed, Is.EqualTo(previous.Failed));
                Assert.That(actual.EverFailed, Is.EqualTo(previous.EverFailed));
            });
            return true;
        });

    private sealed record Snapshot(KeyValuePair<HitResult, int>[] Statistics, int Combo, int HighestCombo, int ScoringEvents,
                                   BmsTimingObservation[] Observations, BmsGaugeStateSnapshot[] Gauges, int GaugeEvents, bool Failed, bool EverFailed);
}
