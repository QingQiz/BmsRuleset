#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSongProgress : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestAppearanceAndProgress()
    {
        BmsSongProgress progress() => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().Single();
        double firstHitTime() => Player.GameplayState.Beatmap.HitObjects.Min(hitObject => hitObject.StartTime);
        double lastHitTime() => Player.GameplayState.Beatmap.HitObjects.Max(hitObject => hitObject.GetEndTime());

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("song progress loaded", () => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().SingleOrDefault()?.IsLoaded == true);
        AddStep("stop clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("indicator remains left of playfield", () =>
            progress().ScreenSpaceDrawQuad.TopRight.X < Playfield.SkinnableComponentScreenSpaceDrawQuad.TopLeft.X);

        AddStep("seek to top", () => Player.GameplayClockContainer.Seek(firstHitTime()));
        AddStep("seek to middle", () => Player.GameplayClockContainer.Seek((firstHitTime() + lastHitTime()) / 2));
        AddStep("use cyan indicator", () => progress().IndicatorColour.Value = new Colour4(40, 220, 255, 255));
        AddStep("seek to bottom", () => Player.GameplayClockContainer.Seek(lastHitTime()));
        AddStep("restore default red", () => progress().IndicatorColour.SetDefault());
    }
}
