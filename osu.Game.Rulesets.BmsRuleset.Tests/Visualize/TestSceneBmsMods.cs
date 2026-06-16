using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
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

    private DrawableBmsHitObject firstAliveScratchNote()
        => Playfield.HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => BmsLayout.IsScratchColumn(d.HitObject.Column, Playfield.LayoutVariant));

    [Test]
    public void TestAutoScratch()
    {
        this.AddSetupStep("load player with AS mod", () => LoadPlayer([new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("scratch not hidden", () => !Playfield.Stage.Columns[0].Hidden);
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

        AddAssert("scratch hidden", () => Playfield.Stage.Columns[0].Hidden);

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

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

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

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("scratch hidden", () => Playfield.Stage.Columns[0].Hidden);

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

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("scratch not hidden", () => !Playfield.Stage.Columns[0].Hidden);
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

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

        AddAssert("scratch hidden", () => Playfield.Stage.Columns[0].Hidden);
    }

    [Test]
    public void TestSecondPlayerWithMirror()
    {
        this.AddSetupStep("2P+MR", () => LoadPlayer([new BmsModSecondPlayer(), new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("stage scratch on right", () => BmsLayout.Is2P(Playfield.LayoutVariant));

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

        AddAssert("scratch column index 0 is scratch", () => Playfield.Stage.Columns[0].IsScratch);
    }

    [Test]
    public void TestLaneRandomWithSeed()
    {
        var mod = new BmsModLaneRandom { Seed = { Value = 12345 } };

        this.AddSetupStep("load player with LRN mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());

        AddAssert("all non-scratch columns appear exactly once (bijection)", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            // With IncludeScratch=false (default), column 0 stays at 0.
            // Non-scratch notes at indices 1-7 must be a permutation of [1,2,3,4,5,6,7].
            if (normalNotes.Count < 8) return false;

            var nonScratch = normalNotes.Skip(1).Take(7).Select(n => n.Column).OrderBy(c => c).ToArray();
            return nonScratch.SequenceEqual(new[] { 1, 2, 3, 4, 5, 6, 7 });
        });
    }

    [Test]
    public void TestLaneRandomCustomOrder()
    {
        var mod = new BmsModLaneRandom { LaneOrder = { Value = "0,2,4,6,1,3,5,7" } };

        this.AddSetupStep("load player with LRN custom order", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("all 48 notes match LaneOrder mapping", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern =
            [
                0, 2, 4, 6, 1, 3, 5, 7,
                0, 4, 2, 6, 3, 7, 1, 5,
                0, 1, 2, 3, 4, 5, 6, 7,
                7, 6, 5, 4, 3, 2, 1, 0,
                0, 2, 4, 6, 1, 3, 5, 7,
                0, 4, 2, 6, 3, 7, 1, 5,
            ];

            // Parse LaneOrder "0,2,4,6,1,3,5,7" into a mapping array.
            int[] laneOrderMap = [0, 2, 4, 6, 1, 3, 5, 7];

            if (normalNotes.Count < originalPattern.Length)
                return false;

            for (int i = 0; i < originalPattern.Length; i++)
            {
                int expected = laneOrderMap[originalPattern[i]];
                if (normalNotes[i].Column != expected)
                    return false;
            }

            return true;
        });
    }

    [Test]
    public void TestNoteRandomSRandom()
    {
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.S_Random }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN S-Random mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("notes shuffled from original columns", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0];

            // At least some notes should differ from the original pattern.
            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestNoteRandomHRandom()
    {
        var mod = new BmsModNoteRandom { Mode = { Value = BmsNoteRandomMode.H_Random }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN H-Random mod", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("notes shuffled from original columns", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0];

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                if (normalNotes[i].Column != originalPattern[i])
                    return true;
            }

            return false;
        });
    }

    [Test]
    public void TestNoteRandomExcludeScratch()
    {
        var mod = new BmsModNoteRandom { IncludeScratch = { Value = false }, Seed = { Value = 42 } };

        this.AddSetupStep("load player with NRN exclude scratch", () => LoadPlayer([mod]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddAssert("scratch notes preserved, non-scratch columns populated", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects
                .Where(h => !h.IsLongNote && !h.IsMine)
                .ToList();

            // IncludeScratch=false keeps original scratch notes at column 0.
            bool hasScratchNotes = normalNotes.Any(n => n.Column == 0);
            // Non-scratch notes are shuffled among columns 1-7.
            bool hasNonScratchNotes = normalNotes.Any(n => n.Column > 0);
            return hasScratchNotes && hasNonScratchNotes;
        });
    }

    [Test]
    public void TestRandomModsIncompatible()
    {
        AddAssert("LRN incompatible with NRN and MR", () =>
        {
            var lrn = new BmsModLaneRandom();
            return lrn.IncompatibleMods.Contains(typeof(BmsModNoteRandom))
                   && lrn.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
        AddAssert("NRN incompatible with LRN and MR", () =>
        {
            var nrn = new BmsModNoteRandom();
            return nrn.IncompatibleMods.Contains(typeof(BmsModLaneRandom))
                   && nrn.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
        AddAssert("RR incompatible with LRN, NRN, and MR", () =>
        {
            var rr = new BmsModRotationRandom();
            return rr.IncompatibleMods.Contains(typeof(BmsModLaneRandom))
                   && rr.IncompatibleMods.Contains(typeof(BmsModNoteRandom))
                   && rr.IncompatibleMods.Contains(typeof(BmsModMirror));
        });
    }
}
