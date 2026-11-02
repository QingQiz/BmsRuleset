#nullable enable

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    [TestCase(30, 1280, 720, false)]
    [TestCase(60, 1280, 720, false)]
    [TestCase(60, 1024, 600, true)]
    [TestCase(60, 760, 900, false)]
    public void HourLongRealPatternsKeepOverviewNavigationAndRangeSelectionUsable(int minutes, int width, int height, bool density)
    {
        BmsPracticeSetupOverlay setup = null!;
        BmsPracticeChart chart = null!;
        BmsBeatmap beatmap = null!;
        BmsPracticeSession? started = null;
        Container viewport = null!;
        var full = 0d;
        var target = 0d;
        var selectedStart = 0d;
        var selectedEnd = 0d;
        var frameStart = 0d;
        var frameEnd = 0d;
        Vector2 overviewPoint(double time) => crossViewportOverviewPosition(chart, time, density);

        AddStep("extend real note patterns and timing to a super-long chart", () =>
        {
            var path = Path.Combine(longRangePracticeSongsRoot(), "Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml");
            var original = (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path), randomValueSelector: _ => 1), new BmsRuleset())
            {
                BranchRandomValueSelector = _ => 1,
            }.Convert();
            beatmap = BmsPracticeExtendedChartFixture.RepeatToMinutes(original, minutes);
            BmsTestBeatmaps.SetupBeatmapInfo(beatmap, new BmsRuleset().RulesetInfo);
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = viewport = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [], BmsPracticeSettings.CreateDefault(beatmap))
                {
                    StartRequested = session => started = session,
                },
            };
            setup.Show();
        });
        AddUntilStep("super-long settings and chart are ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("inspect the full chart and choose a close view", () =>
        {
            chart = setup.SelectionChart;
            full = chart.Viewport.Maximum - chart.Viewport.Minimum;
            Assert.That(full, Is.GreaterThanOrEqualTo(minutes * 60000));
            Assert.That(beatmap.HitObjects.Count, Is.GreaterThan(minutes * 500));
            chart.Viewport.Set(30000, 6000);
            chart.Zoom(1);
        });
        AddAssert("column count stays bounded and main lanes remain readable", () => chart.OverviewColumns is >= 2 and <= 8
            && (chart.TimelineRight - chart.TimelineLeft) / beatmap.TotalColumns >= 16);
        AddAssert("overview rectangles stay finite and inside the chart", () => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
            .Where(rectangle => rectangle.Bounds.Left >= chart.OverviewLeft).All(rectangle => float.IsFinite(rectangle.Bounds.X)
                && float.IsFinite(rectangle.Bounds.Y) && float.IsFinite(rectangle.Bounds.Height)
                && rectangle.Bounds.Left >= chart.OverviewLeft && rectangle.Bounds.Right <= chart.DrawWidth + 0.01
                && rectangle.Bounds.Top >= 18 && rectangle.Bounds.Bottom <= chart.DrawHeight - 18 + 0.01));
        AddStep("locate a pattern eighty percent into the song", () =>
        {
            target = full * 0.8;
            InputManager.MoveMouseTo(overviewPoint(target));
            InputManager.Click(MouseButton.Left);
        });
        AddStep("distant navigation centres accurately at the same zoom", () =>
        {
            Assert.That(chart.Viewport.Start + 3000, Is.EqualTo(target).Within(1));
            Assert.That(chart.Viewport.Duration, Is.EqualTo(6000));
        });
        AddStep("place a start far into the song", () =>
        {
            selectedStart = chart.SnapTime(target);
            chart.SelectBoundary(true);
            InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, selectedStart));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("distant start uses the musical grid", () => Math.Abs(setup.Settings.StartTime - selectedStart) < 0.001);
        AddStep("browse to a still later end", () =>
        {
            target = full * 0.95;
            InputManager.MoveMouseTo(overviewPoint(target));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("browsing keeps the chosen start and zoom", () => Math.Abs(setup.Settings.StartTime - selectedStart) < 0.001 && chart.Viewport.Duration == 6000);
        AddStep("place the independent end", () =>
        {
            selectedEnd = chart.SnapTime(target);
            chart.SelectBoundary(false);
            InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, selectedEnd));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("selection spans minutes without squeezing the close view", () => Math.Abs(setup.Settings.EndTime - selectedEnd) < 0.001
            && setup.Settings.EndTime - setup.Settings.StartTime > 240000 && chart.Viewport.Duration == 6000);
        AddStep("grab a very thin view frame at its upper edge", () =>
        {
            frameStart = chart.Viewport.Start;
            frameEnd = chart.Viewport.End;
            InputManager.MoveMouseTo(overviewPoint(frameEnd));
            InputManager.PressButton(MouseButton.Left);
        });
        AddAssert("thin frame does not jump on press", () => Math.Abs(chart.Viewport.Start - frameStart) < 1 && chart.Viewport.Duration == 6000);
        AddStep("drag the thin frame earlier", () => InputManager.MoveMouseTo(overviewPoint(frameEnd - 30000)));
        AddStep("release thin frame", () => InputManager.ReleaseButton(MouseButton.Left));
        AddStep("thin frame moves without resizing or editing selection", () =>
        {
            Assert.That(chart.Viewport.Start, Is.EqualTo(frameStart - 30000).Within(1));
            Assert.That(chart.Viewport.Duration, Is.EqualTo(6000));
            Assert.That(setup.Settings.StartTime, Is.EqualTo(selectedStart).Within(0.001));
            Assert.That(setup.Settings.EndTime, Is.EqualTo(selectedEnd).Within(0.001));
        });
        AddStep("locate the final seconds", () =>
        {
            InputManager.MoveMouseTo(overviewPoint(chart.Viewport.Maximum));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("song-end navigation clamps while preserving duration", () => Math.Abs(chart.Viewport.End - chart.Viewport.Maximum) < 0.001 && chart.Viewport.Duration == 6000);
        AddStep("resize the long-chart settings", () => viewport.Size = width == 760 ? new Vector2(1280, 720) : new Vector2(760, 900));
        AddUntilStep("resized chart and actions fit", () => chart.DrawHeight > 100 && viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat));
        AddAssert("resizing retains endpoints and detail", () => Math.Abs(setup.Settings.StartTime - selectedStart) < 0.001
            && Math.Abs(setup.Settings.EndTime - selectedEnd) < 0.001 && chart.Viewport.Duration == 6000);
        AddStep("start the far-away practice range", () => clickRealChartControl(setup.StartButton));
        AddAssert("session uses the same distant endpoints", () => started != null
            && Math.Abs(started.Settings.StartTime - selectedStart) < 0.001 && Math.Abs(started.Settings.EndTime - selectedEnd) < 0.001);
    }
}
