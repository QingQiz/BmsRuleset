using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    // ScreenTestScene otherwise exits directly, bypassing the route used by OsuGame's footer.
    protected override void BackButtonPressed()
    {
        if (Stack.CurrentScreen is OsuScreen { IsLoaded: true } current && (!current.AllowUserExit || current.OnBackButton()))
            return;
        base.BackButtonPressed();
    }

    private void pressNavigationBack(bool keyboard)
    {
        if (keyboard)
            InputManager.Key(Key.Escape);
        else
        {
            InputManager.MoveMouseTo(ScreenFooter.BackButton);
            InputManager.Click(MouseButton.Left);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestNavigationBackReturnsFromCoursesToSongsBeforeExiting(bool keyboard)
    {
        TestParentScreen parent = null!;
        BmsSoloSongSelect courseSelect = null!;
        WorkingBeatmap originalBeatmap = null!;
        AddStep("load parent", () => Stack.Push(parent = new TestParentScreen()));
        AddUntilStep("parent current", () => Stack.CurrentScreen == parent);
        AddStep("load song select", () =>
        {
            var info = beatmaps.Import(createBeatmapSet(Ruleset.Value))!.Value.Beatmaps.Single().Detach();
            originalBeatmap = beatmaps.GetWorkingBeatmap(info);
            Beatmap.Value = originalBeatmap;
            SelectedMods.Value = [new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }];
            parent.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("songs ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("enter course selection", () => controller.ToggleMode());
        AddUntilStep("courses ready", () => Stack.CurrentScreen is BmsSoloSongSelect current && current != songSelect
            && current.IsLoaded && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == true);
        AddStep("remember course screen", () => songSelect = courseSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddStep("return from courses", () => pressNavigationBack(keyboard));
        AddUntilStep("songs restored instead of parent", () => Stack.CurrentScreen is BmsSoloSongSelect current && current != courseSelect
            && current.IsLoaded && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == false);
        AddStep("capture restored songs", () => songSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddAssert("song restored", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(originalBeatmap.BeatmapInfo.Hash));
        AddAssert("mods restored", () => songSelect.Mods.Value, () => Has.Exactly(1).TypeOf<BmsModDoubleTime>());
        AddAssert("original rate restored", () => songSelect.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddStep("return from songs", () => pressNavigationBack(keyboard));
        AddUntilStep("parent restored on second back", () => Stack.CurrentScreen == parent);
        AddStep("leave parent", () => parent.Exit());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestNavigationCourseBackCancelsPendingStart(bool keyboard)
    {
        TestParentScreen parent = null!;
        BmsSongSelect courseSelect = null!;
        AddStep("load parent", () => Stack.Push(parent = new TestParentScreen()));
        AddUntilStep("parent current", () => Stack.CurrentScreen == parent);
        AddStep("prepare playable course", () =>
        {
            var info = beatmaps.Import(createBeatmapSet(Ruleset.Value))!.Value.Beatmaps.Single().Detach();
            Beatmap.Value = beatmaps.GetWorkingBeatmap(info);
            BmsRulesetRuntime.CourseCatalog.Replace(
            [
                new BmsCourseDefinition("navigation", "Table", "Navigation", [new BmsCourseStage("Stage", "1", BeatmapHash: info.Hash)], []),
            ]);
            parent.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("songs ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("enter courses", () => controller.ToggleMode());
        AddUntilStep("courses ready", () => Stack.CurrentScreen is BmsSoloSongSelect current && current != songSelect && current.IsLoaded
            && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.SelectedCourse?.Id == "navigation");
        AddStep("start course then return before lookup finishes", () =>
        {
            courseSelect = songSelect = (BmsSoloSongSelect)Stack.CurrentScreen;
            controller.StartCourse((BmsSoloSongSelect)songSelect);
            pressNavigationBack(keyboard);
        });
        AddUntilStep("songs restored", () => Stack.CurrentScreen is BmsSoloSongSelect current && current != courseSelect && current.IsLoaded
            && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == false);
        AddStep("remember restored songs", () => songSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddWaitStep("cancelled lookup settles", 10);
        AddAssert("old start cannot push gameplay", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
        AddAssert("stale back cannot affect replacement", () => courseSelect.OnBackButton() && Stack.CurrentScreen == songSelect);
        AddStep("return from songs", () => pressNavigationBack(keyboard));
        AddUntilStep("parent restored", () => Stack.CurrentScreen == parent);
        AddStep("leave parent", () => parent.Exit());
    }

    [Test]
    public void TestNavigationPracticeModsAndBackShareOneModalStack([Values] bool footerEntry, [Values] bool keyboardBack, [Values] bool hasRandomBranches)
    {
        TestParentScreen parent = null!;
        BmsPracticeSetupOverlay setup = null!;
        BeatmapInfo chart = null!;
        FooterButtonMods footerMods = null!;
        BmsPracticeFooterButton practiceFooter = null!;
        var originalPracticeX = 0f;
        Colour4 inactiveColour = default;
        var branchDecisions = string.Empty;
        var directory = string.Empty;
        Task import = null!;

        void openPractice() => songSelect.GetForwardActions(chart).Single(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()).Action.Value();

        void openMods(bool footer)
        {
            if (footer)
            {
                InputManager.MoveMouseTo(footerMods);
                InputManager.Click(MouseButton.Left);
            }
            else
                InputManager.Key(Key.F1);
        }

        AddStep("import practice chart", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-navigation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var branchCommand = hasRandomBranches ? "#RANDOM 2" : "#SETRANDOM 1";
            File.WriteAllText(Path.Combine(directory, "chart.bms"), $"""
                #PLAYER 1
                #TITLE Practice Navigation
                #ARTIST Test
                #BPM 120
                #00011:01
                {branchCommand}
                #IF 1
                #00112:01
                #ELSE
                #00114:01
                #ENDIF
                #ENDRANDOM
                #00813:01
                """);
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
            Stack.Push(parent = new TestParentScreen());
        });
        AddUntilStep("chart imported and parent current", () => import.IsCompleted && Stack.CurrentScreen == parent);
        AddStep("open songs", () =>
        {
            import.GetAwaiter().GetResult();
            chart = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(info => info.Metadata.Source == directory).Detach());
            Beatmap.Value = beatmaps.GetWorkingBeatmap(chart);
            SelectedMods.Value = [new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }];
            parent.Push(songSelect = new BmsSoloSongSelect());
        });
        AddUntilStep("songs ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("find footer buttons", () =>
        {
            footerMods = this.ChildrenOfType<FooterButtonMods>().Single(button => button.State.Value == Visibility.Visible);
            practiceFooter = this.ChildrenOfType<BmsPracticeFooterButton>().Single();
            InputManager.MoveMouseTo(songSelect.ScreenSpaceDrawQuad.Centre);
        });
        AddUntilStep("initial carousel selection settled", () => !songSelect.IsFiltering && carousel.MatchedBeatmapsCount == 1
            && carousel.CurrentBeatmap?.Hash == chart.Hash);
        AddUntilStep("footer layout settled", () => practiceFooter.Alpha == 1 && !practiceFooter.Transforms.Any() && !footerMods.Transforms.Any()
            && !practiceFooter.ChildrenOfType<Box>().First().Transforms.Any());
        AddStep("remember closed button state and position", () =>
        {
            Assert.That(practiceFooter.OverlayState.Value, Is.EqualTo(Visibility.Hidden));
            originalPracticeX = practiceFooter.ScreenSpaceDrawQuad.AABBFloat.Left;
            inactiveColour = practiceFooter.ChildrenOfType<Box>().First().Colour.AverageColour;
            var buttons = practiceFooter.Parent!.ChildrenOfType<ScreenFooterButton>().ToArray();
            Assert.That(buttons.Take(3).Select(button => button.GetType()),
                Is.EqualTo(new[] { typeof(FooterButtonMods), typeof(BmsPracticeFooterButton), typeof(BmsCourseFooterButton) }));
        });
        AddStep("open practice settings", openPractice);
        AddUntilStep("practice ready", () =>
        {
            setup = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().SingleOrDefault()!;
            return setup?.IsReady == true && setup.State.Value == Visibility.Visible;
        });
        AddAssert("practice footer excludes system mods", () => footerMods.Mods.Value.All(mod => mod.Type != ModType.System));
        AddAssert("practice mod selection excludes system mods", () => setup.ModSelection.SelectedMods.Value.All(mod => mod.Type != ModType.System));
        AddStep("remember optional branch state", () =>
        {
            Assert.That(setup.SelectedMods.OfType<BmsModBranchReplay>().Count(), Is.EqualTo(hasRandomBranches ? 1 : 0));
            branchDecisions = setup.SelectedMods.OfType<BmsModBranchReplay>().SingleOrDefault()?.Decisions.Value ?? string.Empty;
        });
        AddAssert("practice footer icons exclude branch state", () => footerMods.ChildrenOfType<osu.Game.Rulesets.UI.ModIcon>()
            .All(icon => icon.Mod is not BmsModBranchReplay));
        AddUntilStep("unrelated buttons hidden", () => this.ChildrenOfType<ScreenFooterButton>()
            .Where(button => button is not FooterButtonMods and not BmsPracticeFooterButton).All(button => !button.IsPresent && !button.Enabled.Value));
        AddAssert("practice stays enabled in the same position", () => practiceFooter.Enabled.Value && practiceFooter.IsPresent
            && Math.Abs(practiceFooter.ScreenSpaceDrawQuad.AABBFloat.Left - originalPracticeX) < 0.1f);
        AddAssert("practice button reflects open state", () => practiceFooter.OverlayState.Value, () => Is.EqualTo(Visibility.Visible));
        AddUntilStep("practice button changes colour", () => !practiceFooter.ChildrenOfType<Box>().First().Colour.AverageColour.Equals(inactiveColour));
        AddAssert("settings only retain their fixed start action", () => ((Container)setup.StartButton.Parent!).Children.OfType<RoundedButton>().Count(), () => Is.EqualTo(1));
        AddStep("inspect modal footer and open mods", () =>
        {
            footerMods = this.ChildrenOfType<FooterButtonMods>().Single(button => button.State.Value == Visibility.Visible);
            Assert.That(this.ChildrenOfType<FooterButtonRandom>().Single().Enabled.Value, Is.False);
            Assert.That(this.ChildrenOfType<BmsCourseFooterButton>().Single().Enabled.Value, Is.False);
            openMods(footerEntry);
        });
        AddUntilStep("mod overlay above practice", () => ScreenFooter.ActiveOverlay == setup.ModSelection
            && setup.ModSelection.State.Value == Visibility.Visible);
        AddAssert("both entrances share one overlay", () => setup.ModSelection, () => Is.SameAs(songSelect.ModSelectOverlay));
        AddAssert("mod overlay is a higher sibling than practice", () => setup.ModSelection.Parent == setup.Parent && setup.ModSelection.Depth < setup.Depth);
        AddStep("change practice rate", () => setup.ModSelection.SelectedMods.Value = [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }]);
        AddUntilStep("local chart updated", () => setup.IsReady && setup.SelectedMods.OfType<BmsModHalfTime>().Any());
        AddAssert("changing local mods preserves optional branch state", () => setup.SelectedMods.OfType<BmsModBranchReplay>()
            .SingleOrDefault()?.Decisions.Value ?? string.Empty, () => Is.EqualTo(branchDecisions));
        AddAssert("changing local mods keeps system state out of the footer", () => footerMods.Mods.Value.Select(mod => mod.GetType()),
            () => Is.EqualTo(new[] { typeof(BmsModHalfTime) }));
        AddAssert("footer displays practice mods", () => footerMods.Mods.Value.OfType<BmsModHalfTime>().Single().SpeedChange.Value, () => Is.EqualTo(0.75));
        AddAssert("song mods remain separate", () => songSelect.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddStep("back closes mods first", () => pressNavigationBack(keyboardBack));
        AddUntilStep("mods closed but settings retained", () => ScreenFooter.ActiveOverlay == null && setup.ModSelection.State.Value == Visibility.Hidden
            && setup.State.Value == Visibility.Visible && Stack.CurrentScreen == songSelect);
        AddStep("open mods from other entrance", () => openMods(!footerEntry));
        AddUntilStep("same mods reopen", () => ScreenFooter.ActiveOverlay == setup.ModSelection && setup.ModSelection.State.Value == Visibility.Visible);
        AddAssert("local rate retained", () => setup.ModSelection.SelectedMods.Value.OfType<BmsModHalfTime>().Single().SpeedChange.Value, () => Is.EqualTo(0.75));
        AddStep("close mods using F1", () => InputManager.Key(Key.F1));
        AddUntilStep("settings remain current", () => ScreenFooter.ActiveOverlay == null && setup.State.Value == Visibility.Visible);
        AddStep("clear local mods from footer", () =>
        {
            InputManager.MoveMouseTo(footerMods);
            InputManager.Click(MouseButton.Right);
        });
        AddUntilStep("local mods cleared and chart ready", () => setup.IsReady && setup.SelectedMods.All(mod => mod is BmsModBranchReplay));
        AddAssert("clearing mods preserves optional branch state", () => setup.SelectedMods.OfType<BmsModBranchReplay>()
            .SingleOrDefault()?.Decisions.Value ?? string.Empty, () => Is.EqualTo(branchDecisions));
        AddAssert("branchless chart stays without branch mod", () => setup.SelectedMods.OfType<BmsModBranchReplay>().Count(),
            () => Is.EqualTo(hasRandomBranches ? 1 : 0));
        AddAssert("cleared footer has no mods", () => footerMods.Mods.Value, () => Is.Empty);
        AddAssert("clearing local mods preserves song rate", () => songSelect.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddStep("open mods using F1", () => InputManager.Key(Key.F1));
        AddUntilStep("F1 opens same overlay", () => ScreenFooter.ActiveOverlay == setup.ModSelection);
        AddStep("close mods from other back action", () => pressNavigationBack(!keyboardBack));
        AddUntilStep("only settings remain", () => ScreenFooter.ActiveOverlay == null && setup.State.Value == Visibility.Visible);
        AddStep("back closes settings", () => pressNavigationBack(keyboardBack));
        AddUntilStep("settings closed on songs", () => setup.State.Value == Visibility.Hidden && Stack.CurrentScreen == songSelect);
        AddAssert("original mod restored in footer", () => footerMods.Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddAssert("random and course controls restored", () => this.ChildrenOfType<FooterButtonRandom>().Single().Enabled.Value
            && this.ChildrenOfType<BmsCourseFooterButton>().Single().Enabled.Value);
        AddStep("reopen practice", openPractice);
        AddUntilStep("fresh practice ready", () =>
        {
            var current = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().Single();
            if (current == setup || !current.IsReady)
                return false;
            setup = current;
            return true;
        });
        AddAssert("cancelled local mods did not leak", () => setup.SelectedMods.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        AddStep("close by clicking active practice button", () =>
        {
            InputManager.MoveMouseTo(practiceFooter);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("cancel restored songs", () => setup.State.Value == Visibility.Hidden && Stack.CurrentScreen == songSelect);
        AddAssert("practice toggle returns to closed state", () => practiceFooter.OverlayState.Value, () => Is.EqualTo(Visibility.Hidden));
        AddStep("reopen at the same position before footer fade completes", () => InputManager.Click(MouseButton.Left));
        AddUntilStep("footer toggle opens settings", () => practiceFooter.OverlayState.Value == Visibility.Visible
            && songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().Single().State.Value == Visibility.Visible);
        AddStep("close again at the same practice button", () => InputManager.Click(MouseButton.Left));
        AddUntilStep("footer toggle closes settings", () => practiceFooter.OverlayState.Value == Visibility.Hidden
            && songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().Single().State.Value == Visibility.Hidden);
        AddUntilStep("other footer buttons fully return after rapid toggling", () => this.ChildrenOfType<FooterButtonRandom>().Single().Alpha == 1
            && this.ChildrenOfType<BmsCourseFooterButton>().Single().Alpha == 1);
        AddStep("back now exits song select", () => pressNavigationBack(keyboardBack));
        AddUntilStep("parent current", () => Stack.CurrentScreen == parent);
        AddAssert("no active mod footer remains", () => ScreenFooter.ActiveOverlay, () => Is.Null);
        AddStep("leave parent", () => parent.Exit());
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TestNavigationPracticeReleasesModOverlayWhenLeavingScreen(bool exitScreen, bool switchRuleset)
    {
        TestParentScreen parent = null!;
        TestParentScreen child = null!;
        BmsPracticeScreen practice = null!;
        RulesetInfo originalRuleset = null!;
        ToolbarRulesetSelector rulesetSelector = null!;
        BmsPracticeSetupOverlay setup = null!;
        BeatmapInfo chart = null!;
        Task import = null!;
        var directory = string.Empty;

        AddStep("import chart", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"practice-leaving-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "chart.bms"), "#PLAYER 1\n#TITLE Practice Leaving\n#BPM 120\n#00011:01\n#00112:01\n#00813:01\n");
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
            Stack.Push(parent = new TestParentScreen());
        });
        AddUntilStep("import complete", () => import.IsCompleted && Stack.CurrentScreen == parent);
        AddStep("open songs", () =>
        {
            import.GetAwaiter().GetResult();
            chart = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable().Single(info => info.Metadata.Source == directory).Detach());
            Beatmap.Value = beatmaps.GetWorkingBeatmap(chart);
            SelectedMods.Value = [new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }];
            parent.Push(songSelect = new BmsSoloSongSelect());
            BmsSongSelectEntryPatcher.TrackRulesetChanges(songSelect);
        });
        AddUntilStep("songs ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("open practice", () => songSelect.GetForwardActions(chart)
            .Single(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()).Action.Value());
        AddUntilStep("practice ready", () =>
        {
            setup = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().SingleOrDefault()!;
            return setup?.IsReady == true;
        });
        AddStep("open local mods", () =>
        {
            setup.ModSelection.SelectedMods.Value = [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }];
            InputManager.Key(Key.F1);
        });
        AddUntilStep("mod footer active", () => ScreenFooter.ActiveOverlay == setup.ModSelection);
        AddStep("leave while mods are open", () =>
        {
            if (switchRuleset)
            {
                originalRuleset = globalRuleset.Value;
                rulesetSelector = this.ChildrenOfType<ToolbarRulesetSelector>().Single();
                rulesetSelector.Current.UnbindBindings();
                globalRuleset.Value = new AlternateTestRuleset().RulesetInfo;
            }
            else if (exitScreen)
                songSelect.Exit();
            else
                songSelect.Push(child = new TestParentScreen());
        });
        AddUntilStep("destination entered", () => switchRuleset
            ? Stack.CurrentScreen is SoloSongSelect { IsLoaded: true }
            : Stack.CurrentScreen == (exitScreen ? parent : child));
        AddAssert("practice and mods are closed", () => setup.State.Value == Visibility.Hidden && setup.ModSelection.State.Value == Visibility.Hidden);
        AddAssert("mod footer released", () => ScreenFooter.ActiveOverlay, () => Is.Null);
        AddAssert("original rate preserved", () => SelectedMods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
        if (switchRuleset)
        {
            AddStep("switch back to BMS", () => globalRuleset.Value = originalRuleset);
            AddUntilStep("new BMS song select ready", () => Stack.CurrentScreen is BmsSoloSongSelect { IsLoaded: true } current && current != songSelect);
            AddStep("restore toolbar and capture screen", () =>
            {
                rulesetSelector.Current.BindTo(globalRuleset);
                songSelect = (BmsSoloSongSelect)Stack.CurrentScreen;
            });
            AddAssert("old settings did not reopen", () => songSelect.ChildrenOfType<BmsPracticeSetupOverlay>(), () => Is.Empty);
            AddStep("back exits replacement songs", () => pressNavigationBack(false));
            AddUntilStep("parent restored", () => Stack.CurrentScreen == parent);
        }
        if (!exitScreen)
        {
            AddStep("return from child", () => child.Exit());
            AddUntilStep("songs resumed", () => Stack.CurrentScreen == songSelect);
            AddAssert("footer uses original mods", () => this.ChildrenOfType<FooterButtonMods>().Single(button => button.IsPresent)
                .Mods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
            AddStep("reopen practice after suspension", () => songSelect.GetForwardActions(chart)
                .Single(item => item.Text.Value.ToString() == BmsStrings.Practice.ToString()).Action.Value());
            AddUntilStep("new practice ready", () =>
            {
                setup = songSelect.ChildrenOfType<BmsPracticeSetupOverlay>().Single();
                return setup.IsReady && setup.State.Value == Visibility.Visible;
            });
            AddStep("prepare a local range and rate", () =>
            {
                setup.SelectionChart.RangeChanged?.Invoke(1000, 5000);
                setup.SkipResultsInput.Current.Value = false;
                setup.ModSelection.SelectedMods.Value = [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }];
            });
            AddUntilStep("local conversion ready", () => setup.IsReady && setup.SelectedMods.OfType<BmsModHalfTime>().Any());
            AddStep("start from settings", () => setup.StartButton.TriggerClick());
            AddUntilStep("practice gameplay starts", () =>
            {
                practice = songSelect.GetChildScreen() as BmsPracticeScreen;
                return practice != null && Stack.CurrentScreen is BmsPracticePlayer { HasAttemptStarted: true };
            });
            AddStep("finish selected range", () => ((Drawable)Stack.CurrentScreen).ChildrenOfType<GameplayClockContainer>().Single().Seek(5500));
            AddUntilStep("practice result opens", () => Stack.CurrentScreen == practice && practice.LastResult != null);
            AddStep("back returns from result to settings", () => pressNavigationBack(true));
            AddUntilStep("same settings reopen", () => Stack.CurrentScreen == songSelect && setup.State.Value == Visibility.Visible && setup.IsReady);
            AddAssert("range and local rate survive return", () => setup.Settings.StartTime == 1000 && setup.Settings.EndTime == 5000
                && setup.SelectedMods.OfType<BmsModHalfTime>().Single().SpeedChange.Value == 0.75);
            AddAssert("resumed footer uses practice mods", () => this.ChildrenOfType<FooterButtonMods>().Single(button => button.IsPresent)
                .Mods.Value.OfType<BmsModHalfTime>().Single().SpeedChange.Value, () => Is.EqualTo(0.75));
            AddStep("back closes resumed settings", () => pressNavigationBack(false));
            AddUntilStep("settings close without leaving songs", () => Stack.CurrentScreen == songSelect && setup.State.Value == Visibility.Hidden);
            AddAssert("song rate remains intact after gameplay", () => SelectedMods.Value.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, () => Is.EqualTo(1.25));
            AddStep("back exits resumed songs", () => pressNavigationBack(false));
            AddUntilStep("parent restored", () => Stack.CurrentScreen == parent);
        }
        AddStep("leave parent", () => parent.Exit());
    }
}
