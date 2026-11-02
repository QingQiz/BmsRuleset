#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsPracticeSetup
{
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 1280, 720)]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 1024, 900)]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml", 760, 900)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 1280, 720)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 1024, 900)]
    [TestCase("Aleph-0 (by LeaF)", "_7ANOTHER.bms", 760, 900)]
    public void RealChartLongRangeBrowsingPreservesSelectionAndStarts(string directory, string filename, int width, int height)
    {
        BmsPracticeSetupOverlay setup = null!;
        BmsPracticeChart chart = null!;
        TestWorkingBeatmap source = null!;
        BmsBeatmap fullBeatmap = null!;
        BmsPracticeSession? started = null;
        BmsPracticeSettings selection = null!;
        RoundedSliderBar<double> zoom = null!;
        OsuDropdown<int> snap = null!;
        Container viewport = null!;
        var starts = 0;
        var fullStart = 0d;
        var fullEnd = 0d;
        var fullDuration = 0d;
        var zoomedDuration = 0d;

        AddStep("open a real full-chart selection", () =>
        {
            var path = Path.Combine(longRangePracticeSongsRoot(), directory, filename);
            var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path), randomValueSelector: _ => 1);
            var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
            {
                BranchRandomValueSelector = _ => 1,
            }.Convert();
            BmsTestBeatmaps.SetupBeatmapInfo(beatmap, new BmsRuleset().RulesetInfo);
            beatmap.Metadata.Source = Path.GetDirectoryName(path)!;
            source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Ruleset.Value = new BmsRuleset().RulesetInfo;
            Child = viewport = new Container
            {
                Size = new Vector2(width, height),
                // Keep all input within the test scene without changing the logical layout breakpoints.
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(source, [], BmsPracticeSettings.CreateDefault(beatmap) with
                {
                    SkipResults = true,
                    CountIn = 5,
                })
                {
                    StartRequested = session => { started = session; starts++; },
                },
            };
            setup.Show();
        });
        AddUntilStep("full-chart selection is ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("remember the full chart", () =>
        {
            chart = setup.SelectionChart;
            fullBeatmap = BmsPracticeSession.GetFullBeatmap(source, setup.SelectedMods);
            selection = setup.Settings;
            fullStart = chart.Viewport.Minimum;
            fullEnd = chart.Viewport.Maximum;
            fullDuration = fullEnd - fullStart;
            zoom = setup.ChildrenOfType<RoundedSliderBar<double>>().Single();
            snap = setup.ChildrenOfType<OsuDropdown<int>>().Single(dropdown => dropdown.Items.SequenceEqual([0, 4, 8, 12, 16, 24, 32, 48, 64]));
        });
        AddAssert("initial range covers the full song", () => selection.StartTime == fullStart && selection.EndTime == fullEnd);
        AddAssert("full song is longer than ninety seconds", () => fullDuration, () => Is.GreaterThan(90000));
        AddAssert("all chart notes are selected", () => BmsPracticeBeatmap.SelectedCount(fullBeatmap, selection),
            () => Is.EqualTo(BmsPracticeBeatmap.SelectedCount(fullBeatmap, BmsPracticeSettings.CreateDefault(fullBeatmap))));
        AddAssert("start remains accessible with a full-chart range", () => viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat));
        AddAssert("full-chart boundary labels fit and stay separate", () => longRangeBoundaryLabelsFit(chart, setup.Settings));
        AddStep("fit the whole chart", () => clickRealChartControl(setup.ChildrenOfType<RoundedButton>()
            .Single(button => button.Text.Equals(BmsStrings.PracticeFullChart))));
        AddAssert("full chart viewport matches its bounds", () => chart.Viewport.Start == fullStart && chart.Viewport.End == fullEnd);
        AddAssert("fit all keeps selection and repeat settings", () => setup.Settings, () => Is.EqualTo(selection));

        AddStep("press the full-chart start line", () =>
        {
            InputManager.MoveMouseTo(realChartPosition(chart, setup.Settings.StartTime));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag start into the song", () => InputManager.MoveMouseTo(realChartPosition(chart, fullStart + fullDuration * 0.17)));
        AddStep("release the start line", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("long-range start snaps to the sixteenth grid", () => setup.Settings.StartTime > fullStart
            && BmsPracticeGrid.Create(fullBeatmap.TimingMap, 16).Any(line => Math.Abs(line.Time - setup.Settings.StartTime) < 0.000001));
        AddAssert("start drag leaves the full-chart end intact", () => setup.Settings.EndTime, () => Is.EqualTo(fullEnd));
        AddStep("open long-range snap choices", () => clickRealChartControl(snap.ChildrenOfType<DropdownHeader>().Single()));
        AddUntilStep("measure alignment is available", () => snap.ChildrenOfType<Menu.DrawableMenuItem>()
            .Any(item => item.IsPresent && item.Item is DropdownMenuItem<int> { Value: 0 }));
        AddStep("choose measure alignment for the end", () => clickRealChartControl(snap.ChildrenOfType<Menu.DrawableMenuItem>()
            .Single(item => item.Item is DropdownMenuItem<int> { Value: 0 })));
        AddAssert("measure snapping is active", () => chart.SnapDivisor, () => Is.Zero);
        AddStep("press the full-chart end line", () =>
        {
            selection = setup.Settings;
            InputManager.MoveMouseTo(realChartPosition(chart, setup.Settings.EndTime));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag end into the song", () => InputManager.MoveMouseTo(realChartPosition(chart, fullStart + fullDuration * 0.83)));
        AddStep("release the end line", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("long-range end snaps to a measure", () => setup.Settings.EndTime < fullEnd
            && BmsPracticeGrid.Create(fullBeatmap.TimingMap, 0).Any(line => line.IsMeasureLine && Math.Abs(line.Time - setup.Settings.EndTime) < 0.000001));
        AddAssert("end drag keeps the exact start", () => setup.Settings.StartTime, () => Is.EqualTo(selection.StartTime));
        AddAssert("chosen interval remains longer than sixty seconds", () => setup.Settings.EndTime - setup.Settings.StartTime, () => Is.GreaterThan(60000));
        AddAssert("long interval contains a dense real pattern", () => BmsPracticeBeatmap.SelectedCount(fullBeatmap, setup.Settings), () => Is.GreaterThan(200));
        AddStep("fit the long selection", () =>
        {
            selection = setup.Settings;
            clickRealChartControl(setup.ChildrenOfType<RoundedButton>().Single(button => button.Text.Equals(BmsStrings.PracticeFitRange)));
        });
        AddAssert("fit selection includes both boundaries", () => chart.Viewport.Start <= selection.StartTime && chart.Viewport.End >= chart.SelectedEnd);
        AddAssert("fit selection removes unused song edges", () => chart.Viewport.Duration, () => Is.LessThan(fullDuration));

        AddStep("press the long-range zoom control", () =>
        {
            InputManager.MoveMouseTo(zoom.ToScreenSpace(new Vector2(zoom.DrawWidth * 0.38f, zoom.DrawHeight / 2)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("zoom into a small part of the long range", () => InputManager.MoveMouseTo(zoom.ToScreenSpace(new Vector2(zoom.DrawWidth * 0.68f, zoom.DrawHeight / 2))));
        AddStep("release long-range zoom", () =>
        {
            InputManager.ReleaseButton(MouseButton.Left);
            zoomedDuration = chart.Viewport.Duration;
        });
        AddAssert("zoom reveals a small portion of the song", () => zoomedDuration, () => Is.LessThan(fullDuration * 0.2));
        AddAssert("zoom leaves the long selection intact", () => setup.Settings, () => Is.EqualTo(selection));
        AddAssert("offscreen boundary labels fit and stay separate", () => longRangeBoundaryLabelsFit(chart, setup.Settings));
        AddStep("locate a later pattern using the overview", () =>
        {
            InputManager.MoveMouseTo(longRangeOverviewPosition(chart, fullStart + fullDuration * 0.78));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("overview click centres the later song position", () => chart.Viewport.Start + chart.Viewport.Duration / 2,
            () => Is.EqualTo(fullStart + fullDuration * 0.78).Within(0.1));
        AddAssert("overview click preserves zoom", () => chart.Viewport.Duration, () => Is.EqualTo(zoomedDuration).Within(0.000001));
        AddStep("press an earlier part of the overview", () =>
        {
            InputManager.MoveMouseTo(longRangeOverviewPosition(chart, fullStart + fullDuration * 0.15));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag overview to the middle pattern", () => InputManager.MoveMouseTo(longRangeOverviewPosition(chart, fullStart + fullDuration * 0.35)));
        AddStep("release overview navigation", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("overview drag centres the requested song position", () => chart.Viewport.Start + chart.Viewport.Duration / 2,
            () => Is.EqualTo(fullStart + fullDuration * 0.35).Within(0.1));
        AddAssert("overview navigation preserves all practice settings", () => setup.Settings, () => Is.EqualTo(selection));
        AddStep("restore the long-range view", () => clickRealChartControl(setup.ChildrenOfType<RoundedButton>()
            .Single(button => button.Text.Equals(BmsStrings.PracticeFitRange))));
        AddAssert("fit selection restores both offscreen boundaries", () => chart.Viewport.Start <= selection.StartTime && chart.Viewport.End >= chart.SelectedEnd);
        AddStep("return to the complete song view", () => clickRealChartControl(setup.ChildrenOfType<RoundedButton>()
            .Single(button => button.Text.Equals(BmsStrings.PracticeFullChart))));
        AddAssert("whole-song browsing retains the long interval", () => setup.Settings == selection
            && chart.Viewport.Start == fullStart && chart.Viewport.End == fullEnd);
        AddStep("start the long interval once", () => clickRealChartControl(setup.StartButton));
        AddAssert("one long-range session starts immediately", () => starts == 1 && started != null);
        AddAssert("session keeps exact long-range and repeat settings", () => started!.Settings, () => Is.EqualTo(selection));
        AddAssert("session retains the original real song", () => started!.Source, () => Is.SameAs(source));
        AddAssert("setup closes after starting the long range", () => setup.State.Value, () => Is.EqualTo(Visibility.Hidden));
    }

    private static string longRangePracticeSongsRoot([CallerFilePath] string sourceFile = "")
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "bms_test_songs"));
        return Directory.Exists(sourceRoot) ? sourceRoot : BmsEmbeddedSongDecoderTest.TestSongsRoot;
    }

    private static Vector2 longRangeOverviewPosition(BmsPracticeChart chart, double time)
        => chart.ToScreenSpace(chart.MiniaturePositionAt(time));

    private static bool longRangeBoundaryLabelsFit(BmsPracticeChart chart, BmsPracticeSettings settings)
    {
        var labels = chart.ChildrenOfType<OsuSpriteText>().Where(label => label.Text.Equals(BmsStrings.PracticeStartLine)
            || label.Text.Equals(BmsStrings.PracticeEndLine)).ToArray();
        if (labels.Length != 2 || labels.Any(label => !chart.ScreenSpaceDrawQuad.AABBFloat.Contains(label.ScreenSpaceDrawQuad.AABBFloat)))
            return false;
        var first = labels[0].ScreenSpaceDrawQuad.AABBFloat;
        var second = labels[1].ScreenSpaceDrawQuad.AABBFloat;
        return first.Bottom <= second.Top || second.Bottom <= first.Top;
    }
}
