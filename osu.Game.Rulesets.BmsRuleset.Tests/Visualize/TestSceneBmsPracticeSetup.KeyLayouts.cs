#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    private static IEnumerable<TestCaseData> practiceKeyLayouts()
    {
        (BmsLayoutVariant Layout, int Columns)[] layouts =
        [
            (BmsLayoutVariant.Bms5K, 6),
            (BmsLayoutVariant.Bme7K, 8),
            (BmsLayoutVariant.Pms9K, 9),
            (BmsLayoutVariant.Bms5KDouble, 12),
            (BmsLayoutVariant.Bme7KDouble, 16),
            (BmsLayoutVariant.Pms9KDouble, 18),
        ];
        (int Width, int Height)[] sizes = [(1280, 720), (1024, 900), (760, 900)];
        foreach (var (layout, columns) in layouts)
        {
            foreach (var (width, height) in sizes)
                yield return new TestCaseData(layout, columns, width, height);
        }
        yield return new TestCaseData(BmsLayoutVariant.Bms5K2P, 6, 1280, 720);
        yield return new TestCaseData(BmsLayoutVariant.Bme7K2P, 8, 1280, 720);
    }

    [TestCaseSource(nameof(practiceKeyLayouts))]
    public void KeyLayoutsKeepAllLanesAndSettingsUsable(BmsLayoutVariant layout, int columns, int width, int height)
    {
        BmsPracticeSetupOverlay setup = null!;
        BmsPracticeChart selectionChart = null!;
        BmsBeatmap converted = null!;
        Container viewport = null!;
        OsuScrollContainer settingsScroll = null!;

        AddStep("open decoded key-layout fixture", () =>
        {
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            var source = new TestWorkingBeatmap(BmsPracticeLayoutFixture.Create(layout), audioManager: Audio);
            Child = viewport = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(source, BmsPracticeLayoutFixture.ModsFor(layout), new BmsPracticeSettings
                {
                    StartTime = 4000,
                    EndTime = 10000,
                    SkipResults = true,
                    CountIn = 3,
                }),
            };
            setup.Show();
            converted = BmsPracticeSession.GetFullBeatmap(source, setup.SelectedMods);
        });
        AddUntilStep("key-layout setup is ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("show the pattern shared by every column", () =>
        {
            selectionChart = setup.SelectionChart;
            selectionChart.Viewport.Set(4000, 6000);
            selectionChart.Zoom(1);
            settingsScroll = setup.ChildrenOfType<OsuScrollContainer>()
                .Single(scroll => scroll.ChildrenOfType<OsuTextBox>().Contains(setup.StartTimeInput));
        });
        AddAssert("fixture survives practice conversion with its intended layout", () => converted.LayoutVariant == layout
            && converted.TotalColumns == columns && converted.HitObjects.Select(note => note.Column).Distinct().Count() == columns);
        AddAssert("main lanes are continuous and individually readable", () =>
        {
            var lanes = keyLayoutRectangles(selectionChart)
                .Where(bounds => Math.Abs(bounds.Top) < 0.1f && Math.Abs(bounds.Height - selectionChart.DrawHeight) < 0.1f
                    && bounds.Left >= selectionChart.TimelineLeft && bounds.Right <= selectionChart.TimelineRight + 0.1f)
                .OrderBy(bounds => bounds.Left).ToArray();
            return lanes.Length == columns && lanes.All(lane => lane.Width >= 16)
                   && Math.Abs(lanes[0].Left - selectionChart.TimelineLeft) < 0.1f
                   && Math.Abs(lanes[^1].Right - selectionChart.TimelineRight) < 0.1f
                   && lanes.Skip(1).Select((lane, index) => Math.Abs(lane.Left - lanes[index].Right) < 0.1f
                       && Math.Abs(lane.Width - lanes[index].Width) < 0.1f).All(value => value);
        });
        AddAssert("both note heads and long-note bodies appear in every main lane", () =>
        {
            var laneWidth = (selectionChart.TimelineRight - selectionChart.TimelineLeft) / columns;
            var rectangles = keyLayoutRectangles(selectionChart);
            return Enumerable.Range(0, columns).All(column =>
            {
                var x = selectionChart.TimelineLeft + column * laneWidth;
                var notes = rectangles.Where(bounds => bounds.Left >= x - 0.1f && bounds.Right <= x + laneWidth + 0.1f);
                return notes.Any(bounds => bounds.Height > 0 && bounds.Height <= 12 && Math.Abs(bounds.Width - laneWidth) < 0.1f)
                       && notes.Any(bounds => bounds.Height > 20 && bounds.Height < selectionChart.DrawHeight - 36
                           && Math.Abs(bounds.Left - x) < 0.1f && Math.Abs(bounds.Width - laneWidth) < 0.1f);
            });
        });
        AddAssert("thumbnail retains a separate column for every lane", () =>
        {
            var lanes = keyLayoutRectangles(selectionChart).Where(bounds => Math.Abs(bounds.Top - 18) < 0.1f
                && Math.Abs(bounds.Height - selectionChart.DrawHeight + 36) < 0.1f
                && bounds.Left >= selectionChart.OverviewLeft && bounds.Right < selectionChart.DensityLeft).ToArray();
            return lanes.Length == columns * selectionChart.OverviewColumns && lanes.All(lane => lane.Width > 2);
        });
        AddAssert("density remains visible beside the thumbnail", () => selectionChart.TimelineRight < selectionChart.OverviewLeft
            && selectionChart.OverviewLeft < selectionChart.DensityLeft
            && keyLayoutRectangles(selectionChart).Any(bounds => Math.Abs(bounds.Left - selectionChart.DensityLeft - 2) < 0.1f
                && bounds.Width > 0 && bounds.Right < selectionChart.DrawWidth));
        AddAssert("chart and fixed actions fit without overlapping", () =>
        {
            var chartBounds = selectionChart.ScreenSpaceDrawQuad.AABBFloat;
            var actionBounds = setup.StartButton.ScreenSpaceDrawQuad.AABBFloat;
            return viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(chartBounds)
                   && viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(actionBounds)
                   && (chartBounds.Right <= actionBounds.Left || chartBounds.Bottom <= actionBounds.Top);
        });
        AddAssert("side-by-side charts use the height released by the header", () => width < 1000
            || Math.Abs(selectionChart.Parent!.Parent!.ToLocalSpace(selectionChart.ScreenSpaceDrawQuad.TopLeft).Y - 24) < 1);
        AddAssert("chart title fits without covering the chart", () =>
        {
            var title = setup.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.Equals(BmsStrings.PracticeSelectRange));
            var bounds = title.ScreenSpaceDrawQuad.AABBFloat;
            var chartBounds = selectionChart.ScreenSpaceDrawQuad.AABBFloat;
            return viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(bounds)
                   && (bounds.Right <= chartBounds.Left || bounds.Left >= chartBounds.Right || bounds.Bottom <= chartBounds.Top);
        });
        AddStep("reveal repeat settings", () => settingsScroll.ScrollIntoView(setup.CountInButtons[0], false));
        AddAssert("count-in remains inside the interactive settings viewport", () => settingsScroll.ScreenSpaceDrawQuad.AABBFloat
            .Contains(setup.CountInButtons[0].ScreenSpaceDrawQuad.AABBFloat));
        AddStep("set count-in through the visible control", () =>
        {
            InputManager.MoveMouseTo(setup.CountInButtons[0].ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("key-layout settings remain interactive", () => setup.Settings.CountIn, () => Is.Zero);
    }

    private static RectangleF[] keyLayoutRectangles(BmsPracticeChart chart)
        => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Select(rectangle => rectangle.Bounds).ToArray();
}
