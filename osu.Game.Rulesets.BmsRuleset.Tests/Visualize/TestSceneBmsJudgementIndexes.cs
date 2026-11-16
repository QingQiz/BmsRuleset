#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RhythmEngine.Profiles.Common;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsJudgementIndexes : BmsPlayerTestScene
{
    private const int future_note_count = 128;
    private bool includeMine;
    private bool includeLongNotes;
    [Test]
    public void RefreshingVisualMembershipDoesNotAllocateBindings()
    {
        load(false);
        seek(3000);
        AddStep("refresh visuals without allocating bindings", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            var refresh = typeof(BmsColumnHitObjectContainer).GetMethod("refreshCoreOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(container);
            for (var i = 0; i < 32; i++)
            {
                container.InvalidateCoreOrder();
                refresh();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 32; i++)
            {
                container.InvalidateCoreOrder();
                refresh();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero);
        });
    }

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(11000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2,
            Total = (3 + 3 * future_note_count) * 4,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = includeLongNotes ? [new BmsLongNote { Kind = KeyNoteKind.SustainHold, StartTime = 3000, Duration = 1000, Column = 1 }] : [],
        };
        if (includeMine)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 10 });
        if (orderingFixture)
        {
            for (var i = 0; i < 4; i++)
            {
                beatmap.HitObjects.Add(new BmsLandmine { StartTime = 5100, Column = 1 });
            }

            beatmap.InvisibleNotes = Enumerable.Range(0, 4).Select(_ => new BmsInvisibleNote { StartTime = 5100, Column = 1 }).ToArray();
        }

        for (var i = 0; i < future_note_count; i++)
        {
            if (includeLongNotes)
                beatmap.HitObjects.Add(new BmsLongNote { Kind = KeyNoteKind.SustainHold, StartTime = 5000 + i, Duration = 500, Column = 1 });
            beatmap.HitObjects.Add(new BmsNote { StartTime = 5200 + i, Column = 1 });
        }

        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 2 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void load(bool mine, bool ordering = false, bool longNotes = true)
    {
        AddStep("load dense column", () =>
        {
            includeMine = mine;
            includeLongNotes = longNotes;
            orderingFixture = ordering;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("show future notes", () =>
        {
            Playfield.ScrollController.SetConfiguredScrollSpeed(1);
            Playfield.RefreshAllLifetimes();
        });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }
}
