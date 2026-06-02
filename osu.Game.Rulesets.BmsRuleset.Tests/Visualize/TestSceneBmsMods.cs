using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsMods : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateWatchOnlyFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private DrawableBmsHitObject? firstAliveScratchNote()
        => Playfield.HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => BmsSkinComponentLookup.IsScratchColumn(d.HitObject.Column, Playfield.LayoutVariant));

    [Test]
    public void TestAutoScratch()
    {
        this.AddSetupStep("load player with AS mod", () => LoadPlayer([new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch not hidden", () => !Playfield.HideScratch);
        AddAssert("judgement area visible", () => Playfield.Stage.JudgementArea.Count, () => Is.GreaterThan(0));
    }

    [Test]
    public void TestAutoScratchHideScratch()
    {
        var mod = new BmsModAutoScratch { HideScratch = { Value = true } };

        this.AddSetupStep("load player with AS+HS mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch hidden", () => Playfield.HideScratch);

        // Regression (issue 4): hidden scratch notes live in a zero-width column and are
        // auto-judged. They must never become visible/freeze on screen. Alpha is enforced in
        // DrawableBmsHitObject.Update so this holds even in headless mode without draw geometry.
        AddStep("seek to start", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("scratch note alive", () => firstAliveScratchNote() != null);
        AddUntilStep("hidden scratch note stays invisible", () => firstAliveScratchNote()?.Alpha == 0f);
    }

    [Test]
    public void TestMirror()
    {
        this.AddSetupStep("load player with MR mod", () => LoadPlayer([new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("hit object columns mirrored", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                var expected = mirroredColumn(originalPattern[i]);
                if (normalNotes[i].Column != expected)
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestSecondPlayer()
    {
        this.AddSetupStep("load player with 2P mod", () => LoadPlayer([new BmsModSecondPlayer()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("layout variant switched to 2P", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            return beatmap.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        });

        AddAssert("hit object columns unchanged (scratch=0, keys=1..N)", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return false;
            }

            return true;
        });

        AddAssert("scratch column width overridden by skin (≠ default)", () =>
        {
            var stage = Playfield.Stage;
            return stage.Columns[0].IsScratch
                   && stage.Columns[0].Width != BmsColumn.COLUMN_WIDTH
                   && stage.Columns[0].Width != BmsColumn.SCRATCH_COLUMN_WIDTH;
        });

        AddAssert("key columns have key width overridden by skin (≠ default)", () =>
        {
            var stage = Playfield.Stage;
            var keyWidth = stage.Columns[1].Width;
            return stage.Columns.Skip(1).All(c => c.IsScratch == false)
                   && stage.Columns.Skip(1).All(c => c.Width == keyWidth)
                   && keyWidth != BmsColumn.COLUMN_WIDTH
                   && keyWidth != BmsColumn.SCRATCH_COLUMN_WIDTH;
        });

        AddAssert("input maps scratch action to column 0", () =>
        {
            var variant = ((BmsBeatmap)Player.GameplayState.Beatmap).LayoutVariant;
            return BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, variant) == 0;
        });

        AddAssert("input maps key actions to columns 1..N", () =>
        {
            var variant = ((BmsBeatmap)Player.GameplayState.Beatmap).LayoutVariant;
            return BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key1, variant) == 1
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key2, variant) == 2
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key3, variant) == 3
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key4, variant) == 4
                   && BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key5, variant) == 5;
        });
    }

    [Test]
    public void TestSecondPlayerWithAllMods()
    {
        var asHs = new BmsModAutoScratch { HideScratch = { Value = true } };

        this.AddSetupStep("2P+HS+MR mods", () => LoadPlayer([new BmsModSecondPlayer(), asHs, new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("layout variant switched to 2P", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            return beatmap.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        });

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch hidden", () => Playfield.HideScratch);

        AddAssert("hit object columns mirrored with 2P variant", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != mirroredColumn(originalPattern[i]))
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestSecondPlayerWithAutoScratch()
    {
        this.AddSetupStep("2P+AS mods", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("layout variant switched to 2P", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            return beatmap.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        });

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch not hidden", () => !Playfield.HideScratch);
        AddAssert("scratch column index 0 is scratch", () => Playfield.Stage.Columns[0].IsScratch);
    }

    [Test]
    public void TestSecondPlayerWithHideScratch()
    {
        var asHs = new BmsModAutoScratch { HideScratch = { Value = true } };

        this.AddSetupStep("2P+HS mods", () => LoadPlayer([new BmsModSecondPlayer(), asHs]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("layout variant switched to 2P", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            return beatmap.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        });

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch hidden", () => Playfield.HideScratch);
    }

    [Test]
    public void TestSecondPlayerWithMirror()
    {
        this.AddSetupStep("2P+MR", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("layout variant switched to 2P", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            return beatmap.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        });

        AddAssert("hit object columns mirrored with 2P variant", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var variant = beatmap.LayoutVariant;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != mirroredColumn(originalPattern[i]))
                    return false;
            }

            return true;
        });

        AddAssert("scratch column index 0 is scratch", () => Playfield.Stage.Columns[0].IsScratch);
    }
}
