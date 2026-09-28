using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaPmsConsumers : BmsPlayerTestScene
{
    private bool badScenario;
    private BmsScoreProcessor score => (BmsScoreProcessor)Player.ScoreProcessor;
    private BmsHitErrorMeter meter => Player.ChildrenOfType<BmsHitErrorMeter>().Single();
    private Container markers => meter.ChildrenOfType<Container>().Single(c => c.Name == "judgements");

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(createReplay);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Pms9K, TotalColumns = 9, Rank = 2, Total = 30,
            HitObjects = [new BmsNote { StartTime = 1000, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 }, new BmsNote { StartTime = 3000, Column = 1 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap) => badScenario
        ? [new BmsReplayFrame(0), new BmsReplayFrame(850, BmsAction.PmsKey2), new BmsReplayFrame(851),
            new BmsReplayFrame(860, BmsAction.PmsKey2), new BmsReplayFrame(861),
            new BmsReplayFrame(1000, BmsAction.PmsKey2), new BmsReplayFrame(1001), new BmsReplayFrame(11000)]
        : [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.PmsKey2), new BmsReplayFrame(1001),
            new BmsReplayFrame(1600, BmsAction.PmsKey2), new BmsReplayFrame(1601),
            new BmsReplayFrame(3000, BmsAction.PmsKey2), new BmsReplayFrame(3001), new BmsReplayFrame(11000)];

    [Test]
    public void NonConsumingBadRehitUsesActualMeterAndResultScatter()
    {
        load(true);
        checkpoint(875, () =>
        {
            assertState(0.28, 0, [0]);
            Assert.That(markers.AliveChildren, Has.Count.EqualTo(1));
            Assert.That(markers.AliveChildren.Single().X, Is.EqualTo(0.35).Within(1e-6));
            Assert.That(markers.AliveChildren.Single().Alpha, Is.GreaterThan(0));
            var saved = new ScoreInfo(); score.PopulateScore(saved);
            var hit = saved.HitEvents.Single();
            Assert.That(((BmsHitObject)hit.HitObject).Column, Is.EqualTo(1));
            Assert.That(hit.HitObject.StartTime, Is.EqualTo(1000));
            Assert.That(hit.TimeOffset, Is.EqualTo(-150));
            var scatter = BmsHitScatterStatistic.CreateStatistics((BmsBeatmap)Player.GameplayState.Beatmap, saved.HitEvents);
            Assert.That(scatter.Keys[1].Data.Points.Single().Offset, Is.EqualTo(-150));
            Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Ok), Is.EqualTo(1));
        });
        checkpoint(950, () =>
        {
            assertState(0.28, 0, [0]);
            Assert.That(markers.AliveChildren, Is.Empty, "the second BAD cannot add a marker after seek clears the old marker");
        });
        checkpoint(1050, afterRehit);
        checkpoint(875, () => assertState(0.28, 0, [0]));
        checkpoint(1050, afterRehit);
        checkpoint(800, () => assertState(0.30, 0, [0]));
        checkpoint(1050, afterRehit);

        void afterRehit()
        {
            assertState(0.38, 1, [0, 2]);
            var saved = new ScoreInfo(); score.PopulateScore(saved);
            Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Ok), Is.EqualTo(1));
            Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Perfect), Is.EqualTo(1));
            Assert.That(saved.HitEvents.Select(e => e.TimeOffset), Is.EqualTo(new[] { -150d, 0 }));
        }
    }

    [Test]
    public void SuppressedPoorKeepsProgressAndArchiveWithoutFeedbackOrSecondPenalty()
    {
        load(false);
        checkpoint(1700, () => assertState(0.34, 0, [0, 2]));
        checkpoint(2250, () =>
        {
            assertState(0.34, 0, [0, 2, 2]);
            Assert.That(markers.AliveChildren, Is.Empty, "suppressed passive POOR adds no marker");
            Assert.That(score.JudgementEvents.Last().SuppressPenalty, Is.True);
        });
        checkpoint(3050, finalState);
        checkpoint(1700, () => assertState(0.34, 0, [0, 2]));
        checkpoint(3050, finalState);
        checkpoint(1500, () => assertState(0.40, 1, [0, 2]));
        checkpoint(3050, finalState);
        checkpoint(3400, () => { finalState(); Assert.That(score.HasCompleted.Value, Is.True); });

        void finalState()
        {
            assertState(0.44, 1, [0, 2, 2, 4]);
            var saved = new Score(); score.PopulateScore(saved.ScoreInfo);
            var restored = BmsTestReplayArchive.RoundTrip(saved);
            Assert.That(BmsJudgementEventStore.TryGet(restored.ScoreInfo, out var events), Is.True);
            Assert.That(BmsExScore.CreateProgression(events), Is.EqualTo(new[] { 0, 2, 2, 4 }));
            Assert.That(events.Single(e => e.SuppressPenalty).Source.IsScoring, Is.True);
            Assert.That(restored.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Miss), Is.EqualTo(1));
            Assert.That(restored.ScoreInfo.Statistics.GetValueOrDefault(HitResult.Meh), Is.Zero);
            Assert.That(restored.ScoreInfo.HitEvents.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect, HitResult.Miss, HitResult.Perfect }));
            Assert.That(BmsScoreGraph.ScoreAtProgress(4, BmsExScore.CreateProgression(events), 2, 3), Is.EqualTo(2));
            Assert.That(score.Accuracy.Value, Is.EqualTo(2d / 3).Within(1e-10));
            // GaugeProperty.NORMAL_PMS has a 120% ceiling; graph positions are normalised to it.
            Assert.That(BmsGaugeHistoryGraph.CreateSeries(restored.ScoreInfo, Player.GameplayState.Beatmap).Single().Points.Last().Health,
                Is.EqualTo(44d / 120).Within(1e-8), "archive fallback gauge must not charge suppressed POOR again");
        }
    }

    private void load(bool bad)
    {
        AddStep("load PMS replay", () => { badScenario = bad; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("meter loaded", () => Player.ChildrenOfType<BmsHitErrorMeter>().Any(m => m.IsLoaded));
    }

    private void checkpoint(double time, Action assertion)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("simulation reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
        AddWaitStep("HUD scheduled feedback", 2);
        AddStep("assert " + time, assertion);
    }

    private void assertState(double health, int combo, int[] progression)
    {
        Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health).Within(1e-8));
        Assert.That(score.Combo.Value, Is.EqualTo(combo));
        Assert.That(BmsExScore.CreateProgression(score.JudgementEvents), Is.EqualTo(progression));
        Assert.That(score.ScoringJudgementEventCount, Is.EqualTo(progression.Length - 1));
    }
}
