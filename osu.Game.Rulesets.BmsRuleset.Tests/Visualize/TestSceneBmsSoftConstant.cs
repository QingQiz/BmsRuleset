using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsSoftConstant : BmsPlayerTestScene
{
    private BmsRulesetConfigManager config => (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset());

    private DrawableBmsHitObject[] notes = [];
    private double duration;
    private float visibleY;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmapFromChart("""
            #TITLE Soft Constant
            #BPM 120
            #BASEBPM 240
            #LNTYPE 1
            #00211:01
            #00252:01
            #00352:01
            #002D3:01
            #00234:01
            #01515:01
            """);
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestLiveToggleFadeAndRewindForAllNoteTypes()
    {
        AddStep("load soft constant player", () =>
        {
            config.SetValue(BmsRulesetSetting.SoftConstant, true);
            config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, 0.0);
            config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        AddStep("read target duration", () => duration = Playfield.ScrollController.ScrollRange / Playfield.ScrollController.ScrollSpeedMultiplier);
        seek(() => 4000 - duration - 100);
        AddUntilStep("all note types alive", () =>
        {
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(n => n.HitObject.StartTime == 4000).ToArray();
            return notes.Length == 4;
        });
        AddUntilStep("hidden before target", () => notes.All(n => visualAlpha(n) == 0));
        AddStep("disable live", () => config.SetValue(BmsRulesetSetting.SoftConstant, false));
        AddUntilStep("all types visible", () => notes.All(n => visualAlpha(n) == 1));
        AddStep("capture position", () => visibleY = notes[0].Y);
        AddStep("re-enable live", () => config.SetValue(BmsRulesetSetting.SoftConstant, true));
        AddUntilStep("hidden again", () => notes.All(n => visualAlpha(n) == 0));
        AddAssert("toggle preserves position", () => notes[0].Y == visibleY);
        AddAssert("no judgement from hiding", () => Player.Results.Count == 0);
        AddStep("enable advance fade", () => config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, 200.0));
        AddUntilStep("all types half faded", () => notes.All(n => Math.Abs(visualAlpha(n) - 0.5f) < 0.001));
        AddStep("enable delayed fade", () => config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, -200.0));
        seek(() => 4000 - duration + 100);
        AddUntilStep("delayed fade halfway", () => notes.All(n => Math.Abs(visualAlpha(n) - 0.5f) < 0.001));
        seek(() => 4000 - duration + 201);
        AddUntilStep("fully visible", () => notes.All(n => visualAlpha(n) == 1));
        seek(() => 4000 - duration - 100);
        AddUntilStep("rewind hides notes", () => Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
            .Where(n => n.HitObject.StartTime == 4000).All(n => visualAlpha(n) == 0));
        seek(() => 4500);
        AddUntilStep("misses still judged", () => Player.Results.Count >= 2);
        AddStep("restore defaults", () =>
        {
            config.SetValue(BmsRulesetSetting.SoftConstant, false);
            config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, 0.0);
            config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false);
        });
    }

    private static float visualAlpha(DrawableBmsHitObject note) => note.ChildrenOfType<Container>().First().Alpha;

    private void seek(Func<double> time)
    {
        AddStep("seek", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time());
        });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time()) < 0.001);
    }
}
