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
public partial class TestSceneBmsBeatorajaHcnFrameOrder : BmsPlayerTestScene
{
    private int hcnColumn;
    private double total;
    private bool released;
    private readonly List<Action> assertions = [];

    private BmsAction held => hcnColumn == 1 ? BmsAction.Key1 : BmsAction.Key2;
    private BmsAction tap => hcnColumn == 1 ? BmsAction.Key2 : BmsAction.Key1;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => createFrames());

    private IList<ReplayFrame> createFrames() => released
        ? [new BmsReplayFrame(0), new BmsReplayFrame(3000, held), new BmsReplayFrame(3001),
            new BmsReplayFrame(3201, tap), new BmsReplayFrame(3210), new BmsReplayFrame(11000)]
        : [new BmsReplayFrame(0), new BmsReplayFrame(3000, held), new BmsReplayFrame(3201, held, tap),
            new BmsReplayFrame(3210, held), new BmsReplayFrame(5000), new BmsReplayFrame(11000)];

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = total,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 2000, Column = hcnColumn },
                new BmsNote { StartTime = released ? 3201 : 3350, Column = hcnColumn == 1 ? 2 : 1 },
                new BmsNote { StartTime = 10000, Column = 1 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(1)]
    [TestCase(2)]
    public void BodyBeforeOtherColumnBadAtGaugeCeiling(int column)
    {
        load(column, 320, false);
        // Java 9cddf911 JudgeManager 310-338 precedes every key change at 347.
        // NORMAL starts at 20%; TOTAL320 / 4 endpoints gives +80% per PG, +40% body, BAD -3%.
        checkpoint(3200, 1, [HitResult.Perfect], [3000], [0]);
        checkpoint(3201, .97, [HitResult.Perfect, HitResult.Ok], [3000, 3201], [0, -149], [1, .97]);
        checkpoint(3200, 1, [HitResult.Perfect], [3000], [0]);
        checkpoint(3201, .97, [HitResult.Perfect, HitResult.Ok], [3000, 3201], [0, -149], [1, .97]);
        assertTimeline();
    }

    [Test]
    public void UnsaturatedBodyAndOtherColumnBadApplyExactlyOneTick()
    {
        load(1, 12, false);
        // Without clipping, one +1.5% body and one -3% BAD must net -1.5% in either order.
        checkpoint(3200, .23, [HitResult.Perfect], [3000], [0]);
        checkpoint(3201, .215, [HitResult.Perfect, HitResult.Ok], [3000, 3201], [0, -149]);
        checkpoint(3200, .23, [HitResult.Perfect], [3000], [0]);
        checkpoint(3201, .215, [HitResult.Perfect, HitResult.Ok], [3000, 3201], [0, -149]);
        assertTimeline();
    }

    [Test]
    public void ReleasedBodyBeforeOtherColumnPerfectAtGaugeCeiling()
    {
        load(1, 320, true);
        // The early release is POOR (-6%). At 3201 body damage (-1.5%) comes before
        // the other column's +80% PG, which clamps to 100%; reversing them leaves 98.5%.
        checkpoint(3200, .94, [HitResult.Perfect, HitResult.Meh], [3000, 3001], [0, -1999]);
        checkpoint(3201, 1, [HitResult.Perfect, HitResult.Meh, HitResult.Perfect], [3000, 3001, 3201], [0, -1999, 0], [.925, 1]);
        checkpoint(3200, .94, [HitResult.Perfect, HitResult.Meh], [3000, 3001], [0, -1999]);
        checkpoint(3201, 1, [HitResult.Perfect, HitResult.Meh, HitResult.Perfect], [3000, 3001, 3201], [0, -1999, 0], [.925, 1]);
        assertTimeline();
    }

    private void load(int column, double chartTotal, bool release)
    {
        AddStep("load frame order scenario", () =>
        {
            hcnColumn = column;
            total = chartTotal;
            released = release;
            assertions.Clear();
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
    }

    private void checkpoint(double time, double expectedHealth, HitResult[] expectedResults, double[] expectedTimes,
                            double[] expectedOffsets, double[]? expectedFrameHealth = null)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
        AddStep("capture " + time, () =>
        {
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.ToArray();
            var health = Player.HealthProcessor.Health.Value;
            var frameHealth = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Where(e => e.Time == time)
                .Select(e => e.States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health).ToArray();
            var columns = released && time == 3200 ? new[] { hcnColumn, hcnColumn }
                : expectedResults.Select((_, i) => i == 0 || released && i == 1 ? hcnColumn : hcnColumn == 1 ? 2 : 1).ToArray();
            TestContext.WriteLine($"{time}: HP={health:R}; frame=[{string.Join(",", frameHealth)}]; "
                                  + string.Join("; ", events.Select(e => $"{e.Result}/col{e.Source.Column}@{e.TimingObservations.Single().ActualTime}")));
            // Defer assertions on immutable snapshots so the known initial red result cannot
            // prevent the rewind path from running and reporting its own evidence.
            assertions.Add(() =>
            {
                Assert.That(health, Is.EqualTo(expectedHealth).Within(1e-8), "health at " + time);
                Assert.That(events.Select(e => e.Result), Is.EqualTo(expectedResults), "results at " + time);
                Assert.That(events.Select(e => e.Source.Column), Is.EqualTo(columns), "columns at " + time);
                Assert.That(events.Select(e => e.TimingObservations.Single().ActualTime), Is.EqualTo(expectedTimes), "input times at " + time);
                Assert.That(events.Select(e => e.TimingObservations.Single().TimeOffset), Is.EqualTo(expectedOffsets), "offsets at " + time);
                Assert.That(frameHealth, Has.Length.EqualTo(time == 3200 ? 0 : 2), "strict >200ms and exactly one body tick");
                if (expectedFrameHealth != null)
                    Assert.That(frameHealth, Is.EqualTo(expectedFrameHealth).Within(1e-8), "ordered NORMAL gauge values at " + time);
            });
        });
    }

    private void assertTimeline() => AddStep("assert initial and rewound timeline", () => Assert.Multiple(() =>
    {
        foreach (var assertion in assertions)
            assertion();
    }));
}
