#nullable enable

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeChart
{
    [TestCase(520, 660, 8)]
    [TestCase(520, 660, 16)]
    [TestCase(520, 160, 18)]
    public void LongOverviewUsesSeparateColumnsAndClipsCrossingHolds(int width, int height, int keys)
    {
        var span = 0d;
        AddStep("show a long chart", () => setLongOverviewChart(width, height, keys));
        AddStep("place a long note across the first column seam", () =>
        {
            span = (chart.Viewport.Maximum - chart.Viewport.Minimum) / chart.OverviewColumns;
            var beatmap = new BmsBeatmap
            {
                TotalColumns = keys,
                HitObjects =
                [
                    new BmsLongNote { StartTime = span - 1000, Duration = 2000, Column = 1 },
                    new BmsNote { StartTime = 359999, Column = 0 },
                ],
            };
            chart.SetBeatmap(beatmap, settings, true);
            chart.Viewport.Set(span - 5000, 10000);
            chart.Zoom(1);
        });
        AddAssert("long chart unfolds while main lanes stay readable", () => chart.OverviewColumns >= 2
            && (chart.TimelineRight - chart.TimelineLeft) / keys >= 16);
        AddAssert("time runs upward in a column and then continues to its right", () =>
        {
            var early = chart.MiniaturePositionAt(span * 0.25);
            var late = chart.MiniaturePositionAt(span * 0.75);
            var next = chart.MiniaturePositionAt(span * 1.25);
            return early.X == late.X && early.Y > late.Y && next.X > early.X && Math.Abs(next.Y - early.Y) < 0.01;
        });
        AddAssert("crossing hold continues in the next column without duplicating its head", () =>
        {
            var rectangles = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles;
            var bodies = rectangles.Where(rectangle => rectangle.Bounds.Left >= chart.OverviewLeft
                && rectangle.Bounds.Right < chart.DensityLeft && rectangle.Colour.A == 0.6f).ToArray();
            var heads = rectangles.Where(rectangle => rectangle.Bounds.Left >= chart.OverviewLeft
                && rectangle.Bounds.Right < chart.DensityLeft && rectangle.Colour.A == 0.8f && rectangle.Bounds.Height == 1).ToArray();
            return bodies.Length == 2 && heads.Length == 2
                && bodies.Any(body => Math.Abs(body.Bounds.Top - 18) < 0.01)
                && bodies.Any(body => Math.Abs(body.Bounds.Bottom - chart.DrawHeight + 18) < 0.01)
                && bodies.All(body => body.Bounds.Top >= 18 && body.Bounds.Bottom <= chart.DrawHeight - 18);
        });
        AddAssert("a view crossing the seam has two thumbnail frames and one density frame", () =>
        {
            var frames = chart.ChildrenOfType<BmsReplayVisualizer.PlotLayer>().Single().Rectangles.Where(rectangle => rectangle.Outline).ToArray();
            return frames.Length == 3 && frames.Count(frame => frame.Bounds.Left < chart.DensityLeft) == 2
                && frames.All(frame => frame.Bounds.Top >= 18 && frame.Bounds.Bottom <= chart.DrawHeight - 18);
        });
        AddStep("resize and keep the chosen zoom", () => ((Container)chart.Parent!).Size = new Vector2(700, 500));
        AddAssert("resizing keeps view and selected range", () => chart.Viewport.Duration == 10000
            && Math.Abs(chart.Viewport.Start - span + 5000) < 0.01 && settings.StartTime == 20000 && settings.EndTime == 340000);
    }

    [TestCase(false, -1, false)]
    [TestCase(false, 0, false)]
    [TestCase(false, 1, false)]
    [TestCase(true, -1, false)]
    [TestCase(true, 0, false)]
    [TestCase(true, 1, false)]
    [TestCase(false, 1, true)]
    [TestCase(true, -1, true)]
    public void EveryOverviewFrameGrabMovesTheViewWithoutResizing(bool density, int edge, bool touch)
    {
        var start = 0d;
        var grabTime = 0d;
        Vector2 point(double time) => chart.ToScreenSpace(density
            ? new Vector2(chart.DensityLeft + 20, overviewPositionAt(time))
            : chart.MiniaturePositionAt(time));

        AddStep("show a detailed view of a long chart", () =>
        {
            setLongOverviewChart(520, 660, 8);
            chart.Viewport.Set(120000, 10000);
            chart.Zoom(1);
            start = chart.Viewport.Start;
            grabTime = start + 5000 + edge * 5000;
        });
        AddStep("grab frame at the chosen edge or centre", () =>
        {
            if (touch)
                InputManager.BeginTouch(new Touch(TouchSource.Touch1, point(grabTime)));
            else
            {
                InputManager.MoveMouseTo(point(grabTime));
                InputManager.PressButton(MouseButton.Left);
            }
        });
        AddAssert("grabbing a frame does not recenter it", () => Math.Abs(chart.Viewport.Start - start) < 0.01 && chart.Viewport.Duration == 10000);
        AddStep("move the grab point later in the song", () =>
        {
            if (touch)
                InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, point(grabTime + 5000)));
            else
                InputManager.MoveMouseTo(point(grabTime + 5000));
        });
        AddStep("release the frame", () =>
        {
            if (touch)
                InputManager.EndTouch(new Touch(TouchSource.Touch1, point(grabTime + 5000)));
            else
                InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("both view endpoints move together", () => Math.Abs(chart.Viewport.Start - start - 5000) < 0.1 && chart.Viewport.Duration == 10000);
        AddAssert("navigation preserves exercise boundaries", () => settings.StartTime == 20000 && settings.EndTime == 340000);
        AddStep("control-scroll over the overview", () =>
        {
            InputManager.MoveMouseTo(point(grabTime + 5000));
            InputManager.PressKey(Key.ControlLeft);
            InputManager.ScrollVerticalBy(1);
            InputManager.ReleaseKey(Key.ControlLeft);
        });
        AddAssert("overview scrolling also preserves zoom", () => chart.Viewport.Duration == 10000);
    }

    [Test]
    public void MiniatureNavigationCrossesColumnsAndIgnoresGaps()
    {
        var span = 0d;
        AddStep("show a view close to a column seam", () =>
        {
            setLongOverviewChart(520, 660, 8);
            span = (chart.Viewport.Maximum - chart.Viewport.Minimum) / chart.OverviewColumns;
            chart.Viewport.Set(span - 7000, 10000);
            chart.Zoom(1);
        });
        AddStep("grab first-column frame", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(chart.MiniaturePositionAt(span - 2000)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("continue dragging into the next column", () => InputManager.MoveMouseTo(chart.ToScreenSpace(chart.MiniaturePositionAt(span + 4000))));
        AddStep("release in the next column", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("view crosses the seam at the same zoom", () => Math.Abs(chart.Viewport.Start - span + 1000) < 0.1 && chart.Viewport.Duration == 10000);
        AddStep("click the gap between columns", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(new Vector2(chart.OverviewLeft + 52, chart.DrawHeight / 2)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("gap click leaves the view alone", () => Math.Abs(chart.Viewport.Start - span + 1000) < 0.1 && chart.Viewport.Duration == 10000);
        AddStep("click the last column away from the frame", () =>
        {
            InputManager.MoveMouseTo(chart.ToScreenSpace(chart.MiniaturePositionAt(300000)));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("last column locates its part of the song", () => Math.Abs(chart.Viewport.Start - 295000) < 0.1 && chart.Viewport.Duration == 10000);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PinchingAnOverviewPreservesViewDuration(bool density)
    {
        Vector2 first = default;
        Vector2 second = default;
        AddStep("show a long chart and touch its overview", () =>
        {
            setLongOverviewChart(520, 660, 8);
            chart.Viewport.Set(120000, 10000);
            chart.Zoom(1);
            var centre = density ? new Vector2(chart.DensityLeft + 20, overviewPositionAt(125000)) : chart.MiniaturePositionAt(125000);
            first = chart.ToScreenSpace(centre - new Vector2(0, 10));
            second = chart.ToScreenSpace(centre + new Vector2(0, 10));
            InputManager.BeginTouch(new Touch(TouchSource.Touch1, first));
            InputManager.BeginTouch(new Touch(TouchSource.Touch2, second));
        });
        AddStep("spread fingers over the overview", () =>
        {
            InputManager.MoveTouchTo(new Touch(TouchSource.Touch1, first - new Vector2(0, 20)));
            InputManager.MoveTouchTo(new Touch(TouchSource.Touch2, second + new Vector2(0, 20)));
        });
        AddStep("release both touches", () =>
        {
            InputManager.EndTouch(new Touch(TouchSource.Touch1, first - new Vector2(0, 20)));
            InputManager.EndTouch(new Touch(TouchSource.Touch2, second + new Vector2(0, 20)));
        });
        AddAssert("overview gesture leaves zoom and selection intact", () => chart.Viewport.Duration == 10000
            && settings.StartTime == 20000 && settings.EndTime == 340000);
    }

    private void setLongOverviewChart(int width, int height, int keys)
    {
        ((Container)chart.Parent!).Size = new Vector2(width, height);
        var beatmap = new BmsBeatmap
        {
            TotalColumns = keys,
            LayoutVariant = keys == 18 ? BmsLayoutVariant.Pms9KDouble : keys == 16 ? BmsLayoutVariant.Bme7KDouble : BmsLayoutVariant.Bme7K,
            HitObjects = Enumerable.Range(0, 360).Select(index => (BmsHitObject)new BmsNote { StartTime = index * 1000, Column = index % keys })
                .Append(new BmsNote { StartTime = 359999, Column = 0 }).ToList(),
        };
        settings = new BmsPracticeSettings { StartTime = 20000, EndTime = 340000 };
        chart.SetBeatmap(beatmap, settings, true);
    }
}
