#nullable enable

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    [TestCase(1280, 720, false)]
    [TestCase(1280, 720, true)]
    [TestCase(760, 900, false)]
    [TestCase(760, 900, true)]
    public void CountInFoldsWithAutomaticRepeatAndRetainsItsValue(int width, int height, bool initiallyEnabled)
    {
        BmsPracticeSetupOverlay setup = null!;
        OsuScrollContainer scroll = null!;
        var expandedHeight = 0f;
        var expandingHeight = 0f;
        var collapsingHeight = 0f;
        var expandedContent = 0f;

        AddStep("open with a remembered countdown preference", () =>
        {
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [],
                    BmsPracticeSettings.CreateDefault(beatmap) with { SkipResults = initiallyEnabled, CountIn = 4 }),
            };
            setup.Show();
        });
        AddUntilStep("initial visibility follows the saved preference", () => setup.IsReady
            && setup.CountInGroup.IsPresent == initiallyEnabled
            && (initiallyEnabled ? setup.CountInGroup.DrawHeight > 60 : setup.CountInGroup.DrawHeight == 0));
        AddStep("fold the group and find settings flow", () =>
        {
            setup.SkipResultsInput.Current.Value = false;
            scroll = setup.ChildrenOfType<OsuScrollContainer>().Single(container => container.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput));
        });
        AddUntilStep("folded controls occupy no height", () => !setup.CountInGroup.IsPresent && setup.CountInGroup.DrawHeight == 0);
        AddAssert("folded choices are disabled and keep their value", () => setup.CountInButtons.All(choice => !choice.Enabled.Value) && setup.Settings.CountIn == 4);
        AddStep("enable automatic repeats and observe expansion", () =>
        {
            setup.SkipResultsInput.Current.Value = true;
            Scheduler.AddDelayed(() => expandingHeight = setup.CountInGroup.DrawHeight, 80);
        });
        AddUntilStep("expanded layout settles", () => setup.CountInGroup.DrawHeight > 60 && !setup.CountInGroup.Transforms.Any());
        AddStep("verify animated expansion and record its space", () =>
        {
            expandedHeight = setup.CountInGroup.DrawHeight;
            expandedContent = scroll.AvailableContent;
            Assert.That(expandingHeight, Is.GreaterThan(0).And.LessThan(expandedHeight));
            Assert.That(setup.Settings.CountIn, Is.EqualTo(4));
            Assert.That(setup.CountInButtons.All(choice => choice.Enabled.Value), Is.True);
            Assert.That(aligned(setup.CountInButtons[^1].ScreenSpaceDrawQuad.AABBFloat.Right, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Right), Is.True);
        });
        AddStep("select a new countdown and fold it again", () =>
        {
            setup.CountInButtons[5].TriggerClick();
            setup.SkipResultsInput.Current.Value = false;
            Scheduler.AddDelayed(() => collapsingHeight = setup.CountInGroup.DrawHeight, 80);
        });
        AddUntilStep("collapse finishes and releases layout space", () => !setup.CountInGroup.IsPresent && setup.CountInGroup.DrawHeight == 0
            && scroll.AvailableContent < expandedContent - 20);
        AddStep("verify animated collapse preserves the new choice", () =>
        {
            Assert.That(collapsingHeight, Is.GreaterThan(0).And.LessThan(expandedHeight));
            Assert.That(setup.Settings.CountIn, Is.EqualTo(5));
        });
        AddStep("reverse the animation twice before it finishes", () =>
        {
            setup.SkipResultsInput.Current.Value = true;
            Scheduler.AddDelayed(() => setup.SkipResultsInput.Current.Value = false, 80);
            Scheduler.AddDelayed(() => setup.SkipResultsInput.Current.Value = true, 160);
        });
        AddUntilStep("latest toggle wins without a stale collapse", () => setup.CountInGroup.IsPresent && aligned(setup.CountInGroup.DrawHeight, expandedHeight)
            && !setup.CountInGroup.Transforms.Any());
        AddAssert("saved countdown and range remain intact", () => setup.Settings.SkipResults && setup.Settings.CountIn == 5 && setup.StartButton.Enabled.Value);
    }

}
