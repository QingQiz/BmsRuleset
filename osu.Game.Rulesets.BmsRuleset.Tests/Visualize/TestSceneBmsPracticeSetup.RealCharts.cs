#nullable enable
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
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
    public void RealChartRangeControlsAndStart(string directory, string filename, int width, int height)
    {
        BmsPracticeSetupOverlay setup = null!;
        Container viewport = null!;
        TestWorkingBeatmap source = null!;
        BmsBeatmap fullBeatmap = null!;
        BmsPracticeSession? started = null;
        BmsPracticeSettings selection = null!;
        RoundedSliderBar<double> zoom = null!;
        OsuDropdown<int> snap = null!;
        var starts = 0;
        var duration = 0d;
        var target = 0d;
        var unchangedBoundary = 0d;

        AddStep("open real chart practice setup", () =>
        {
            var path = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, directory, filename);
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
                // Logical viewport sizes exercise each layout while keeping every input inside the test scene.
                Scale = new Vector2(0.5f),
                Child = setup = new BmsPracticeSetupOverlay(source, [], new BmsPracticeSettings
                {
                    StartTime = 45000,
                    EndTime = 53000,
                    SkipResults = true,
                    CountIn = 3,
                })
                {
                    StartRequested = session => { started = session; starts++; },
                },
            };
            setup.Show();
        });
        AddUntilStep("real chart and controls ready", () => setup.IsReady && setup.SelectionChart.DrawHeight > 100);
        AddStep("get chart timing and controls", () =>
        {
            fullBeatmap = BmsPracticeSession.GetFullBeatmap(source, setup.SelectedMods);
            zoom = setup.ChildrenOfType<RoundedSliderBar<double>>().Single();
            snap = setup.ChildrenOfType<OsuDropdown<int>>().Single(dropdown => dropdown.Items.SequenceEqual([0, 4, 8, 12, 16, 24, 32, 48, 64]));
            selection = setup.Settings;
            duration = setup.SelectionChart.Viewport.Duration;
        });
        AddAssert("real selection contains playable notes", () => BmsPracticeBeatmap.SelectedCount(fullBeatmap, selection), () => Is.GreaterThan(20));
        AddAssert("start button is inside the viewport", () => viewport.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat));
        AddAssert("later time is above earlier time", () => realChartPosition(setup.SelectionChart, selection.EndTime).Y
            < realChartPosition(setup.SelectionChart, selection.StartTime).Y);

        AddStep("disable count-in", () => clickRealChartControl(setup.CountInButtons[0]));
        AddAssert("count-in is disabled", () => setup.Settings.CountIn, () => Is.Zero);
        AddStep("select five-second count-in", () => clickRealChartControl(setup.CountInButtons[5]));
        AddAssert("count-in is five seconds", () => setup.Settings.CountIn, () => Is.EqualTo(5));
        AddStep("disable looping", () => clickRealChartControl(setup.ChildrenOfType<OsuCheckbox>().Single()));
        AddAssert("loop is disabled", () => !setup.Settings.SkipResults);
        AddStep("enable looping", () => clickRealChartControl(setup.ChildrenOfType<OsuCheckbox>().Single()));
        AddAssert("loop is enabled", () => setup.Settings.SkipResults);

        AddStep("press zoom slider", () =>
        {
            InputManager.MoveMouseTo(zoom.ToScreenSpace(new Vector2(zoom.DrawWidth * 0.35f, zoom.DrawHeight / 2)));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag zoom towards the right", () => InputManager.MoveMouseTo(zoom.ToScreenSpace(new Vector2(zoom.DrawWidth * 0.98f, zoom.DrawHeight / 2))));
        AddStep("release zoom slider", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("rightward drag zooms in", () => setup.SelectionChart.Viewport.Duration, () => Is.LessThan(duration));
        AddAssert("zoom preserves range", () => setup.Settings.StartTime == selection.StartTime && setup.Settings.EndTime == selection.EndTime);
        AddStep("fit selected range", () => clickRealChartControl(setup.ChildrenOfType<RoundedButton>()
            .Single(button => button.Text.Equals(BmsStrings.PracticeFitRange))));
        AddUntilStep("range is visible again", () => setup.SelectionChart.Viewport.Start <= setup.Settings.StartTime
            && setup.SelectionChart.Viewport.End >= setup.Settings.EndTime);

        AddStep("press start boundary", () =>
        {
            unchangedBoundary = setup.Settings.EndTime;
            target = setup.Settings.StartTime + 1370;
            InputManager.MoveMouseTo(realChartPosition(setup.SelectionChart, setup.Settings.StartTime));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag start upwards", () => InputManager.MoveMouseTo(realChartPosition(setup.SelectionChart, target)));
        AddStep("release start boundary", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("start moved to a real sixteenth-note grid line", () => setup.Settings.StartTime > selection.StartTime
            && BmsPracticeGrid.Create(fullBeatmap.TimingMap, 16).Any(line => Math.Abs(line.Time - setup.Settings.StartTime) < 0.000001));
        AddAssert("moving start preserves end", () => setup.Settings.EndTime, () => Is.EqualTo(unchangedBoundary));

        AddStep("open snap choices", () => clickRealChartControl(snap.ChildrenOfType<DropdownHeader>().Single()));
        AddUntilStep("measure choice is visible", () => snap.ChildrenOfType<Menu.DrawableMenuItem>()
            .Any(item => item.IsPresent && item.Item is DropdownMenuItem<int> { Value: 0 }));
        AddStep("choose measure alignment", () => clickRealChartControl(snap.ChildrenOfType<Menu.DrawableMenuItem>()
            .Single(item => item.Item is DropdownMenuItem<int> { Value: 0 })));
        AddAssert("measure snapping selected", () => setup.SelectionChart.SnapDivisor, () => Is.Zero);
        AddStep("press end boundary", () =>
        {
            unchangedBoundary = setup.Settings.StartTime;
            target = setup.Settings.EndTime - 1370;
            InputManager.MoveMouseTo(realChartPosition(setup.SelectionChart, setup.Settings.EndTime));
            InputManager.PressButton(MouseButton.Left);
        });
        AddStep("drag end downwards", () => InputManager.MoveMouseTo(realChartPosition(setup.SelectionChart, target)));
        AddStep("release end boundary", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("end moved to a real measure line", () => setup.Settings.EndTime < selection.EndTime
            && BmsPracticeGrid.Create(fullBeatmap.TimingMap, 0).Any(line => line.IsMeasureLine && Math.Abs(line.Time - setup.Settings.EndTime) < 0.000001));
        AddAssert("moving end preserves start", () => setup.Settings.StartTime, () => Is.EqualTo(unchangedBoundary));

        AddStep("remember range and resize", () =>
        {
            selection = setup.Settings;
            viewport.Size = width == 760 ? new Vector2(1280, 720) : new Vector2(760, 900);
        });
        AddUntilStep("resized controls remain inside setup", () => setup.ScreenSpaceDrawQuad.AABBFloat.Contains(setup.StartButton.ScreenSpaceDrawQuad.AABBFloat)
            && setup.SelectionChart.DrawHeight > 100);
        AddAssert("resizing preserves all practice settings", () => setup.Settings, () => Is.EqualTo(selection));
        AddStep("select rate mod", () => setup.ChildrenOfType<ModSelectOverlay>().Single().SelectedMods.Value =
            [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }]);
        AddUntilStep("mod conversion completes", () => setup.IsReady && setup.SelectedMods.OfType<BmsModHalfTime>().Any());
        AddAssert("rate mod preserves selected range", () => setup.Settings, () => Is.EqualTo(selection));
        AddStep("click start once", () => clickRealChartControl(setup.StartButton));
        AddAssert("start creates one session immediately", () => starts == 1 && started != null);
        AddAssert("session contains the selected settings", () => started!.Settings, () => Is.EqualTo(selection));
        AddAssert("session contains the selected rate", () => started!.Mods.OfType<BmsModHalfTime>().Single().SpeedChange.Value, () => Is.EqualTo(0.75));
        AddAssert("practice setup closes", () => setup.State.Value, () => Is.EqualTo(Visibility.Hidden));
    }

    private void clickRealChartControl(Drawable control)
    {
        InputManager.MoveMouseTo(control.ScreenSpaceDrawQuad.Centre);
        InputManager.Click(MouseButton.Left);
    }

    private static Vector2 realChartPosition(BmsPracticeChart chart, double time)
        => chart.ToScreenSpace(new Vector2((chart.TimelineLeft + chart.TimelineRight) / 2, 18 + (float)((chart.Viewport.End - time) / chart.Viewport.Duration) * Math.Max(1, chart.DrawHeight - 36)));
}
