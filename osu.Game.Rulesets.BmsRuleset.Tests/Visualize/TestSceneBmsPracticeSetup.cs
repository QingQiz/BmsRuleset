#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPracticeSetup : OsuManualInputManagerTestScene
{
    [Test]
    public void ResizingPreservesSelectionAndKeepsControlsAccessible()
    {
        BmsPracticeSetupOverlay setup = null!;
        Container viewport = null!;
        BmsPracticeSettings selection = null!;

        AddStep("open in a wide viewport", () =>
        {
            var source = new TestWorkingBeatmap(BmsTestBeatmaps.CreateBeatmap(), audioManager: Audio);
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = viewport = new Container
            {
                Size = new Vector2(1280, 720),
                Child = setup = new BmsPracticeSetupOverlay(source, []),
            };
            setup.Show();
        });
        AddUntilStep("practice settings ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 500);
        AddStep("remember selection", () => selection = setup.Settings);
        AddAssert("wide layout gives the chart most of the height", () => setup.SelectionChart.DrawHeight > viewport.DrawHeight * 0.7f);
        AddAssert("view controls are left of chart", () => setup.ChildrenOfType<RoundedSliderBar<double>>().Single().ScreenSpaceDrawQuad.AABBFloat.Right
            < setup.SelectionChart.ScreenSpaceDrawQuad.AABBFloat.Left);
        AddAssert("time inputs and start are right of chart", () => setup.StartTimeInput.ScreenSpaceDrawQuad.AABBFloat.Left
            > setup.SelectionChart.ScreenSpaceDrawQuad.AABBFloat.Right
            && setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Left > setup.SelectionChart.ScreenSpaceDrawQuad.AABBFloat.Right);
        AddStep("reduce viewport width", () => viewport.Width = 1024);
        AddUntilStep("view controls move beside chart", () => setup.ChildrenOfType<RoundedSliderBar<double>>().Single().ScreenSpaceDrawQuad.AABBFloat.Left
            > setup.SelectionChart.ScreenSpaceDrawQuad.AABBFloat.Right);
        AddAssert("chart retains its height", () => setup.SelectionChart.DrawHeight > viewport.DrawHeight * 0.7f);
        AddAssert("scrolling settings are clipped above action buttons", () => setup.ChildrenOfType<OsuScrollContainer>()
            .Single(scroll => scroll.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput)).ScreenSpaceDrawQuad.AABBFloat.Bottom
            < setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Top);
        AddStep("use a narrow viewport", () => viewport.Size = new Vector2(760, 900));
        AddUntilStep("inputs move below chart", () => setup.StartTimeInput.ScreenSpaceDrawQuad.AABBFloat.Top
            > setup.SelectionChart.ScreenSpaceDrawQuad.AABBFloat.Bottom);
        AddAssert("start button stays inside setup", () => setup.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat));
        AddAssert("selection and repeat settings survive resizing", () => setup.Settings == selection);
    }

    [Test]
    public void RangeSelectionStartsPracticeWithOneClick()
    {
        BmsPracticeSetupOverlay setup = null!;
        BmsPracticeSession? started = null;
        var starts = 0;

        AddStep("open practice setup", () =>
        {
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Child = setup = new BmsPracticeSetupOverlay(source, [])
            {
                StartRequested = session => { started = session; starts++; },
            };
            setup.Show();
        });
        AddUntilStep("practice settings ready", () => setup.IsReady);
        AddAssert("unmodified setup does not select internal branch mod", () => setup.ModSelection.SelectedMods.Value, () => Is.Empty);
        AddAssert("start button shown on range selection", () => setup.StartButton.Text.ToString(), () => Is.EqualTo(BmsStrings.PracticeBegin.ToString()));
        AddAssert("count-in is initially folded away", () => !setup.CountInGroup.IsPresent && setup.CountInGroup.DrawHeight == 0);
        AddStep("enable automatic next attempts", () => setup.SkipResultsInput.Current.Value = true);
        AddUntilStep("count-in unfolds", () => setup.CountInGroup.IsPresent && setup.CountInGroup.DrawHeight > 60 && !setup.CountInGroup.Transforms.Any());
        AddStep("scroll the count-in controls into view", () => setup.ChildrenOfType<OsuScrollContainer>()
            .Single(scroll => scroll.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput)).ScrollIntoView(setup.CountInButtons[0], false));
        AddAssert("count-in is inside the interactive settings area", () => setup.ChildrenOfType<OsuScrollContainer>()
            .Single(scroll => scroll.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput)).ScreenSpaceDrawQuad.AABBFloat
            .Contains(setup.CountInButtons[0].ScreenSpaceDrawQuad.AABBFloat));
        AddStep("configure repeat and count-in", () =>
        {
            InputManager.MoveMouseTo(setup.CountInButtons[0].ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("click start once", () =>
        {
            InputManager.MoveMouseTo(setup.StartButton.ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("one session created immediately", () => starts == 1 && started != null);
        AddAssert("session uses settings from same page", () => started!.Settings.SkipResults && started.Settings.CountIn == 0);
        AddAssert("unmodified session has no gameplay mods", () => started!.Mods, () => Is.Empty);
        AddAssert("setup closes after start", () => setup.State.Value, () => Is.EqualTo(Visibility.Hidden));
    }
}
