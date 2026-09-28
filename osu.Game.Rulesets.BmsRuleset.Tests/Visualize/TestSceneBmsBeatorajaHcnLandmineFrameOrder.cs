#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaHcnLandmineFrameOrder : BmsPlayerTestScene
{
    private int mineColumn = 1;
    private double total = 240;
    private bool releaseAtMine;
    private readonly List<Action> assertions = [];

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => createFrames());

    private IList<ReplayFrame> createFrames() => releaseAtMine
        ? [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Key1, BmsAction.Key2),
            new BmsReplayFrame(3201, BmsAction.Key1), new BmsReplayFrame(3202, BmsAction.Key1, BmsAction.Key2),
            new BmsReplayFrame(3210, BmsAction.Key1), new BmsReplayFrame(5000), new BmsReplayFrame(11000)]
        : [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Key1),
            new BmsReplayFrame(3201, BmsAction.Key1, BmsAction.Key2),
            new BmsReplayFrame(3210, BmsAction.Key1), new BmsReplayFrame(5000), new BmsReplayFrame(11000)];

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = total,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 2000, Column = 1 },
                new BmsLandmine { StartTime = 3201, Column = mineColumn, LandmineDamagePercent = 10 },
                new BmsNote { StartTime = 10000, Column = 3 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(1)]
    [TestCase(2)]
    public void MineBeforeHcnBodyAtGaugeCeiling(int column)
    {
        load(column, 240, false);
        // Java 9cddf911 JudgeManager 244-246 precedes all bodies at 306-338.
        // NORMAL TOTAL240 / 3 endpoints: head +80%, body +40%, mine -10%.
        checkpoint("first", 3200, 1, false, []);
        checkpoint("first", 3201, 1, true, [.9, 1]);
        checkpoint("rewound", 3200, 1, false, []);
        checkpoint("replayed", 3201, 1, true, [.9, 1]);
        assertTimeline();
    }

    [Test]
    public void UnsaturatedMineAndHcnBodyApplyExactlyOnceInReferenceOrder()
    {
        load(2, 12, false);
        // TOTAL12 / 3 gives head +4% and body +2%; neither operation clips.
        // Final 16% alone cannot distinguish ordering, but [14%,16%] can.
        checkpoint("first", 3200, .24, false, []);
        checkpoint("first", 3201, .16, true, [.14, .16]);
        checkpoint("rewound", 3200, .24, false, []);
        checkpoint("replayed", 3201, .16, true, [.14, .16]);
        assertTimeline();
    }

    [Test]
    public void SameFrameReleaseAvoidsMineAndLaterPressDoesNotDetonateIt()
    {
        load(2, 240, true);
        // The mine scan reads final physical keys, so a release at 3201 avoids
        // the mine even though Key2 was held at 3200. Passed mines stay passed.
        checkpoint("first", 3200, 1, false, []);
        checkpoint("first", 3201, 1, false, [1]);
        checkpoint("later press", 3202, 1, false, []);
        checkpoint("rewound", 3200, 1, false, []);
        checkpoint("replayed", 3201, 1, false, [1]);
        checkpoint("replayed later press", 3202, 1, false, []);
        assertTimeline();
    }

    private void load(int column, double chartTotal, bool release)
    {
        AddStep("load mine and HCN scenario", () =>
        {
            mineColumn = column;
            total = chartTotal;
            releaseAtMine = release;
            assertions.Clear();
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
    }

    private void checkpoint(string phase, double time, double expectedHealth, bool expectedMine, double[] expectedFrameHealth)
    {
        var label = phase + " at " + time;
        AddStep("seek " + label, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("reached " + label, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
        AddStep("capture " + label, () =>
        {
            var health = Player.HealthProcessor.Health.Value;
            var history = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.ToArray();
            var frameHealth = history.Where(e => e.Time == time)
                .Select(e => e.States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health).ToArray();
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.ToArray();
            HitResult[] expectedResults = expectedMine ? [HitResult.Perfect, HitResult.Meh] : [HitResult.Perfect];
            BmsJudgementSourceKind[] expectedKinds = expectedMine
                ? [BmsJudgementSourceKind.LongNote, BmsJudgementSourceKind.Landmine] : [BmsJudgementSourceKind.LongNote];
            int[] expectedColumns = expectedMine ? [1, mineColumn] : [1];
            double[] expectedTimes = expectedMine ? [3000, 3201] : [3000];
            double[] expectedDamage = expectedMine ? [0, 10] : [0];
            BmsTimingObservationKind[] expectedTimingKinds = expectedMine
                ? [BmsTimingObservationKind.LongNoteHead, BmsTimingObservationKind.Note] : [BmsTimingObservationKind.LongNoteHead];
            TestContext.WriteLine($"mineColumn={mineColumn}, TOTAL={total}, release={releaseAtMine}, {label}: "
                                  + $"HP={health:R}; frame=[{string.Join(",", frameHealth)}]; "
                                  + string.Join("; ", events.Select(e => $"{e.Result}/{e.Source.Kind}/col{e.Source.Column}"
                                      + $"@{e.TimingObservations.Single().ActualTime}/offset{e.TimingObservations.Single().TimeOffset}")));
            // A first red checkpoint must not prevent collecting independent rewind evidence.
            assertions.Add(() =>
            {
                Assert.That(health, Is.EqualTo(expectedHealth).Within(1e-8), "final health " + label);
                Assert.That(frameHealth, Is.EqualTo(expectedFrameHealth).Within(1e-8), "ordered NORMAL gauge history " + label);
                Assert.That(history.Any(e => e.Time > time), Is.False, "no future gauge history after rewind " + label);
                Assert.That(events.Select(e => e.Result), Is.EqualTo(expectedResults), "results, without extra endpoints " + label);
                Assert.That(events.Select(e => e.Source.Kind), Is.EqualTo(expectedKinds), "source kinds " + label);
                Assert.That(events.Select(e => e.Source.Column), Is.EqualTo(expectedColumns), "source columns " + label);
                Assert.That(events.Select(e => e.Source.StartTime), Is.EqualTo(expectedTimes), "source times " + label);
                Assert.That(events.Select(e => e.Source.LandmineDamagePercent), Is.EqualTo(expectedDamage), "mine damage " + label);
                Assert.That(events.All(e => !e.SuppressPenalty && e.TimingObservations.Count == 1), Is.True, "one unsuppressed observation per event " + label);
                Assert.That(events.Select(e => e.TimingObservations.Single().Kind), Is.EqualTo(expectedTimingKinds), "timing kinds " + label);
                Assert.That(events.Select(e => e.TimingObservations.Single().ActualTime), Is.EqualTo(expectedTimes), "actual times " + label);
                Assert.That(events.Select(e => e.TimingObservations.Single().ExpectedTime), Is.EqualTo(expectedTimes), "expected times " + label);
            });
        });
    }

    private void assertTimeline() => AddStep("assert initial and rewound timeline", () => Assert.Multiple(() =>
    {
        foreach (var assertion in assertions)
            assertion();
    }));
}
