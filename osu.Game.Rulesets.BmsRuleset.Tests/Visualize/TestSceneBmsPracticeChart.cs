#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPracticeChart : OsuManualInputManagerTestScene
{
    private BmsPracticeChart chart = null!;
    private BmsPracticeSettings settings = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create static chart", () =>
        {
            var beatmap = new BmsBeatmap
            {
                TotalColumns = 8,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TimingMap = BmsChartParser.Parse(["#BPM 120", "#03011:01"]).TimingMap,
            };
            beatmap.HitObjects = Enumerable.Range(1, 60).Select(i => (BmsHitObject)new BmsNote { StartTime = i * 1000, Column = i % 8, Beatmap = beatmap }).ToList();
            beatmap.HitObjects.Add(new BmsLongNote { StartTime = 18000, Duration = 10000, Column = 4, Beatmap = beatmap });
            settings = new BmsPracticeSettings { StartTime = 10000, EndTime = 20000 };
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(700, 500),
                Child = chart = new BmsPracticeChart
                {
                    RangeChanged = (start, end) => settings = settings with { StartTime = start, EndTime = end },
                },
            };
            chart.SetBeatmap(beatmap, settings, true);
        });
        AddUntilStep("chart loaded", () => chart.IsLoaded && chart.DrawHeight > 0);
    }

    [Test]
    public void ViewportNavigationKeepsSelection()
    {
        var duration = 0d;
        var pointerTime = 0d;
        AddStep("zoom around pointer", () =>
        {
            duration = chart.Viewport.Duration;
            pointerTime = chart.Viewport.End - (200 - 18) / (chart.DrawHeight - 36) * chart.Viewport.Duration;
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(300, 200)));
            InputManager.PressKey(Key.ControlLeft);
            InputManager.ScrollVerticalBy(2);
            InputManager.ReleaseKey(Key.ControlLeft);
        });
        AddAssert("view zoomed", () => chart.Viewport.Duration < duration);
        AddAssert("pointer retains its original time", () => Math.Abs(positionAt(pointerTime) - 200) < 0.01);
        AddStep("scroll view", () => InputManager.ScrollVerticalBy(-2));
        AddAssert("selected times untouched", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [Test]
    public void LongNoteBodyExtendsUpwardFromItsHead()
    {
        AddAssert("start appears below end", () =>
        {
            var start = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeStartLine.ToString());
            var end = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeEndLine.ToString());
            return start.Y > end.Y;
        });
        AddAssert("long-note body matches its lane and ends squarely", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var x = chart.TimelineLeft + 4 * laneWidth;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            return rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.X - x) < 0.01
                && Math.Abs(rectangle.Bounds.Width - laneWidth) < 0.01
                && Math.Abs(rectangle.Bounds.Top - positionAt(28000)) < 0.1
                && Math.Abs(rectangle.Bounds.Bottom - positionAt(18000)) < 0.1);
        });
        AddStep("zoom view into chart", () => { chart.Viewport.Set(10000, 20000); chart.Zoom(1); });
        AddStep("scroll upward", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(300, 250)));
            InputManager.ScrollVerticalBy(1);
        });
        AddAssert("upward scrolling reveals later time", () => chart.Viewport.Start > 10000);
    }

    [Test]
    public void OverviewMovesWithoutResizingUsingUpwardTime()
    {
        var endTime = 0d;
        AddStep("zoom view into chart", () => { chart.Viewport.Set(10000, 20000); chart.Zoom(1); });
        AddStep("press overview window centre", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(20000))));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("move overview upward", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(25000)))));
        AddStep("release overview", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("window moved to later time", () => Math.Abs(chart.Viewport.Start - 15000) < 1 && Math.Abs(chart.Viewport.Duration - 20000) < 1);
        AddStep("press upper overview edge", () =>
        {
            endTime = chart.Viewport.End;
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(endTime))));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("move upper edge toward later time", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(endTime + 5000)))));
        AddStep("release upper edge", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("upper edge moves the whole view at the same zoom", () => Math.Abs(chart.Viewport.Start - 20000) < 1
            && Math.Abs(chart.Viewport.End - endTime - 5000) < 1 && Math.Abs(chart.Viewport.Duration - 20000) < 1);
        AddAssert("overview leaves selected times unchanged", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [Test]
    public void DensityOverviewShowsRealTimeBurstsAndNavigatesWithoutChangingSelection()
    {
        osu.Framework.Graphics.Primitives.RectangleF[] histogram = [];
        AddStep("show two different bursts and a silent second", () =>
        {
            var beatmap = new BmsBeatmap { TotalColumns = 8, LayoutVariant = BmsLayoutVariant.Bme7K };
            beatmap.HitObjects = Enumerable.Range(1, 8).Select(i => (BmsHitObject)new BmsNote { StartTime = i * 100, Column = i % 8 })
                .Concat(Enumerable.Range(1, 4).Select(i => (BmsHitObject)new BmsNote { StartTime = 1000 + i * 100, Column = i }))
                .Append(new BmsNote { StartTime = 3999, Column = 1 }).ToList();
            settings = new BmsPracticeSettings { StartTime = 0, EndTime = 4000 };
            chart.SetBeatmap(beatmap, settings, true);
        });
        AddAssert("denser first second has twice the bar width", () =>
        {
            var bars = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Where(rectangle => rectangle.Bounds.X == chart.DensityLeft + 2).ToArray();
            var first = bars.Single(rectangle => rectangle.Bounds.Top <= overviewPositionAt(500) && rectangle.Bounds.Bottom > overviewPositionAt(500));
            var second = bars.Single(rectangle => rectangle.Bounds.Top <= overviewPositionAt(1500) && rectangle.Bounds.Bottom > overviewPositionAt(1500));
            return Math.Abs(first.Bounds.Width - 36) < 0.01 && Math.Abs(first.Bounds.Width - second.Bounds.Width * 2) < 0.01;
        });
        AddAssert("silent second has no density bar", () => !chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Any(rectangle =>
            rectangle.Bounds.X == chart.DensityLeft + 2 && rectangle.Bounds.Top <= overviewPositionAt(2500) && rectangle.Bounds.Bottom > overviewPositionAt(2500)));
        AddStep("hover dense first second", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(500)))));
        AddAssert("tooltip describes its real one-second window", () => chart.TooltipText.ToString(),
            () => Is.EqualTo(BmsStrings.PracticeDensityWindow(0, 1000, 8).ToString()));
        AddStep("remember full-chart density and zoom into silence", () =>
        {
            histogram = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Where(rectangle => rectangle.Bounds.X == chart.DensityLeft + 2)
                .Select(rectangle => rectangle.Bounds).ToArray();
            chart.Viewport.Set(2000, 1000);
            chart.Zoom(1);
        });
        AddAssert("full-chart density remains fixed while viewport zooms", () => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
            .Where(rectangle => rectangle.Bounds.X == chart.DensityLeft + 2).Select(rectangle => rectangle.Bounds).SequenceEqual(histogram));
        AddStep("click the dense first second to locate it", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(500))));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("visible range centres on chosen density", () => Math.Abs(chart.Viewport.Start) < 0.01 && Math.Abs(chart.Viewport.End - 1000) < 0.01);
        AddStep("drag lower visible edge upwards", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(0))));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("move lower edge to half a second", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.DensityLeft + 20, overviewPositionAt(500)))));
        AddStep("release lower edge", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("lower edge moves both ends at the same zoom", () => Math.Abs(chart.Viewport.Start - 500) < 0.01
            && Math.Abs(chart.Viewport.End - 1500) < 0.01 && Math.Abs(chart.Viewport.Duration - 1000) < 0.01);
        AddAssert("density navigation leaves exercise boundaries unchanged", () => settings.StartTime == 0 && settings.EndTime == 4000);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DensityOverviewHandlesZeroDensityAndVeryLongSilence(bool empty)
    {
        AddStep("replace chart with zero density or long silence", () =>
        {
            var beatmap = new BmsBeatmap
            {
                TotalColumns = 8,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                HitObjects = empty ? [new BmsLandmine { StartTime = 0 }]
                    : [new BmsNote { StartTime = 0 }, new BmsNote { StartTime = 1_000_000_000 }],
            };
            chart.SetBeatmap(beatmap, new BmsPracticeSettings { StartTime = 0, EndTime = empty ? 1 : 1_000_000_001 }, true);
        });
        AddAssert("density bars are bounded by display height", () => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Count(rectangle => rectangle.Bounds.X == chart.DensityLeft + 2),
            () => Is.EqualTo(empty ? 0 : 2));
        AddAssert("all overview geometry is finite and stays inside the chart", () => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
            .Where(rectangle => rectangle.Bounds.X >= chart.OverviewLeft).All(rectangle => float.IsFinite(rectangle.Bounds.X)
                && float.IsFinite(rectangle.Bounds.Y) && float.IsFinite(rectangle.Bounds.Width) && float.IsFinite(rectangle.Bounds.Height)
                && rectangle.Bounds.Left >= chart.OverviewLeft && rectangle.Bounds.Right <= chart.DrawWidth
                && rectangle.Bounds.Top >= 18 && rectangle.Bounds.Bottom <= chart.DrawHeight - 18 + 0.01));
    }

    [Test]
    public void MiniatureAndDensityShareTheFullSongAndVisibleRange()
    {
        AddStep("browse a detailed part of a longer selection", () =>
        {
            chart.Viewport.Set(10000, 20000);
            chart.Zoom(1);
        });
        AddAssert("miniature keeps note columns beside the density strip", () =>
        {
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            return rectangles.Any(rectangle => rectangle.Bounds.Left >= chart.OverviewLeft
                       && rectangle.Bounds.Right <= chart.DensityLeft - 8 && rectangle.Bounds.Height == 1
                       && rectangle.Bounds.Width > 0 && rectangle.Bounds.Width < 12)
                   && rectangles.Any(rectangle => rectangle.Bounds.X == chart.DensityLeft + 2
                       && rectangle.Bounds.Width > 0 && rectangle.Bounds.Width <= 36);
        });
        AddAssert("both strips outline the same viewport on the full-song scale", () =>
        {
            var outlines = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles
                .Where(rectangle => rectangle.Outline && rectangle.Bounds.X >= chart.OverviewLeft).ToArray();
            return outlines.Length == 2
                   && outlines.Any(rectangle => rectangle.Bounds.X == chart.OverviewLeft)
                   && outlines.Any(rectangle => rectangle.Bounds.X == chart.DensityLeft)
                   && outlines.All(rectangle => Math.Abs(rectangle.Bounds.Top - overviewPositionAt(chart.Viewport.End)) < 0.01
                       && Math.Abs(rectangle.Bounds.Bottom - overviewPositionAt(chart.Viewport.Start)) < 0.01);
        });
        AddAssert("narrow gutter contains only measure and short endpoint labels", () => chart.TimelineLeft <= 52
            && chart.ChildrenOfType<OsuSpriteText>().All(text => text.Text.ToString().StartsWith('#')
                || text.Text.Equals(BmsStrings.PracticeStartLine) || text.Text.Equals(BmsStrings.PracticeEndLine)));
    }

    [Test]
    public void DraggingBoundaryUsesOriginalChartTime()
    {
        AddStep("press start line", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(200, positionAt(10000))));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag to note", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(200, positionAt(15000)))));
        AddStep("release boundary", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("start snapped to chart note", () => Math.Abs(settings.StartTime - 15000) < 1);
        AddAssert("end retained", () => settings.EndTime == 20000);
    }

    [Test]
    public void FitSelectionIncludesCompleteHoldTail()
    {
        AddStep("fit selected range", chart.FitSelection);
        AddAssert("selected tail survives boundary", () => chart.SelectedEnd == 28000);
        AddAssert("tail visible with context", () => chart.Viewport.Start < 10000 && chart.Viewport.End > 28000);
    }

    [Test]
    public void SelectionRendersMeasuresAndSixteenthDivisions()
    {
        AddStep("view first five seconds", () =>
        {
            chart.Viewport.Set(0, 5000);
            chart.Zoom(1);
        });
        AddAssert("measure numbers label the grid", () => chart.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == BmsStrings.PracticeMeasure(2).ToString()));
        AddAssert("sixteenth-note line projected at 125ms", () => chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Any(rectangle =>
            rectangle.Bounds.Height == 1 && Math.Abs(rectangle.Bounds.Width - (chart.TimelineRight - chart.TimelineLeft)) < 0.01 && Math.Abs(rectangle.Bounds.Y - positionAt(125)) < 0.1));
        AddAssert("seconds do not label the grid", () => chart.ChildrenOfType<OsuSpriteText>().All(text => text.Text.ToString() != BmsStrings.PracticeTime(1000).ToString()));
    }

    [Test]
    public void NotesAreThickAndSquareWithinTheirColumns()
    {
        AddStep("show a note in the middle of the view", () =>
        {
            chart.Viewport.Set(0, 5000);
            chart.Zoom(1);
        });
        AddAssert("normal note keeps twelve-pixel thickness and fills its column", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            return hasSquareHead(1000, 1, laneWidth);
        });
    }

    [Test]
    public void CloseViewKeepsTwelvePixelHeadsAlignedToTheirTimeLines()
    {
        AddStep("show taps and a hold in a close view", () =>
        {
            var beatmap = new BmsBeatmap
            {
                TotalColumns = 8,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                HitObjects =
                [
                    new BmsNote { StartTime = 1000, Column = 1 },
                    new BmsNote { StartTime = 2000, Column = 1 },
                    new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 2 },
                    new BmsNote { StartTime = 6000, Column = 3 },
                ],
            };
            chart.SetBeatmap(beatmap, new BmsPracticeSettings { StartTime = 0, EndTime = 5000 }, true);
            chart.Viewport.Set(0, 5000);
            chart.Zoom(1);
        });
        AddAssert("close-up tap and hold heads remain twelve pixels thick", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            (double Time, int Column)[] notes = [(1000, 1), (2000, 1), (3000, 2)];
            return notes.All(note => hasSquareHead(note.Time, note.Column, laneWidth));
        });
    }

    [Test]
    public void LongRangeViewSeparatesAdjacentHeadsAndKeepsTheirBottomEdgesOnTime()
    {
        AddStep("fit a ninety-second practice range", () =>
        {
            var beatmap = new BmsBeatmap
            {
                TotalColumns = 8,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                HitObjects =
                [
                    new BmsNote { StartTime = 46000, Column = 1 },
                    new BmsNote { StartTime = 46125, Column = 1 },
                    new BmsNote { StartTime = 90000, Column = 2 },
                    new BmsNote { StartTime = 90040, Column = 2 },
                    new BmsLongNote { StartTime = 100000, Duration = 1000, Column = 3 },
                    new BmsNote { StartTime = 200000, Column = 0 },
                ],
            };
            settings = new BmsPracticeSettings { StartTime = 45000, EndTime = 135000 };
            chart.SetBeatmap(beatmap, settings, true);
            chart.FitSelection();
        });
        AddAssert("ninety-second range has its full context", () => chart.Viewport.Duration, () => Is.EqualTo(104400));
        AddAssert("zoomed-out heads shrink and keep accurate bottom edges", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var maximumHeight = 12 * 5000 / chart.Viewport.Duration;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            (double Time, int Column)[] notes = [(46000, 1), (46125, 1), (90000, 2), (90040, 2), (100000, 3)];
            return notes.All(note => rectangles.Any(rectangle => rectangle.Bounds.Left >= chart.TimelineLeft + note.Column * laneWidth - 0.01
                && rectangle.Bounds.Right <= chart.TimelineLeft + (note.Column + 1) * laneWidth + 0.01
                && rectangle.Bounds.Height > 0 && rectangle.Bounds.Height <= maximumHeight + 0.001
                && Math.Abs(rectangle.Bounds.Bottom - positionAt(note.Time)) < 0.01));
        });
        AddAssert("same-column heads retain a visible geometric gap", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            (double Earlier, double Later, int Column)[] pairs = [(46000, 46125, 1), (90000, 90040, 2)];
            return pairs.All(pair =>
            {
                var earlier = rectangles.Single(rectangle => rectangle.Bounds.Left >= chart.TimelineLeft + pair.Column * laneWidth - 0.01
                    && rectangle.Bounds.Right <= chart.TimelineLeft + (pair.Column + 1) * laneWidth + 0.01
                    && rectangle.Bounds.Height < 1 && Math.Abs(rectangle.Bounds.Bottom - positionAt(pair.Earlier)) < 0.01);
                return earlier.Bounds.Top - positionAt(pair.Later) >= (positionAt(pair.Earlier) - positionAt(pair.Later)) * 0.39;
            });
        });
        AddAssert("hold body matches its lane and has a square end", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var bodyX = chart.TimelineLeft + 3 * laneWidth;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            return hasHeadAtTime(100000, 3, laneWidth, 1)
                && rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.X - bodyX) < 0.01
                    && Math.Abs(rectangle.Bounds.Width - laneWidth) < 0.01
                    && Math.Abs(rectangle.Bounds.Top - positionAt(101000)) < 0.01
                    && Math.Abs(rectangle.Bounds.Bottom - positionAt(100000)) < 0.01);
        });
    }

    [Test]
    public void NoteHeadsAndLandminesAlignTheirBottomEdgesToTime()
    {
        AddStep("show notes with distinct times and columns", () =>
        {
            var beatmap = new BmsBeatmap
            {
                TotalColumns = 8,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                HitObjects =
                [
                    new BmsNote { StartTime = 1000, Column = 1 },
                    new BmsLongNote { StartTime = 2000, Duration = 2000, Column = 2 },
                    new BmsLandmine { StartTime = 3000, Column = 3 },
                ],
            };
            chart.SetBeatmap(beatmap, new BmsPracticeSettings { StartTime = 0, EndTime = 4000 }, true);
            chart.FitAll();
        });
        AddAssert("normal note and LN head end on their time lines", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            (double Time, int Column)[] notes = [(1000, 1), (2000, 2)];
            return notes.All(note => hasSquareHead(note.Time, note.Column, laneWidth));
        });
        AddAssert("landmine base and stem end on its time line", () =>
        {
            var laneWidth = (chart.TimelineRight - chart.TimelineLeft) / 8;
            var x = chart.TimelineLeft + 3 * laneWidth;
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            return rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.X - x) < 0.01
                       && Math.Abs(rectangle.Bounds.Width - laneWidth) < 0.01 && rectangle.Bounds.Height == 3
                       && Math.Abs(rectangle.Bounds.Bottom - positionAt(3000)) < 0.01)
                   && rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.X - (x + laneWidth * 0.45f)) < 0.01
                       && rectangle.Bounds.Height == 14 && Math.Abs(rectangle.Bounds.Bottom - positionAt(3000)) < 0.01);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OffscreenBoundaryHintsRemainSeparateAndBothCanBeLocated(bool above)
    {
        AddStep("move selected range out of view", () =>
        {
            chart.Viewport.Set(above ? 30000 : 0, 5000);
            chart.Zoom(1);
        });
        AddAssert("boundary hints do not overlap", () =>
        {
            var start = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeStartLine.ToString());
            var end = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeEndLine.ToString());
            return start.Y - end.Y >= 18;
        });
        AddStep("locate offscreen end", () =>
        {
            var end = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeEndLine.ToString());
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(30, end.Y + 8)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("end is back in view", () => chart.Viewport.Start <= settings.EndTime && chart.Viewport.End >= settings.EndTime);
        AddAssert("locating leaves selection unchanged", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [TestCase(10000d, 10125d, 500f)]
    [TestCase(19875d, 20000d, 500f)]
    [TestCase(0d, 125d, 500f)]
    [TestCase(10000d, 10125d, 60f)]
    public void ShortRangeLabelsRemainSeparateWithoutMovingTimeLines(double start, double end, float height)
    {
        AddStep("show a short selection in a broad view", () =>
        {
            chart.Parent!.Height = height;
            settings = settings with { StartTime = start, EndTime = end };
            chart.SetSelection(settings);
            chart.Viewport.Set(0, 20000);
            chart.Zoom(1);
        });
        AddAssert("both boundary labels remain visible", () => chart.ChildrenOfType<OsuSpriteText>().Count(text =>
            text.Text.ToString() == BmsStrings.PracticeStartLine.ToString()
            || text.Text.ToString() == BmsStrings.PracticeEndLine.ToString()), () => Is.EqualTo(2));
        AddAssert("all gutter text stays inside the chart", () => chart.ChildrenOfType<OsuSpriteText>().All(text =>
            chart.ScreenSpaceDrawQuad.AABBFloat.Contains(text.ScreenSpaceDrawQuad.AABBFloat)));
        AddAssert("boundary details and measure labels do not overlap", () =>
        {
            var bounds = chart.ChildrenOfType<OsuSpriteText>().Select(text => text.ScreenSpaceDrawQuad.AABBFloat).OrderBy(rectangle => rectangle.Top).ToArray();
            return bounds.Zip(bounds.Skip(1), (earlier, later) => earlier.Bottom <= later.Top + 0.01f).All(separate => separate);
        });
        AddAssert("range lines remain at their exact time positions", () =>
        {
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            double[] times = [start, end];
            return times.All(time => rectangles.Any(rectangle =>
                rectangle.Bounds.X == chart.TimelineLeft - 10 && rectangle.Bounds.Height == 2
                && Math.Abs(rectangle.Bounds.Y + 1 - positionAt(time)) < 0.01));
        });
    }

    [Test]
    public void SeparatedBoundaryLabelSelectsItsOwnBoundary()
    {
        AddStep("show a one-sixteenth selection", () =>
        {
            settings = settings with { StartTime = 10000, EndTime = 10125 };
            chart.SetSelection(settings);
            chart.Viewport.Set(0, 20000);
            chart.Zoom(1);
        });
        AddStep("select shifted start label", () =>
        {
            var label = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeStartLine.ToString());
            InputManager.MoveMouseTo(label.ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });
        AddStep("move selected start earlier", () => InputManager.Key(Key.Down));
        AddAssert("only start moved to the preceding grid line", () => settings.StartTime == 9875 && settings.EndTime == 10125);
    }

    [Test]
    public void DraggingSeparatedLabelKeepsItsOffsetFromTheRangeLine()
    {
        var pointer = Vector2.Zero;
        AddStep("show a one-sixteenth selection", () =>
        {
            settings = settings with { StartTime = 10000, EndTime = 10125 };
            chart.SetSelection(settings);
            chart.Viewport.Set(0, 20000);
            chart.Zoom(1);
        });
        AddStep("press shifted start label", () =>
        {
            var label = chart.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == BmsStrings.PracticeStartLine.ToString());
            pointer = new Vector2(30, label.Y + 8);
            InputManager.MoveMouseTo(chart.ToScreenSpace(pointer));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag label earlier by two seconds", () =>
            InputManager.MoveMouseTo(chart.ToScreenSpace(pointer + new Vector2(0, positionAt(8000) - positionAt(10000)))));
        AddStep("release shifted label", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("displaced label preserves the exact drag distance", () => settings.StartTime == 8000 && settings.EndTime == 10125);
    }

    [Test]
    public void TouchPinchAdjustsOnlyViewport()
    {
        var duration = 0d;
        AddStep("begin pinch", () =>
        {
            duration = chart.Viewport.Duration;
            InputManager.BeginTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 180))));
            InputManager.BeginTouch(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 320))));
        });
        AddStep("spread fingers", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 400)))));
        AddAssert("touch zoomed", () => chart.Viewport.Duration < duration);
        AddStep("release touches", () =>
        {
            InputManager.EndTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 180))));
            InputManager.EndTouch(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 400))));
        });
        AddAssert("selection unchanged", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [Test]
    public void TouchPinchWithAnActiveBoundaryDoesNotPlaceOnSequentialRelease()
    {
        var duration = 0d;
        AddStep("select start while browsing a detailed viewport", () =>
        {
            chart.Viewport.Set(5000, 40000);
            chart.Zoom(1);
            chart.SelectBoundary(true);
            duration = chart.Viewport.Duration;
        });
        AddStep("begin a pinch away from both range lines", () =>
        {
            InputManager.BeginTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 140))));
            InputManager.BeginTouch(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 300))));
        });
        AddStep("spread the second finger", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 380)))));
        AddAssert("pinch zooms the viewport", () => chart.Viewport.Duration < duration);
        AddStep("lift the first finger", () => InputManager.EndTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 140)))));
        AddAssert("first release keeps the selected range", () => settings.StartTime == 10000 && settings.EndTime == 20000);
        AddStep("move the remaining finger slightly", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 382)))));
        AddStep("lift the remaining finger", () => InputManager.EndTouch(new Touch(TouchSource.Touch2, chart.ToScreenSpace(new Vector2(300, 382)))));
        AddAssert("the consumed pinch never places the active boundary", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [Test]
    public void SlowTouchPanUsesTotalTravelAndDoesNotPlaceTheActiveBoundary()
    {
        AddStep("select start while browsing away from both range lines", () =>
        {
            chart.Viewport.Set(5000, 40000);
            chart.Zoom(1);
            chart.SelectBoundary(true);
            InputManager.BeginTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 160))));
        });
        AddStep("move only two pixels", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 162)))));
        AddStep("move another two pixels", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 164)))));
        AddStep("pass the drag threshold cumulatively", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 166)))));
        AddStep("continue the slow pan", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 168)))));
        AddAssert("small moves pan the viewport", () => chart.Viewport.Start > 5000 && Math.Abs(chart.Viewport.Duration - 40000) < 0.01);
        AddStep("release the slow pan", () => InputManager.EndTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(300, 168)))));
        AddAssert("release preserves both range boundaries", () => settings.StartTime == 10000 && settings.EndTime == 20000);
    }

    [Test]
    public void TouchDragResetsPreviousMouseSnapOverride()
    {
        AddStep("prepare close view", () => { chart.Viewport.Set(0, 30000); chart.Zoom(1); });
        AddStep("begin mouse drag without snap", () =>
        {
            InputManager.PressKey(Key.AltLeft);
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(200, positionAt(settings.StartTime))));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("move between grid points", () => InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(200, positionAt(12020)))));
        AddStep("release mouse override", () => { InputManager.ReleaseButton(MouseButton.Left); InputManager.ReleaseKey(Key.AltLeft); });
        AddAssert("mouse remained off grid", () => Math.Abs(settings.StartTime - 12020) < 1);
        AddStep("begin touch drag", () => InputManager.BeginTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(200, positionAt(settings.StartTime))))));
        AddStep("touch moves near grid point", () => InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(200, positionAt(15020))))));
        AddStep("release touch", () => InputManager.EndTouch(new Touch(TouchSource.Touch1, chart.ToScreenSpace(new Vector2(200, positionAt(15020))))));
        AddAssert("touch restored grid snapping", () => Math.Abs(settings.StartTime - 15000) < 1);
    }

    private float positionAt(double time) => 18 + (float)((chart.Viewport.End - time) / chart.Viewport.Duration) * (chart.DrawHeight - 36);
    private float overviewPositionAt(double time) => 18 + (float)((chart.Viewport.Maximum - time) / (chart.Viewport.Maximum - chart.Viewport.Minimum)) * (chart.DrawHeight - 36);

    private bool hasSquareHead(double time, int column, float laneWidth, float height = 12)
    {
        var left = chart.TimelineLeft + column * laneWidth;
        var bottom = positionAt(time);
        var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
        return rectangles.Any(rectangle => Math.Abs(rectangle.Bounds.Left - left) < 0.01
            && Math.Abs(rectangle.Bounds.Width - laneWidth) < 0.01
            && Math.Abs(rectangle.Bounds.Height - height) < 0.01
            && Math.Abs(rectangle.Bounds.Bottom - bottom) < 0.01);
    }

    private bool hasHeadAtTime(double time, int column, float laneWidth, float maximumHeight)
    {
        var left = chart.TimelineLeft + column * laneWidth;
        var right = left + laneWidth;
        var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
        return rectangles.Any(rectangle => rectangle.Bounds.Left >= left - 0.01
            && rectangle.Bounds.Right <= right + 0.01 && rectangle.Bounds.Height > 0
            && rectangle.Bounds.Height <= maximumHeight && Math.Abs(rectangle.Bounds.Bottom - positionAt(time)) < 0.01);
    }
}
