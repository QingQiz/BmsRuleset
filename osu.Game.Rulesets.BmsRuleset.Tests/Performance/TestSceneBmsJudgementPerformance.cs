using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Tests.Visualize;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[HeadlessTest, NonParallelizable, Explicit("Opt-in loaded-column measurements; select with a FullyQualifiedName filter."), Category("Performance")]
public partial class TestSceneBmsJudgementPerformance : BmsPlayerTestScene
{
    private int futureNotes;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(11000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1 },
                new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 10 }],
        };
        for (var i = 0; i < futureNotes; i++)
            beatmap.HitObjects.Add(i % 2 == 0
                ? new BmsLongNote { StartTime = 5000 + i * .01, Duration = 500, Column = 1 }
                : new BmsNote { StartTime = 5000 + i * .01, Column = 1 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(64)]
    [TestCase(2048)]
    public void LoadedColumnPhases(int count)
    {
        AddStep("load column", () => { futureNotes = count; LoadPlayer(); });
        AddUntilStep("loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("activate future candidates", () =>
        {
            Playfield.ScrollController.SetConfiguredScrollSpeed(1);
            Playfield.RefreshAllLifetimes();
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(3000);
        });
        AddUntilStep("reached measurement time", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 3000) < .001);
        AddStep("measure empty hold lookup and judgement phases", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.EqualTo(count + 2));
            const int operations = 20000;
            BmsJudgementPerformanceTest.Measure("Repress-empty-" + count, operations, () =>
            {
                var found = 0;
                return new BmsJudgementPerformanceTest.Measurement(() =>
                {
                    for (var i = 0; i < operations; i++)
                        if (container.TryRepress(3000, false) != null)
                            found++;
                }, () => { Assert.That(found, Is.Zero); return new { Found = found, Alive = container.AliveEntries.Count }; });
            });
            measurePhases(container, count, "idle", operations);
            Assert.That(Playfield.GetAliveObjectAtTime(3000).TryHit(HitResult.Perfect), Is.True);
            measurePhases(container, count, "active", operations);
        });
    }

    private static void measurePhases(BmsColumnHitObjectContainer container, int count, string state, int operations)
        => BmsJudgementPerformanceTest.Measure("Mine-HCN-" + state + "-" + count, operations, () =>
            new BmsJudgementPerformanceTest.Measurement(() =>
            {
                for (var i = 0; i < operations; i++)
                {
                    container.UpdateLandmines(false);
                    container.UpdateHellChargeBodies(false);
                }
            }, () => new { Alive = container.AliveEntries.Count, Active = container.AliveEntries.Values.Count(d => d.HitObject.StartTime == 3000 && d.Judged) }));
}
