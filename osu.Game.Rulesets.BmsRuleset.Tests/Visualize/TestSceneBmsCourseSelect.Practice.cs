using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Tests.Beatmaps;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase("osu", 0, 7, false)]
    [TestCase("taiko", 1, 7, false)]
    [TestCase("fruits", 2, 7, false)]
    [TestCase("mania", 3, 4, false)]
    [TestCase("mania", 3, 7, true)]
    [TestCase("mania", 3, 9, false)]
    public void TestPracticeEntryTracksChartCompatibility(string sourceRuleset, int onlineID, int keyCount, bool supported)
    {
        BeatmapInfo foreignChart = null!;
        WorkingBeatmap nativeChart = null!;
        WorkingBeatmap foreignWorking = null!;
        BmsPracticeFooterButton practiceButton = null!;

        AddStep("enter song select with foreign chart selected", () =>
        {
            config.SetValue(OsuSetting.ShowConvertedBeatmaps, true);
            var source = Ruleset.Value.Clone();
            source.ShortName = sourceRuleset;
            source.OnlineID = onlineID;
            Realm.Write(realm => realm.Add(source, update: true));
            var set = createBeatmapSet(source);
            set.Beatmaps.Single().Difficulty.CircleSize = keyCount;
            foreignChart = beatmaps.Import(set)!.Value.Beatmaps.Single().Detach();
            foreignWorking = new TestWorkingBeatmap(new Beatmap
            {
                BeatmapInfo = foreignChart,
                HitObjects = [new PracticeEntryManiaNote { StartTime = 1000 }, new PracticeEntryManiaNote { StartTime = 3000 }],
            }, audioManager: Audio);
            Beatmap.Value = foreignWorking;
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("capture practice button", () => practiceButton = this.ChildrenOfType<BmsPracticeFooterButton>().Single());
        AddUntilStep("footer matches chart compatibility", () => practiceButton.Enabled.Value == supported
            && (supported ? practiceButton.Width > 100 && practiceButton.Alpha > 0.99f : practiceButton.Width == 0 && practiceButton.Alpha == 0));
        AddAssert("context menu matches chart compatibility", () => songSelect.GetForwardActions(foreignChart)
            .Any(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()), () => Is.EqualTo(supported));
        if (onlineID == 0)
            AddAssert("unsupported standard chart remains selected on entry", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(foreignChart.Hash));
        if (!supported)
        {
            AddStep("hidden practice button cannot open setup", () => practiceButton.TriggerClick());
            AddAssert("no practice setup created", () => songSelect.ChildrenOfType<BmsPracticeSetupOverlay>(), () => Is.Empty);
        }
        AddStep("select native BMS chart", () =>
        {
            var nativeInfo = beatmaps.Import(createBeatmapSet(Ruleset.Value))!.Value.Beatmaps.Single().Detach();
            Beatmap.Value = nativeChart = beatmaps.GetWorkingBeatmap(nativeInfo);
        });
        AddUntilStep("practice returns for native BMS", () => practiceButton.Enabled.Value && practiceButton.Width > 100 && practiceButton.Alpha > 0.99f);
        AddAssert("native chart menu includes practice", () => songSelect.GetForwardActions(nativeChart.BeatmapInfo)
            .Any(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()));
        if (onlineID == 0 || supported)
        {
            AddStep("switch back to foreign chart", () => Beatmap.Value = foreignWorking);
            AddUntilStep("practice updates after switching back", () => practiceButton.Enabled.Value == supported
                && (supported ? practiceButton.Width > 100 && practiceButton.Alpha > 0.99f : practiceButton.Width == 0 && practiceButton.Alpha == 0));
        }
    }

    [Test]
    public void TestPracticeEntryHiddenWithoutSelectedChart()
    {
        AddStep("open empty song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("empty song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddUntilStep("practice occupies no footer space", () => this.ChildrenOfType<BmsPracticeFooterButton>().Single()
            is { Enabled.Value: false, Width: 0, Alpha: 0 });
    }

    private class PracticeEntryManiaNote : HitObject, IHasColumn
    {
        public int Column { get; set; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestPracticeSetupCancelKeepsSongSelectionAndMods(bool useContextMenu)
    {
        var directory = string.Empty;
        Task import = null!;
        BeatmapInfo chart = null!;
        BmsPracticeSetupOverlay setup = null!;
        var originalMod = new BmsModDoubleTime();
        originalMod.SpeedChange.Value = 1.25;
        var savedStart = 0d;
        var savedDuration = 0d;

        AddStep("import practice chart", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-selection-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "practice.bms"), """
                #PLAYER 1
                #TITLE Practice Selection
                #ARTIST Test
                #BPM 120
                #PLAYLEVEL 1
                #00011:0001
                #00112:0101
                #00251:01
                #00451:01
                #00518:01
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("chart imported", () => import.IsCompleted);
        AddStep("select imported chart", () =>
        {
            import.GetAwaiter().GetResult();
            chart = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(info => info.Metadata.Source == directory).Detach());
            Beatmap.Value = beatmaps.GetWorkingBeatmap(chart);
            SelectedMods.Value = [originalMod];
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddUntilStep("practice footer available", () => this.ChildrenOfType<BmsPracticeFooterButton>().SingleOrDefault() is { Enabled.Value: true, Alpha: > 0.9f });
        AddStep("open practice", () =>
        {
            if (useContextMenu)
                songSelect.GetForwardActions(chart).Single(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()).Action.Value();
            else
            {
                InputManager.MoveMouseTo(this.ChildrenOfType<BmsPracticeFooterButton>().Single());
                InputManager.Click(MouseButton.Left);
            }
        });
        AddUntilStep("practice chart conversion ready", () =>
        {
            setup = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().SingleOrDefault()!;
            return setup?.IsReady == true;
        });
        AddAssert("local mods are deep copies", () => setup.SelectedMods.OfType<BmsModDoubleTime>().Single(), () => Is.Not.SameAs(originalMod));
        AddStep("change local rate", () =>
        {
            var selected = setup.ModSelection;
            var halfTime = new BmsModHalfTime();
            halfTime.SpeedChange.Value = 0.75;
            selected.SelectedMods.Value = [halfTime];
        });
        AddUntilStep("new local chart ready", () => setup.IsReady && setup.SelectedMods.OfType<BmsModHalfTime>().Any());
        AddAssert("song select retains its rate", () => songSelect.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddStep("adjust visible range", () =>
        {
            var selection = setup.Settings;
            setup.SelectionChart.Zoom(0.3);
            setup.SelectionChart.Viewport.Pan(1000);
            savedStart = setup.SelectionChart.Viewport.Start;
            savedDuration = setup.SelectionChart.Viewport.Duration;
            Assert.That(setup.Settings, Is.EqualTo(selection));
        });
        AddAssert("start action is available directly from range selection", () => setup.StartButton.Text.ToString() == BmsStrings.PracticeBegin.ToString());
        AddAssert("selection view restored", () => setup.SelectionChart.Viewport.Start == savedStart && setup.SelectionChart.Viewport.Duration == savedDuration);
        AddStep("cancel practice", () => InputManager.Key(Key.Escape));
        AddUntilStep("practice closed", () => setup.State.Value == Visibility.Hidden);
        AddAssert("song selection retained", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(chart.Hash));
        AddAssert("original mods retained after cancel", () => songSelect.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddAssert("cancel creates no player", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
    }

    [Test]
    public void TestPracticeEntryUnavailableInCourseMode()
    {
        BeatmapInfo chart = null!;
        AddStep("load song select", () =>
        {
            var ruleset = rulesets.AvailableRulesets.Single(info => info.ShortName == Constant.SHORT_NAME);
            chart = beatmaps.Import(createBeatmapSet(ruleset))!.Value.Beatmaps.Single().Detach();
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddAssert("single chart menu has practice", () => songSelect.GetForwardActions(chart)
            .Any(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()));
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("practice footer hidden", () => this.ChildrenOfType<BmsPracticeFooterButton>().Single() is { Enabled.Value: false, Width: < 1 });
        AddAssert("course menu omits practice", () => songSelect.GetForwardActions(chart)
            .All(item => item.Text.Value.ToString() != BmsStrings.Practice.ToString()));
    }
}
