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
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
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
        buttons.Add(new BmsCourseFooterButton(courseController));

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

        yield return new OsuMenuItem(SongSelectStrings.ClearAllLocalScores, MenuItemType.Standard, () => dialogOverlay?.Push(new BeatmapClearScoresDialog(beatmap)))
        {
            Icon = FontAwesome.Solid.Eraser,
        };

        if (beatmaps.CanHide(beatmap))
            yield return new OsuMenuItem(WebCommonStrings.ButtonsHide.ToSentence(), MenuItemType.Destructive, () => beatmaps.Hide(beatmap));
    }

    protected override void OnStart()
    {
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
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e))
            return true;

        revertMods();
        return false;
    }

    private ModAutoplay? getAutoplayMod() => Ruleset.Value.CreateInstance().GetAutoplayMod();

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