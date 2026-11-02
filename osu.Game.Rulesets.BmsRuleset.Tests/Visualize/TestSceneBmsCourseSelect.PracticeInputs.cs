using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [Test]
    public void TestPracticeTimeInputsPreserveGridPrecisionAndRecoverAfterModRefresh()
    {
        var directory = string.Empty;
        Task import = null!;
        BeatmapInfo chart = null!;
        BmsPracticeSetupOverlay setup = null!;
        OsuDropdown<int> snapInput = null!;
        var exactStart = 0d;
        var exactEnd = 0d;
        var measureStart = 0d;
        var measureDuration = 0d;

        void commitTime(OsuTextBox input, double time, bool free = false)
        {
            InputManager.MoveMouseTo(input.ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
            setup.SelectionChart.RangeChanged?.Invoke(exactStart, exactEnd);
            input.Text = BmsStrings.PracticeInputTime(time).ToString();
            if (free) InputManager.PressKey(Key.AltLeft);
            InputManager.Key(Key.Enter);
            if (free) InputManager.ReleaseKey(Key.AltLeft);
        }

        AddStep("import chart with fractional grid times", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-inputs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "practice.bms"), """
                #PLAYER 1
                #TITLE Practice Precision
                #ARTIST Test
                #BPM 130
                #00011:01
                #00112:01
                #00213:01
                #00814:01
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("chart imported", () => import.IsCompleted);
        AddStep("select chart", () =>
        {
            import.GetAwaiter().GetResult();
            chart = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(info => info.Metadata.Source == directory).Detach());
            Beatmap.Value = beatmaps.GetWorkingBeatmap(chart);
            Stack.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("open practice", () => songSelect.GetForwardActions(chart).Single(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()).Action.Value());
        AddUntilStep("practice ready", () =>
        {
            setup = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().SingleOrDefault()!;
            return setup?.IsReady == true;
        });
        AddStep("select exact musical grid boundaries", () =>
        {
            var full = BmsPracticeSession.GetFullBeatmap(songSelect.Beatmap.Value, setup.SelectedMods);
            var grid = BmsPracticeGrid.Create(full.TimingMap);
            exactStart = grid.Single(line => line.Measure == 0 && line.Subdivision == 1).Time;
            exactEnd = grid.Single(line => line.Measure == 2 && line.Subdivision == 3).Time;
            measureStart = grid.Single(line => line.Measure == 1 && line.IsMeasureLine).Time;
            measureDuration = grid.Single(line => line.Measure == 2 && line.IsMeasureLine).Time - measureStart;
            setup.SelectionChart.RangeChanged?.Invoke(exactStart, exactEnd);
        });
        AddStep("edit end field", () => setup.EndTimeInput.Text = BmsStrings.PracticeInputTime(exactEnd + 500).ToString());
        AddAssert("start retains all grid precision", () => setup.Settings.StartTime, () => Is.EqualTo(exactStart));
        AddStep("restore exact range then edit start", () =>
        {
            setup.SelectionChart.RangeChanged?.Invoke(exactStart, exactEnd);
            setup.StartTimeInput.Text = BmsStrings.PracticeInputTime(exactStart + 500).ToString();
        });
        AddAssert("end retains all grid precision", () => setup.Settings.EndTime, () => Is.EqualTo(exactEnd));
        AddStep("choose measure snapping", () =>
        {
            snapInput = setup.ChildrenOfType<OsuDropdown<int>>().Single(dropdown => dropdown.Items.SequenceEqual([0, 4, 8, 12, 16, 24, 32, 48, 64]));
            snapInput.Current.Value = 0;
        });
        AddAssert("selector uses measure grid", () => setup.SelectionChart.SnapDivisor == 0);
        AddStep("commit end time between measure lines", () => commitTime(setup.EndTimeInput, measureStart + measureDuration * 1.2));
        AddAssert("end snaps to nearest measure", () => setup.Settings.EndTime, () => Is.EqualTo(measureStart + measureDuration).Within(0.000001));
        AddAssert("committing end preserves exact start", () => setup.Settings.StartTime, () => Is.EqualTo(exactStart));
        AddStep("commit start time between measure lines", () => commitTime(setup.StartTimeInput, measureStart + measureDuration * 0.2));
        AddAssert("start snaps to nearest measure", () => setup.Settings.StartTime, () => Is.EqualTo(measureStart).Within(0.000001));
        AddAssert("committing start preserves exact end", () => setup.Settings.EndTime, () => Is.EqualTo(exactEnd));
        AddStep("choose twelfth-note snapping", () => snapInput.Current.Value = 12);
        AddStep("commit time near a triplet grid position", () => commitTime(setup.StartTimeInput, measureStart + measureDuration / 12 + 20));
        AddAssert("start uses twelfth-note division", () => setup.Settings.StartTime, () => Is.EqualTo(measureStart + measureDuration / 12).Within(0.000001));
        AddAssert("triplet commit preserves exact end", () => setup.Settings.EndTime, () => Is.EqualTo(exactEnd));
        AddStep("choose twenty-fourth-note snapping", () => snapInput.Current.Value = 24);
        AddStep("commit end near a smaller triplet position", () => commitTime(setup.EndTimeInput, measureStart + measureDuration * (1 + 5d / 24) + 15));
        AddAssert("end uses twenty-fourth-note division", () => setup.Settings.EndTime, () => Is.EqualTo(measureStart + measureDuration * (1 + 5d / 24)).Within(0.000001));
        AddAssert("smaller triplet commit preserves exact start", () => setup.Settings.StartTime, () => Is.EqualTo(exactStart));
        AddStep("commit free time with alt enter", () => commitTime(setup.StartTimeInput, 2222, true));
        AddAssert("alt enter preserves manually entered time", () => setup.Settings.StartTime, () => Is.EqualTo(2222).Within(0.000001));
        AddAssert("free time stays outside selected musical grid", () => Math.Abs(setup.Settings.StartTime - setup.SelectionChart.SnapTime(2222)) > 1);
        AddAssert("free commit preserves exact end", () => setup.Settings.EndTime, () => Is.EqualTo(exactEnd));
        AddStep("enter invalid time", () => setup.StartTimeInput.Text = "invalid");
        AddAssert("invalid text prevents start", () => !setup.IsReady);
        AddStep("refresh with a new rate mod", () => setup.ModSelection.SelectedMods.Value = [new BmsModHalfTime()]);
        AddUntilStep("fresh valid preview recovers", () => setup.IsReady && setup.SelectedMods.OfType<BmsModHalfTime>().Any());
        AddAssert("displayed times restored with validation", () => setup.StartTimeInput.Text != "invalid");
        AddStep("close practice", () => InputManager.Key(Key.Escape));
    }
}
