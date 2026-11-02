// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Copied from osu.Game.Screens.Select.SoloSongSelect at osu! revision
// 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Users;
using osu.Game.Utils;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect;

public partial class BmsSoloSongSelect : BmsSongSelect
{
    protected override UserActivity InitialActivity => new UserActivity.ChoosingBeatmap();

    private PlayerLoader? playerLoader;
    private IReadOnlyList<Mod>? modsAtGameplayStart;
    private BmsCourseSongSelectController? courseController;
    private readonly bool startInCourseMode;
    private readonly CourseModeRestoration? initialCourseRestoration;
    private bool initialModeScheduled;
    private BmsPracticeSetupOverlay? practiceSetup;
    private BmsPracticeScreen? practiceScreen;
    private bool resumePracticeSetup;
    private FooterButtonMods? footerModsButton;
    private BmsPracticeFooterButton? footerPracticeButton;
    private readonly Dictionary<ScreenFooterButton, bool> footerAvailabilityBeforePractice = [];
    private IReadOnlyList<ScreenFooterButton> footerButtons = [];

    protected override Bindable<IReadOnlyList<Mod>> FooterMods => practiceSetup?.State.Value == Visibility.Visible ? ModSelectOverlay.SelectedMods : Mods;

    public BmsSoloSongSelect()
    {
    }

    internal BmsSoloSongSelect(bool startInCourseMode, CourseModeRestoration? initialCourseRestoration = null)
    {
        this.startInCourseMode = startInCourseMode;
        this.initialCourseRestoration = initialCourseRestoration;
    }

    public override IReadOnlyList<ScreenFooterButton> CreateFooterButtons()
    {
        var buttons = base.CreateFooterButtons().ToList();
        courseController ??= createCourseController();
        courseController.StartRequested = () => courseController.StartCourse(this);
        courseController.AttachRandomButton(buttons.OfType<FooterButtonRandom>().SingleOrDefault());
        footerPracticeButton = new BmsPracticeFooterButton(courseController, togglePractice)
        {
            OverlayState = { Value = practiceSetup?.State.Value ?? Visibility.Hidden },
        };
        buttons.InsertRange(buttons.FindIndex(button => button is FooterButtonMods) + 1,
            [footerPracticeButton, new BmsCourseFooterButton(courseController)]);
        footerButtons = buttons;
        footerModsButton = buttons.OfType<FooterButtonMods>().Single();

        if (!initialModeScheduled)
        {
            initialModeScheduled = true;

            if (startInCourseMode)
                Schedule(() => courseController.ShowCourseMode(initialCourseRestoration));
            else if (initialCourseRestoration != null)
                RestoreSelectionAfterInitialPresentation(initialCourseRestoration);
        }

        return buttons;
    }

    [Resolved]
    private BeatmapSetOverlay? beatmapOverlay { get; set; }

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private IAPIProvider api { get; set; } = null!;

    [Resolved]
    private INotificationOverlay? notifications { get; set; }

    [Resolved]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved]
    private OsuGame? game { get; set; }

    private Sample? sampleConfirmSelection { get; set; }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio)
    {
        sampleConfirmSelection = audio.Samples.Get(@"SongSelect/confirm-selection");

        AddInternal(new SongSelectTouchInputDetector());
    }

    public override IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
    {
        yield return new OsuMenuItem(ButtonSystemStrings.Play.ToSentence(), MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart)) { Icon = FontAwesome.Solid.Check };
        if (courseController?.IsCourseMode != true && Ruleset.Value.ShortName == Constant.SHORT_NAME && BmsPracticeSession.SupportsBeatmap(beatmap))
            yield return new OsuMenuItem(BmsStrings.Practice, MenuItemType.Standard, () => SelectAndRun(beatmap, openPractice)) { Icon = FontAwesome.Solid.Bullseye };
        yield return new OsuMenuItem(ButtonSystemStrings.Edit.ToSentence(), MenuItemType.Standard, () => Edit(beatmap)) { Icon = FontAwesome.Solid.PencilAlt };

        yield return new OsuMenuItemSpacer();

        if (beatmap.OnlineID > 0)
        {
            yield return new OsuMenuItem(CommonStrings.Details, MenuItemType.Standard, () => beatmapOverlay?.FetchAndShowBeatmap(beatmap.OnlineID));

            if (beatmap.GetOnlineURL(api, Ruleset.Value) is string url)
                yield return new OsuMenuItem(CommonStrings.CopyLink, MenuItemType.Standard, () => game?.CopyToClipboard(url));

            yield return new OsuMenuItemSpacer();
        }

        foreach (var i in CreateCollectionMenuActions(beatmap))
            yield return i;

        if (beatmap.LastPlayed == null)
            yield return new OsuMenuItem(SongSelectStrings.MarkAsPlayed, MenuItemType.Standard, () => beatmaps.MarkPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };
        else
            yield return new OsuMenuItem(SongSelectStrings.RemoveFromPlayed, MenuItemType.Standard, () => beatmaps.MarkNotPlayed(beatmap)) { Icon = FontAwesome.Solid.TimesCircle };

        yield return new OsuMenuItem(SongSelectStrings.ClearAllLocalScores, MenuItemType.Destructive, () => dialogOverlay?.Push(new BeatmapClearScoresDialog(beatmap)))
        {
            Icon = FontAwesome.Solid.Eraser,
        };

        if (beatmaps.CanHide(beatmap))
            yield return new OsuMenuItem(WebCommonStrings.ButtonsHide.ToSentence(), MenuItemType.Standard, () => beatmaps.Hide(beatmap));
    }

    protected override void OnStart()
    {
        if (practiceSetup?.State.Value == Visibility.Visible)
            return;

        if (BmsUnavailableTableBeatmapPlayHandler.TryHandle(this, Beatmap.Value.BeatmapInfo))
            return;

        if (courseController?.IsCourseMode == true)
        {
            courseController.StartCourse(this);
            return;
        }

        if (playerLoader != null) return;

        modsAtGameplayStart = Mods.Value.Select(m => m.DeepClone()).ToArray();

        // Ctrl+Enter should start map with autoplay enabled.
        if (GetContainingInputManager()?.CurrentState?.Keyboard.ControlPressed == true)
        {
            var autoInstance = getAutoplayMod();

            if (autoInstance == null)
            {
                notifications?.Post(new SimpleNotification
                {
                    Text = NotificationsStrings.NoAutoplayMod,
                });
                return;
            }

            var mods = Mods.Value.Append(autoInstance).ToArray();

            if (!ModUtils.CheckCompatibleSet(mods, out var invalid))
                mods = mods.Except(invalid).Append(autoInstance).ToArray();

            Mods.Value = mods;
        }

        sampleConfirmSelection?.Play();

        this.Push(playerLoader = new PlayerLoader(createPlayer));

        Player createPlayer()
        {
            Player player;

            var replayGeneratingMod = Mods.Value.OfType<ICreateReplayData>().FirstOrDefault();

            if (replayGeneratingMod != null)
            {
                player = new ReplayPlayer(replayGeneratingMod.CreateScoreFromReplayData);
            }
            else
            {
                player = new SoloPlayer();
            }

            return player;
        }
    }

    public void Edit(BeatmapInfo beatmap)
    {
        if (!this.IsCurrentScreen())
            return;

        SelectAndRun(beatmap, () => this.Push(new EditorLoader()));
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);
        revertMods();
        practiceScreen = null;

        if (resumePracticeSetup)
        {
            resumePracticeSetup = false;
            Schedule(() =>
            {
                if (this.IsCurrentScreen())
                {
                    practiceSetup?.Show();
                }
            });
        }
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        practiceSetup?.Hide();
        resumePracticeSetup = false;
        if (base.OnExiting(e))
            return true;

        revertMods();
        return false;
    }

    public override void OnSuspending(ScreenTransitionEvent e)
    {
        practiceSetup?.Hide();
        base.OnSuspending(e);
    }

    private ModAutoplay? getAutoplayMod() => Ruleset.Value.CreateInstance().GetAutoplayMod();

    public override bool OnBackButton()
    {
        if (!this.IsCurrentScreen())
            return true;

        if (ModSelectOverlay.State.Value == Visibility.Visible)
        {
            if (!ModSelectOverlay.OnBackButton())
                ModSelectOverlay.Hide();
            return true;
        }

        if (practiceSetup?.State.Value == Visibility.Visible)
            return practiceSetup.HandleBack();

        if (courseController?.IsCourseMode == true)
        {
            ReplaceCourseMode(false);
            return true;
        }

        return base.OnBackButton();
    }

    private void togglePractice()
    {
        if (!this.IsCurrentScreen())
            return;

        if (practiceSetup?.State.Value == Visibility.Visible)
            practiceSetup.Hide();
        else
            SelectAndRun(Beatmap.Value.BeatmapInfo, openPractice);
    }

    private void openPractice()
    {
        if (!this.IsCurrentScreen() || courseController?.IsCourseMode == true || playerLoader != null || practiceScreen != null
            || Beatmap.IsDefault || Ruleset.Value.ShortName != Constant.SHORT_NAME || !BmsPracticeSession.SupportsBeatmap(Beatmap.Value.BeatmapInfo))
            return;

        if (practiceSetup != null)
        {
            practiceSetup.Hide();
            practiceSetup.State.ValueChanged -= practiceVisibilityChanged;
            RemoveInternal(practiceSetup, true);
        }

        ModSelectOverlay.Hide();
        AddInternal(practiceSetup = new BmsPracticeSetupOverlay(Beatmap.Value, Mods.Value, modSelectOverlay: ModSelectOverlay));
        practiceSetup.State.ValueChanged += practiceVisibilityChanged;
        practiceSetup.StartRequested += startPractice;
        practiceSetup.Show();
    }

    private void practiceVisibilityChanged(ValueChangedEvent<Visibility> change)
    {
        ModSelectOverlay.Hide();
        if (change.NewValue == Visibility.Visible)
        {
            // One shared overlay keeps both Mod entry points in the practice scope and above the modal settings.
            ModSelectOverlay.SelectedMods.UnbindFrom(Mods);
            // The native overlay reuses and resets its Mod instances when selection changes.
            // Detach song selection from those instances before switching the overlay to practice.
            Mods.Value = Mods.Value.Select(mod => mod.DeepClone()).ToArray();
            practiceSetup!.ActivateModSelection();
            foreach (var button in footerButtons.Where(button => button != footerModsButton && button != footerPracticeButton))
            {
                footerAvailabilityBeforePractice[button] = button.Enabled.Value;
                button.Enabled.Value = false;
                button.FadeOut(150);
            }
        }
        else
        {
            if (this.IsCurrentScreen())
                ModSelectOverlay.SelectedMods.BindTo(Mods);
            foreach (var (button, enabled) in footerAvailabilityBeforePractice)
            {
                if (footerButtons.Contains(button))
                {
                    button.Enabled.Value = enabled;
                    button.FadeIn(150);
                }
            }
            footerAvailabilityBeforePractice.Clear();
        }

        if (footerModsButton != null)
            footerModsButton.Mods = FooterMods;
        if (footerPracticeButton != null)
            footerPracticeButton.OverlayState.Value = change.NewValue;
    }

    private void startPractice(BmsPracticeSession session)
    {
        if (!this.IsCurrentScreen() || courseController?.IsCourseMode == true || practiceScreen != null)
            return;

        practiceSetup?.Hide();
        practiceScreen = new BmsPracticeScreen(session);
        practiceScreen.AdjustRangeRequested += () => resumePracticeSetup = true;
        this.Push(practiceScreen);
    }

    private BmsCourseSongSelectController createCourseController()
    {
        var controller = new BmsCourseSongSelectController(
            BmsRulesetRuntime.CourseCatalog,
            this,
            () => ModSelectOverlay,
            WedgesContainer,
            TitleWedge,
            DetailsArea,
            FilterControl,
            Carousel,
            NoResultsPlaceholder,
            TopPadding)
        {
            Name = "BMS course selector controller",
        };

        AddCourseController(controller);
        return controller;
    }

    internal void ReplaceCourseMode(bool courseMode)
    {
        var controller = courseController;

        if (!this.IsCurrentScreen() || controller == null || controller.IsCourseMode == courseMode)
            return;

        var parent = this.GetParentScreen();
        if (parent == null)
            return;

        var restoration = courseMode
            ? new CourseModeRestoration(Beatmap.Value, Mods.Value, initialCourseRestoration?.CourseId)
            : TakeCourseModeRestoration();

        this.Exit();

        // Restore global state only after the old song select has unbound its UI callbacks.
        // This avoids waking the suspended normal carousel that the replacement is intended to discard.
        if (restoration != null)
        {
            Beatmap.Value = restoration.Beatmap;
            Mods.Value = restoration.Mods;
        }

        if (!parent.IsCurrentScreen())
            return;

        var replacement = new BmsSoloSongSelect(courseMode, restoration);
        parent.Push(replacement);
        BmsSongSelectEntryPatcher.TrackRulesetChanges(replacement);
    }

    internal CourseModeRestoration? TakeCourseModeRestoration()
    {
        var controller = courseController;
        return controller?.IsCourseMode == true ? controller.PrepareForScreenReplacement() : null;
    }

    private void revertMods()
    {
        if (playerLoader == null) return;

        Mods.Value = modsAtGameplayStart;
        playerLoader = null;
    }

    private partial class PlayerLoader : osu.Game.Screens.Play.PlayerLoader
    {
        public override bool ShowFooter => !QuickRestart;

        public PlayerLoader(Func<Player> createPlayer)
            : base(createPlayer)
        {
        }
    }
}
