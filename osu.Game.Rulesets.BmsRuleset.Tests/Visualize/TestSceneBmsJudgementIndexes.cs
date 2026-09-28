#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsJudgementIndexes : BmsPlayerTestScene
{
    private const int future_note_count = 128;
    private bool includeMine;
    private static int repressCalls;
    private static int unrelatedBodyCalls;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(11000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2,
            Total = (3 + 3 * future_note_count) * 4,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1 }],
        };
        if (includeMine)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 10 });
        for (var i = 0; i < future_note_count; i++)
        {
            beatmap.HitObjects.Add(new BmsLongNote { StartTime = 5000 + i, Duration = 500, Column = 1 });
            beatmap.HitObjects.Add(new BmsNote { StartTime = 5200 + i, Column = 1 });
        }
        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 2 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HeadAndRepressIgnoreFutureLongNotes(bool mine)
    {
        load(mine);
        seek(3000);
        AddStep("head and active hold use the same judgement with bounded work", () =>
        {
            var column = Playfield.Stage.Columns[1];
            var head = Playfield.GetAliveObjectAtTime(3000)!;
            Assert.That(column.HitObjectContainer.AliveEntries.Values.Count(d => d.HitObject is BmsLongNote), Is.GreaterThan(64));
            using var probe = new ScopedMethodProbe(head.GetType().GetMethod(nameof(ILongNoteHolder.TryRepress)),
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countRepress), BindingFlags.Static | BindingFlags.NonPublic));

            repressCalls = 0;
            Assert.That(column.HandlePress(3000), Is.EqualTo(PressOutcome.Hit));
            Assert.That(repressCalls, Is.Zero, "An exact head press must not query unstarted future holds.");
            Assert.That(column.HandlePress(3001), Is.EqualTo(PressOutcome.Hit));
            Assert.That(repressCalls, Is.EqualTo(1), "Only the active hold participates in a repress.");
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect }));
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.24).Within(1e-8));
        });
    }

    [Test]
    public void MineColumnBodyPassSkipsTapsAndUnstartedHolds()
    {
        load(true);
        seek(3000);
        AddStep("start HCN", () => Assert.That(Playfield.Stage.Columns[1].HandlePress(3000), Is.EqualTo(PressOutcome.Hit)));
        seek(3200);
        AddStep("body phase work depends on active holds", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var head = Playfield.GetAliveObjectAtTime(3000)!;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            using var tapProbe = new ScopedMethodProbe(typeof(DrawableBmsHitObject).GetMethod("UpdateHellChargeBody", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countUnrelatedBody), BindingFlags.Static | BindingFlags.NonPublic));
            using var holdProbe = new ScopedMethodProbe(head.GetType().GetMethod("UpdateHellChargeBody", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countUnstartedBody), BindingFlags.Static | BindingFlags.NonPublic));
            unrelatedBodyCalls = 0;
            for (var i = 0; i < 20; i++)
            {
                container.UpdateLandmines(true);
                container.UpdateHellChargeBodies(true);
            }
            Assert.That(unrelatedBodyCalls, Is.Zero, "The body phase must not visit taps, mines or unstarted long notes.");
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.24).Within(1e-8), "Repeated passes at one timestamp must not tick.");
        });
        seek(3201);
        AddStep("mine still precedes the strict body tick", () =>
        {
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.16).Within(1e-8));
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Source.Kind),
                Is.EqualTo(new[] { BmsJudgementSourceKind.LongNote, BmsJudgementSourceKind.Landmine }));
        });
    }

    private void load(bool mine)
    {
        AddStep("load dense column", () => { includeMine = mine; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("show future notes", () =>
        {
            Playfield.ScrollController.SetConfiguredScrollSpeed(1);
            Playfield.RefreshAllLifetimes();
        });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private static void countRepress() => repressCalls++;
    private static void countUnrelatedBody() => unrelatedBodyCalls++;
    private static void countUnstartedBody(DrawableBmsHitObject __instance)
    {
        if (__instance.HasPendingHead)
            unrelatedBodyCalls++;
    }
}
