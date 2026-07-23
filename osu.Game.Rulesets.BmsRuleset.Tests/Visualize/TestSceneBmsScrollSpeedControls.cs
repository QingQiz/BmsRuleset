using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsScrollSpeedControls : BmsPlayerTestScene
{
    private float normalSpacing;
    private double firstTime;
    private double secondTime;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 2500, bpm: 120);
        return beatmap;
    }

    [Test]
    public void TestKeyboardScrollSpeedControlsAffectSpacing()
    {
        this.AddSetupStep("load Argon player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddStep("capture target times", () =>
        {
            var firstTwo = ((BmsBeatmap)Player.GameplayState.Beatmap).HitObjects
                .OrderBy(h => h.StartTime)
                .Take(2)
                .ToArray();

            firstTime = firstTwo[0].StartTime;
            secondTime = firstTwo[1].StartTime;
        });

        AddStep("seek before notes", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("two notes alive", () => Playfield.GetAliveObjectAtTime(firstTime) != null && Playfield.GetAliveObjectAtTime(secondTime) != null);
        AddUntilStep("spacing measurable", () => Playfield.SpacingBetweenTimes(firstTime, secondTime), () => Is.GreaterThan(1));
        AddStep("capture spacing", () => normalSpacing = Playfield.SpacingBetweenTimes(firstTime, secondTime));

        AddStep("press up", () => Playfield.ScrollController.AdjustScrollSpeed(1));
        AddUntilStep("scroll speed increased", () => Playfield.ScrollSpeed, () => Is.EqualTo(10).Within(0.001));
        AddUntilStep("spacing visibly increased", () => Playfield.SpacingBetweenTimes(firstTime, secondTime), () => Is.GreaterThan(normalSpacing * 1.1f));

        AddStep("press down", () => Playfield.ScrollController.AdjustScrollSpeed(-1));
        AddUntilStep("scroll speed restored", () => Playfield.ScrollSpeed, () => Is.EqualTo(8).Within(0.001));
    }

    [Test]
    public void TestScrollSpeedChangeShowsTextHud()
    {
        this.AddSetupStep("load Argon player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupUntilStep("text hud loaded", () => Player.HUDOverlay.ChildrenOfType<BmsTextHud>().SingleOrDefault() != null);

        // The HUD shows a "Game Start" banner on load that fades out ~2s later. Wait for it
        // to clear so a later visibility rise is unambiguously the scroll-speed notification.
        AddUntilStep("initial text hud banner faded", () => Player.HUDOverlay.ChildrenOfType<BmsTextHud>().SingleOrDefault()?.Alpha == 0);

        AddStep("increase scroll speed", () => Playfield.ScrollController.AdjustScrollSpeed(1));
        AddUntilStep("text hud shows the change", () => Player.HUDOverlay.ChildrenOfType<BmsTextHud>().SingleOrDefault()?.Alpha > 0);
    }
}
