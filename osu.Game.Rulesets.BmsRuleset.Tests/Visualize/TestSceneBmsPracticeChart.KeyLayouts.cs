#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeChart
{
    [TestCase(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bms5K2P)]
    [TestCase(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bme7K2P)]
    [TestCase(BmsLayoutVariant.Bms5KDouble, BmsLayoutVariant.Bms5KDouble)]
    [TestCase(BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Bme7KDouble)]
    [TestCase(BmsLayoutVariant.Pms9K, BmsLayoutVariant.Pms9K)]
    [TestCase(BmsLayoutVariant.Pms9KDouble, BmsLayoutVariant.Pms9KDouble)]
    public void MainChartAndMiniatureUseGameplayColumnOrder(BmsLayoutVariant originalLayout, BmsLayoutVariant displayedLayout)
    {
        BmsBeatmap beatmap = null!;
        Colour4[] mainColours = [];
        Colour4[] miniatureColours = [];
        var columns = BmsLayout.GetTotalColumns(originalLayout);
        var expectedOrder = displayedLayout switch
        {
            BmsLayoutVariant.Bms5K2P => new[] { 1, 2, 3, 4, 5, 0 },
            BmsLayoutVariant.Bme7K2P => new[] { 1, 2, 3, 4, 5, 6, 7, 0 },
            _ => Enumerable.Range(0, columns).ToArray(),
        };

        AddStep("show distinct notes and holds in every logical column", () =>
        {
            beatmap = new BmsBeatmap { TotalColumns = columns, LayoutVariant = originalLayout };
            for (var column = 0; column < columns; column++)
            {
                beatmap.HitObjects.Add(new BmsNote { StartTime = 1000 + column * 20, Column = column });
                beatmap.HitObjects.Add(new BmsLongNote { StartTime = 2500 + column * 20, Duration = 500, Column = column });
            }
            beatmap.HitObjects.Add(new BmsNote { StartTime = 5000, Column = columns - 1 });
            settings = new BmsPracticeSettings { StartTime = 0, EndTime = 5001 };
            chart.SetBeatmap(beatmap, settings, true);
            chart.Viewport.Set(0, 5000);
            chart.Zoom(1);
        });
        AddStep("remember the original logical lane colours", () =>
        {
            mainColours = mainLaneBackgrounds().Select(rectangle => rectangle.Colour).ToArray();
            miniatureColours = miniatureLaneBackgrounds().Select(rectangle => rectangle.Colour).ToArray();
            beatmap.LayoutVariant = displayedLayout;
            chart.SetBeatmap(beatmap, settings, true);
            chart.Viewport.Set(0, 5000);
            chart.Zoom(1);
        });
        AddAssert("lane backgrounds preserve logical colours in gameplay order", () =>
        {
            var main = mainLaneBackgrounds();
            var miniature = miniatureLaneBackgrounds();
            return main.Length == columns && miniature.Length == columns
                && Enumerable.Range(0, columns).All(index => main[index].Colour == mainColours[expectedOrder[index]]
                    && miniature[index].Colour == miniatureColours[expectedOrder[index]]);
        });
        AddAssert("all main lanes remain contiguous and fill the timeline", () =>
        {
            var lanes = mainLaneBackgrounds();
            return lanes.Length == columns && Math.Abs(lanes[0].Bounds.Left - chart.TimelineLeft) < 0.01
                && Math.Abs(lanes[^1].Bounds.Right - chart.TimelineRight) < 0.01
                && lanes.Zip(lanes.Skip(1), (left, right) => Math.Abs(left.Bounds.Right - right.Bounds.Left) < 0.01).All(value => value);
        });
        AddAssert("tap and hold heads align to their times in both views", () =>
        {
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            var mainWidth = (chart.TimelineRight - chart.TimelineLeft) / columns;
            var miniatureWidth = (chart.DensityLeft - 8 - chart.OverviewLeft) / columns;
            return Enumerable.Range(0, columns).All(visual =>
            {
                var logical = expectedOrder[visual];
                double[] times = [1000 + logical * 20, 2500 + logical * 20];
                var laneLeft = chart.TimelineLeft + visual * mainWidth;
                var laneRight = laneLeft + mainWidth;
                return times.All(time => rectangles.Any(rectangle => rectangle.Bounds.Left >= laneLeft - 0.01
                        && rectangle.Bounds.Right <= laneRight + 0.01 && rectangle.Bounds.Width > 0
                        && rectangle.Bounds.Height > 0 && rectangle.Bounds.Height <= 12
                        && Math.Abs(rectangle.Bounds.Bottom - positionAt(time)) < 0.01)
                    && rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.Left - (chart.OverviewLeft + visual * miniatureWidth)) < 0.01
                        && Math.Abs(rectangle.Bounds.Width - miniatureWidth) < 0.01 && rectangle.Bounds.Height == 1
                        && Math.Abs(rectangle.Bounds.Bottom - overviewPositionAt(time)) < 0.01));
            });
        });
        AddAssert("hold bodies extend upward in their matching lane in both views", () =>
        {
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            var mainWidth = (chart.TimelineRight - chart.TimelineLeft) / columns;
            var miniatureWidth = (chart.DensityLeft - 8 - chart.OverviewLeft) / columns;
            return Enumerable.Range(0, columns).All(visual =>
            {
                var time = 2500 + expectedOrder[visual] * 20;
                var laneLeft = chart.TimelineLeft + visual * mainWidth;
                return rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.Left - laneLeft) < 0.01
                        && Math.Abs(rectangle.Bounds.Width - mainWidth) < 0.01
                        && Math.Abs(rectangle.Bounds.Top - positionAt(time + 500)) < 0.01
                        && Math.Abs(rectangle.Bounds.Bottom - positionAt(time)) < 0.01)
                    && rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.Left - (chart.OverviewLeft + visual * miniatureWidth)) < 0.01
                        && Math.Abs(rectangle.Bounds.Width - miniatureWidth) < 0.01 && Math.Abs(rectangle.Bounds.Top - overviewPositionAt(time + 500)) < 0.01
                        && Math.Abs(rectangle.Bounds.Bottom - overviewPositionAt(time)) < 0.01);
            });
        });

        BmsReplayVisualizer.PlotRectangle[] mainLaneBackgrounds() => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
            .Where(rectangle => rectangle.Bounds.Left >= chart.TimelineLeft && rectangle.Bounds.Right <= chart.TimelineRight + 0.01
                && rectangle.Bounds.Y == 0 && rectangle.Bounds.Height == chart.DrawHeight)
            .OrderBy(rectangle => rectangle.Bounds.X).ToArray();

        BmsReplayVisualizer.PlotRectangle[] miniatureLaneBackgrounds() => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
            .Where(rectangle => rectangle.Bounds.Left >= chart.OverviewLeft && rectangle.Bounds.Right <= chart.DensityLeft - 8 + 0.01
                && rectangle.Bounds.Top == 18 && Math.Abs(rectangle.Bounds.Height - chart.DrawHeight + 36) < 0.01
                && rectangle.Bounds.Width < 12)
            .OrderBy(rectangle => rectangle.Bounds.X).ToArray();
    }
}
