#nullable enable
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 1280, 720)]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 760, 900)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 1280, 720)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 760, 900)]
    public void RealChartEndpointsCanBeSetInSeparateCloseViews(string directory, string filename, int width, int height)
    {
        BmsPracticeSetupOverlay setup = null!;
        BmsPracticeChart chart = null!;
        BmsPracticeSettings selection = null!;
        BmsPracticeSession? started = null;
        double[] gridTimes = [];
        var fullStart = 0d;
        var fullEnd = 0d;
        var targetStart = 0d;
        var targetEnd = 0d;
        var laterCentre = 0d;
        var beforeDrag = 0d;
        var viewDuration = 0d;

        AddStep("open a real song for cross-view selection", () =>
        {
            var path = Path.Combine(longRangePracticeSongsRoot(), directory, filename);
            var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path), randomValueSelector: _ => 1);
            var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
            {
                BranchRandomValueSelector = _ => 1,
            }.Convert();
            BmsTestBeatmaps.SetupBeatmapInfo(beatmap, new BmsRuleset().RulesetInfo);
            beatmap.Metadata.Source = Path.GetDirectoryName(path)!;
            gridTimes = BmsPracticeGrid.Create(beatmap.TimingMap, 16).Select(line => line.Time).Distinct().Order().ToArray();
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = new Container
            {
                Size = new Vector2(width, height),
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(new TestWorkingBeatmap(beatmap, audioManager: Audio), [],
                    BmsPracticeSettings.CreateDefault(beatmap) with { SkipResults = true, CountIn = 3 })
                {
                    StartRequested = session => started = session,
                },
            };
            setup.Show();
        });
        AddUntilStep("real chart layout is ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("choose start and retain a close view", () =>
        {
            chart = setup.SelectionChart;
            fullStart = chart.Viewport.Minimum;
            fullEnd = chart.Viewport.Maximum;
            targetStart = gridTimes.First(time => time >= fullStart + 30000);
            laterCentre = fullStart + (fullEnd - fullStart) * 0.78;
            clickRealChartControl(setup.StartBoundaryButton);
            chart.Viewport.Set(targetStart - 3000, 6000);
            chart.Zoom(1);
            viewDuration = chart.Viewport.Duration;
        });
        AddAssert("start is the explicit edit target", () => chart.ActiveBoundary, () => Is.True);
        AddStep("place start on the visible pattern", () =>
        {
            InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, targetStart));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("start is placed on the musical grid", () => setup.Settings.StartTime, () => Is.EqualTo(targetStart).Within(0.000001));
        AddAssert("placing start keeps the distant end", () => setup.Settings.EndTime, () => Is.EqualTo(fullEnd));
        AddStep("browse to a distant pattern using the thumbnail", () =>
        {
            selection = setup.Settings;
            InputManager.MoveMouseTo(crossViewportOverviewPosition(chart, laterCentre));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("thumbnail moves to the distant pattern at the same zoom", () => chart.Viewport.Start + chart.Viewport.Duration / 2,
            () => Is.EqualTo(laterCentre).Within(0.1));
        AddAssert("thumbnail browsing keeps the chosen start and settings", () => setup.Settings == selection
            && Math.Abs(chart.Viewport.Duration - viewDuration) < 0.000001);
        AddStep("choose end without moving the current view", () =>
        {
            // The offscreen end's clamped edge hint must not intercept placement on a visible pattern.
            targetEnd = chart.SnapTime(laterCentre + viewDuration * 0.48);
            clickRealChartControl(setup.EndBoundaryButton);
        });
        AddAssert("end is the explicit edit target", () => chart.ActiveBoundary, () => Is.False);
        AddAssert("choosing an endpoint preserves the distant view", () => chart.Viewport.Start + chart.Viewport.Duration / 2,
            () => Is.EqualTo(laterCentre).Within(0.1));
        AddAssert("the fixed start is outside the current view", () => setup.Settings.StartTime, () => Is.LessThan(chart.Viewport.Start));
        AddStep("place end while the start is offscreen", () =>
        {
            InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, targetEnd));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("end is placed without moving the start", () => Math.Abs(setup.Settings.EndTime - targetEnd) < 0.000001
            && Math.Abs(setup.Settings.StartTime - targetStart) < 0.000001);
        AddAssert("separate close views create a range longer than sixty seconds", () => setup.Settings.EndTime - setup.Settings.StartTime,
            () => Is.GreaterThan(60000));
        AddAssert("the chart only labels measures and boundary names", () => chart.ChildrenOfType<OsuSpriteText>().All(label =>
            label.Text.Equals(BmsStrings.PracticeStartLine) || label.Text.Equals(BmsStrings.PracticeEndLine)
            || label.Text.ToString().StartsWith("#", StringComparison.Ordinal)));

        AddStep("locate the fixed start", () =>
        {
            selection = setup.Settings;
            clickRealChartControl(setup.LocateStartButton);
        });
        AddAssert("locating start preserves close zoom and range", () => chart.Viewport.Start <= targetStart && chart.Viewport.End >= targetStart
            && Math.Abs(chart.Viewport.Duration - viewDuration) < 0.000001 && setup.Settings == selection);
        AddStep("choose end while start is visible", () => clickRealChartControl(setup.EndBoundaryButton));
        AddStep("choose a distinct visible grid point for dragging start", () =>
        {
            beforeDrag = setup.Settings.StartTime;
            // STOPs can snap many consecutive times to the same point; drag to a real later grid line instead.
            targetStart = gridTimes.First(time => time > beforeDrag + 1500 && time < chart.Viewport.End - 250);
        });
        AddAssert("drag target advances along the visible musical grid", () => targetStart > beforeDrag && targetStart < chart.Viewport.End);
        AddStep("press the visible start line", () =>
        {
            InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, beforeDrag));
            InputManager.PressButton(MouseButton.Left);
        });
        AddAssert("pressing the visible start selects its handle", () => chart.ActiveBoundary, () => Is.True);
        AddStep("drag the visible start while end was selected", () => InputManager.MoveMouseTo(crossViewportTimelinePosition(chart, targetStart)));
        AddStep("release the visible start", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("direct handle drag still selects start", () => chart.ActiveBoundary, () => Is.True);
        AddAssert("direct handle drag moves start to its snapped target", () => setup.Settings.StartTime, () => Is.EqualTo(targetStart).Within(0.000001));
        AddAssert("direct handle drag moves forward", () => setup.Settings.StartTime, () => Is.GreaterThan(beforeDrag));
        AddAssert("direct handle drag preserves the distant end", () => setup.Settings.EndTime, () => Is.EqualTo(targetEnd).Within(0.000001));
        AddStep("locate end at the same close zoom", () =>
        {
            selection = setup.Settings;
            clickRealChartControl(setup.LocateEndButton);
        });
        AddAssert("locating end preserves close zoom and range", () => chart.Viewport.Start <= targetEnd && chart.Viewport.End >= targetEnd
            && Math.Abs(chart.Viewport.Duration - viewDuration) < 0.000001 && setup.Settings == selection);
        AddStep("browse independently using density", () =>
        {
            laterCentre = fullStart + (fullEnd - fullStart) * 0.5;
            InputManager.MoveMouseTo(crossViewportOverviewPosition(chart, laterCentre, true));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("density also moves the close viewport without editing either endpoint", () => Math.Abs(chart.Viewport.Start + chart.Viewport.Duration / 2 - laterCentre) < 0.1
            && Math.Abs(chart.Viewport.Duration - viewDuration) < 0.000001 && setup.Settings == selection);
        AddStep("start the range selected across views", () => clickRealChartControl(setup.StartButton));
        AddAssert("session retains both independently chosen endpoints", () => started != null && started.Settings == selection);
        AddAssert("cross-view selection closes after starting", () => setup.State.Value, () => Is.EqualTo(Visibility.Hidden));
    }

    private static Vector2 crossViewportTimelinePosition(BmsPracticeChart chart, double time)
        => chart.ToScreenSpace(new Vector2((chart.TimelineLeft + chart.TimelineRight) / 2,
            18 + (float)((chart.Viewport.End - time) / chart.Viewport.Duration) * Math.Max(1, chart.DrawHeight - 36)));

    private static Vector2 crossViewportOverviewPosition(BmsPracticeChart chart, double time, bool density = false)
        => chart.ToScreenSpace(density
            ? new Vector2(chart.DensityLeft + 20, 18 + (float)((chart.Viewport.Maximum - time) / (chart.Viewport.Maximum - chart.Viewport.Minimum)) * Math.Max(1, chart.DrawHeight - 36))
            : chart.MiniaturePositionAt(time));
}
