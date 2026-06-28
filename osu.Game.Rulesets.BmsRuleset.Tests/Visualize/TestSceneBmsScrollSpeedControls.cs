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
    private long firstTick;
    private long secondTick;

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

        AddStep("capture target ticks", () =>
        {
            var firstTwo = ((BmsBeatmap)Player.GameplayState.Beatmap).HitObjects
                .OrderBy(h => h.StartTime)
                .Take(2)
                .ToArray();

            firstTick = firstTwo[0].TickInfo.Tick;
            secondTick = firstTwo[1].TickInfo.Tick;
        });

        AddStep("seek before notes", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("two notes alive", () => Playfield.GetAliveObjectAtTick(firstTick) != null && Playfield.GetAliveObjectAtTick(secondTick) != null);
        AddUntilStep("spacing measurable", () => Playfield.SpacingBetweenTicks(firstTick, secondTick), () => Is.GreaterThan(1));
        AddStep("capture spacing", () => normalSpacing = Playfield.SpacingBetweenTicks(firstTick, secondTick));

        AddStep("press up", () => Playfield.ScrollController.AdjustScrollSpeed(1));
        AddUntilStep("scroll speed increased", () => Playfield.ScrollSpeed, () => Is.EqualTo(10).Within(0.001));
        AddUntilStep("spacing visibly increased", () => Playfield.SpacingBetweenTicks(firstTick, secondTick), () => Is.GreaterThan(normalSpacing * 1.1f));

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
