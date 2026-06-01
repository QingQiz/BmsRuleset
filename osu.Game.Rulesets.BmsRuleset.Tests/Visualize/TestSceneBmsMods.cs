using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
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

    [Test]
    public void TestAutoScratch()
    {
        this.AddSetupStep("load player with AS mod", () => LoadPlayer([new BmsModAutoScratch()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<UI.BmsPlayfield>());

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
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<UI.BmsPlayfield>());

        AddAssert("auto scratch enabled", () => Playfield.IsAutoScratch);
        AddAssert("scratch hidden", () => Playfield.HideScratch);
    }

    [Test]
    public void TestMirror()
    {
        this.AddSetupStep("load player with MR mod", () => LoadPlayer([new BmsModMirror()]));
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<UI.BmsPlayfield>());

        AddAssert("hit object columns mirrored", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var normalNotes = beatmap.HitObjects.OfType<BmsHitObject>()
                .Where(h => !h.IsLongNote && !h.IsMine)
                .OrderBy(h => h.StartTime)
                .ToList();

            int[] originalPattern = [0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5, 0, 1, 2, 3, 4, 5, 6, 7, 7, 6, 5, 4, 3, 2, 1, 0, 0, 2, 4, 6, 1, 3, 5, 7, 0, 4, 2, 6, 3, 7, 1, 5];
            int mirroredColumn(int col) => col switch { 0 => 0, 1 => 7, 2 => 6, 3 => 5, 4 => 4, 5 => 3, 6 => 2, 7 => 1, _ => col };

            for (var i = 0; i < originalPattern.Length && i < normalNotes.Count; i++)
            {
                int expected = mirroredColumn(originalPattern[i]);
                if (normalNotes[i].Column != expected)
                    return false;
            }

            return true;
        });
    }
}
