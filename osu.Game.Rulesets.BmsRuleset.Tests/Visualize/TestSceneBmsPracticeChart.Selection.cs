#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeChart
{
    [TestCase(false, -1e-8)]
    [TestCase(false, 1e-8)]
    [TestCase(true, -1e-8)]
    [TestCase(true, 1e-8)]
    public void CoincidentBoundaryHeadsHighlightStartAndDimEnd(bool holds, double offset)
    {
        AddStep("show heads coinciding with both boundaries", () =>
        {
            var beatmap = new BmsBeatmap { TotalColumns = 8 };
            beatmap.HitObjects = holds
                ? [new BmsLongNote { StartTime = 1000, Column = 1, Duration = 1500 }, new BmsLongNote { StartTime = 2000, Column = 2, Duration = 1000 }]
                : [new BmsNote { StartTime = 1000, Column = 1 }, new BmsNote { StartTime = 2000, Column = 2 }];
            settings = new BmsPracticeSettings { StartTime = 1000 + offset, EndTime = 2000 + offset };
            chart.SetBeatmap(beatmap, settings, true);
        });
        AddAssert("the start head is bright and the end head is dim", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            var start = rectangles.Single(rectangle => Math.Abs(rectangle.Bounds.Left - chart.TimelineLeft - laneWidth) < 0.01
                && Math.Abs(rectangle.Bounds.Bottom - positionAt(1000)) < 0.01 && rectangle.Bounds.Height == 12);
            var end = rectangles.Single(rectangle => Math.Abs(rectangle.Bounds.Left - chart.TimelineLeft - laneWidth * 2) < 0.01
                && Math.Abs(rectangle.Bounds.Bottom - positionAt(2000)) < 0.01 && rectangle.Bounds.Height == 12);
            return start.Colour.A == 1 && end.Colour.A == 0.25f;
        });
        if (holds)
        {
            AddAssert("long-note bodies agree with their head selection", () =>
            {
                var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
                var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
                var start = rectangles.Single(rectangle => Math.Abs(rectangle.Bounds.Left - chart.TimelineLeft - laneWidth) < 0.01
                    && Math.Abs(rectangle.Bounds.Bottom - positionAt(1000)) < 0.01 && rectangle.Bounds.Height > 12);
                var end = rectangles.Single(rectangle => Math.Abs(rectangle.Bounds.Left - chart.TimelineLeft - laneWidth * 2) < 0.01
                    && Math.Abs(rectangle.Bounds.Bottom - positionAt(2000)) < 0.01 && rectangle.Bounds.Height > 12);
                return start.Colour.A == 0.8f && end.Colour.A == 0.4f;
            });
        }
    }
}
