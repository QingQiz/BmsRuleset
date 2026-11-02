#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    private Action? restoreAlignmentLocale;

    [TearDown]
    public void RestoreAlignmentLocale()
    {
        var restore = restoreAlignmentLocale;
        restoreAlignmentLocale = null;
        restore?.Invoke();
    }

    [TestCase(1280, 720, "en")]
    [TestCase(1280, 720, "zh")]
    [TestCase(1024, 900, "en")]
    [TestCase(1024, 900, "zh")]
    [TestCase(1024, 600, "en")]
    [TestCase(1024, 600, "zh")]
    [TestCase(760, 900, "en")]
    [TestCase(760, 900, "zh")]
    public void ControlGroupsShareTheirColumnEdges(int width, int height, string locale)
    {
        BmsPracticeSetupOverlay setup = null!;
        RoundedButton full = null!;
        RoundedButton fit = null!;
        OsuDropdown<int> snap = null!;
        Drawable zoomRow = null!;
        Container startRow = null!;
        Container endRow = null!;
        FillFlowContainer timeRows = null!;

        AddStep("open aligned practice setup", () =>
        {
            var config = Dependencies.Get<FrameworkConfigManager>();
            var originalLocale = config.Get<string>(FrameworkSetting.Locale);
            // NUnit cleanup still runs when a failed visual assertion skips the remaining steps.
            restoreAlignmentLocale = () => config.SetValue(FrameworkSetting.Locale, originalLocale);
            config.SetValue(FrameworkSetting.Locale, locale);
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            Child = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [],
                    BmsPracticeSettings.CreateDefault(beatmap) with { SkipResults = true }),
            };
            setup.Show();
        });
        AddUntilStep("setup layout settled", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("find controls", () =>
        {
            var buttons = setup.ChildrenOfType<RoundedButton>().ToArray();
            full = buttons.Single(button => button.Text.Equals(BmsStrings.PracticeFullChart));
            fit = buttons.Single(button => button.Text.Equals(BmsStrings.PracticeFitRange));
            snap = setup.ChildrenOfType<OsuDropdown<int>>().Single(dropdown => dropdown.Items.Contains(64));
            zoomRow = setup.ChildrenOfType<RoundedSliderBar<double>>().Single().Parent!;
            startRow = (Container)setup.StartTimeInput.Parent!;
            endRow = (Container)setup.EndTimeInput.Parent!;
            timeRows = (FillFlowContainer)startRow.Parent!;
        });
        AddAssert("view rows have equal total width", () =>
            aligned(zoomRow.DrawWidth, full.Parent!.DrawWidth)
            && aligned(zoomRow.DrawWidth, snap.Parent!.DrawWidth)
            && aligned(full.Parent.ScreenSpaceDrawQuad.AABBFloat.Left, full.ScreenSpaceDrawQuad.AABBFloat.Left)
            && aligned(full.Parent.ScreenSpaceDrawQuad.AABBFloat.Right, fit.ScreenSpaceDrawQuad.AABBFloat.Right)
            && aligned(snap.Parent.ScreenSpaceDrawQuad.AABBFloat.Right, snap.ScreenSpaceDrawQuad.AABBFloat.Right));
        AddAssert("fit buttons share the row evenly", () => aligned(full.DrawWidth, fit.DrawWidth));
        AddAssert("view groups meet their column edges", () =>
        {
            var viewGroup = zoomRow.Parent!.ScreenSpaceDrawQuad.AABBFloat;
            var fitGroup = full.Parent!.ScreenSpaceDrawQuad.AABBFloat;
            var snapGroup = snap.Parent!.ScreenSpaceDrawQuad.AABBFloat;
            var toolbar = full.Parent!.Parent!.ScreenSpaceDrawQuad.AABBFloat;
            return width < 900
                ? aligned(viewGroup.Left, toolbar.Left) && aligned(snapGroup.Right, toolbar.Right)
                    && viewGroup.Right < fitGroup.Left && fitGroup.Right < snapGroup.Left
                : aligned(viewGroup.Left, toolbar.Left) && aligned(snapGroup.Left, toolbar.Left)
                    && aligned(fitGroup.Right, toolbar.Right);
        });
        AddAssert("time inputs share a width and the settings right edge", () =>
            aligned(setup.StartTimeInput.DrawWidth, setup.EndTimeInput.DrawWidth)
            && aligned(setup.EndTimeInput.ScreenSpaceDrawQuad.AABBFloat.Right, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Right));
        AddAssert("time row groups share the settings outer edges", () =>
            aligned(startRow.DrawWidth, endRow.DrawWidth)
            && aligned(startRow.ScreenSpaceDrawQuad.AABBFloat.Left, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Left)
            && aligned(endRow.ScreenSpaceDrawQuad.AABBFloat.Right, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Right)
            && aligned(setup.StartTimeInput.ScreenSpaceDrawQuad.AABBFloat.Right, startRow.ScreenSpaceDrawQuad.AABBFloat.Right)
            && aligned(setup.EndTimeInput.ScreenSpaceDrawQuad.AABBFloat.Right, endRow.ScreenSpaceDrawQuad.AABBFloat.Right));
        AddAssert("time row groups keep their intended gap", () => width == 760
            ? aligned(startRow.Y, endRow.Y) && aligned(endRow.X - startRow.X - startRow.DrawWidth, timeRows.Spacing.X)
            : aligned(startRow.X, endRow.X) && aligned(endRow.Y - startRow.Y - startRow.DrawHeight, timeRows.Spacing.Y));
        AddAssert("endpoint controls and time inputs have breathing room", () =>
        {
            (Drawable Select, Drawable Locate, Drawable Input)[] rows =
            [
                (setup.StartBoundaryButton, setup.LocateStartButton, setup.StartTimeInput),
                (setup.EndBoundaryButton, setup.LocateEndButton, setup.EndTimeInput),
            ];
            return rows.All(row => row.Select.ScreenSpaceDrawQuad.AABBFloat.Right < row.Locate.ScreenSpaceDrawQuad.AABBFloat.Left
                && row.Select.ScreenSpaceDrawQuad.AABBFloat.Bottom < row.Input.ScreenSpaceDrawQuad.AABBFloat.Top
                && row.Locate.ScreenSpaceDrawQuad.AABBFloat.Bottom < row.Input.ScreenSpaceDrawQuad.AABBFloat.Top
                && aligned(row.Select.ScreenSpaceDrawQuad.AABBFloat.Left, row.Input.ScreenSpaceDrawQuad.AABBFloat.Left)
                && aligned(row.Locate.ScreenSpaceDrawQuad.AABBFloat.Right, row.Input.ScreenSpaceDrawQuad.AABBFloat.Right));
        });
        AddAssert("count-in choices reach the settings right edge", () =>
            aligned(setup.CountInButtons[^1].ScreenSpaceDrawQuad.AABBFloat.Right, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Right));
        AddAssert("start action shares both edges with the time inputs", () =>
            aligned(setup.StartTimeInput.ScreenSpaceDrawQuad.AABBFloat.Left, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Left)
            && aligned(setup.EndTimeInput.ScreenSpaceDrawQuad.AABBFloat.Right, setup.StartButton.ScreenSpaceDrawQuad.AABBFloat.Right));
        AddAssert("start is the only fixed setup action", () => ((Container)setup.StartButton.Parent!).Children.OfType<RoundedButton>().Count(), () => Is.EqualTo(1));
        AddStep("restore locale", RestoreAlignmentLocale);
    }

    private static bool aligned(float first, float second) => Math.Abs(first - second) < 0.1f;
}
