// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Copied from osu.Game.Screens.Select.SongSelect at osu! revision
// 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Input.StateChanges;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using BeatmapSetLookupResult = osu.Game.Screens.Select.SongSelect.BeatmapSetLookupResult;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect;

public abstract partial class BmsSongSelect : ScreenWithBeatmapBackground, IKeyBindingHandler<GlobalAction>, ISongSelect, IHandlePresentBeatmap, IProvideCursor
{
    /// <summary>
    /// A debounce that governs how long after a panel is selected before the rest of song select (and the game at large)
    /// updates to show that selection.
    ///
    /// This is intentionally slightly higher than key repeat, but low enough to not impede user experience.
    /// </summary>
    private const int selection_debounce = 150;

    /// <summary>
    /// A general "global" debounce to be applied to anything aggressive difficulty calculation at song select,
    /// either after selection or after a panel comes on screen. Value should be low enough that users don't complain,
    /// but otherwise as high as possible to reduce overheads.
    /// </summary>
    public const int DIFFICULTY_CALCULATION_DEBOUNCE = 150;

    private const float logo_scale = 0.4f;
    private const double fade_duration = 300;

    public const float WEDGE_CONTENT_MARGIN = CORNER_RADIUS_HIDE_OFFSET + OsuGame.SCREEN_EDGE_MARGIN;
    public const float CORNER_RADIUS_HIDE_OFFSET = 20f;
    public const float ENTER_DURATION = 600;

    /// <summary>
    /// Whether this song select instance should take control of the global track,
    /// applying looping and preview offsets.
    /// </summary>
    private bool controlGlobalMusic { get; init; } = true;

    /// <summary>
    /// Whether this song select instance should allow scoping down to a specific beatmap set,
    /// exposing other difficulties that are otherwise hidden by filter criteria.
    /// </summary>
    protected bool SupportScoping
    {
        init => scopedBeatmapSet.Disabled = !value;
    }

    /// <summary>
    /// Whether the osu! logo should be shown at the bottom-right of the screen.
    /// </summary>
    protected bool ShowOsuLogo { get; init; } = true;

    /// <summary>
    /// Additional padding to be added to the title wedge.
    /// Generally set to show external content in this space.
    /// </summary>
    public float TopPadding { get; init; }

    internal ModSelectOverlay ModSelectOverlay = null!;
    private ModSpeedHotkeyHandler modSpeedHotkeyHandler = null!;

    // Blue is the most neutral choice, so I'm using that for now.
    // Purple makes the most sense to match the "gameplay" flow, but it's a bit too strong for the current design.
    // TODO: Colour scheme choice should probably be customisable by the user.
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Blue);

    internal BmsBeatmapCarousel Carousel = null!;

    internal FilterControl FilterControl { get; private set; } = null!;

    internal BeatmapTitleWedge TitleWedge = null!;
    internal BmsBeatmapDetailsArea DetailsArea = null!;
    internal FillFlowContainer WedgesContainer = null!;
    private Box rightGradientBackground = null!;
    private Container mainContent = null!;
    private SkinnableContainer skinnableContent = null!;

    private GridContainer mainGridContainer = null!;

    internal NoResultsPlaceholder NoResultsPlaceholder = null!;

    public override bool? ApplyModTrackAdjustments => true;

    public override bool ShowFooter => true;

    private Sample? errorSample;

    [Resolved]
    private OsuGameBase? game { get; set; }

    [Resolved]
    private OsuLogo? logo { get; set; }

    [Resolved]
    private BeatmapSetOverlay? beatmapOverlay { get; set; }

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private IAPIProvider api { get; set; } = null!;

    [Resolved]
    private ManageCollectionsDialog? collectionsDialog { get; set; }

    [Resolved]
    private DifficultyRecommender? difficultyRecommender { get; set; }

    [Resolved]
    private IDialogOverlay? dialogOverlay { get; set; }

    private InputManager inputManager = null!;

    private readonly RealmPopulatingOnlineLookupSource onlineLookupSource = new();

    private Bindable<bool> configBackgroundBlur = null!;
    private Bindable<bool> showConvertedBeatmaps = null!;
    private CancellationTokenSource? backgroundMetadataCancellation;
    private int backgroundMetadataGeneration;


    [BackgroundDependencyLoader]
    private void load(AudioManager audio, OsuConfigManager config)
    {
        errorSample = audio.Samples.Get(@"UI/generic-error");

        AddRangeInternal([
            new GlobalScrollAdjustsVolume(),
            onlineLookupSource,
            mainContent = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Bottom = ScreenFooter.HEIGHT },
                Child = new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new PopoverContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children =
                        [
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Width = 0.6f,
                                Colour = ColourInfo.GradientHorizontal(Color4.Black.Opacity(0.3f), Color4.Black.Opacity(0f)),
                            },
                            mainGridContainer = new GridContainer // used for max width implementation
                            {
                                RelativeSizeAxes = Axes.Both,
                                Content = new[]
                                {
                                    new[]
                                    {
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            // Ensure the left components are on top of the carousel both visually (although they should never overlay)
                                            // but more importantly, for input purposes to allow the scroll-to-selection logic to override carousel's
                                            // screen-wide scroll handling.
                                            Depth = float.MinValue,
                                            Shear = OsuGame.SHEAR,
                                            Padding = new MarginPadding
                                            {
                                                Top = -CORNER_RADIUS_HIDE_OFFSET,
                                                Left = -CORNER_RADIUS_HIDE_OFFSET,
                                            },
                                            Children =
                                            [
                                                new Container
                                                {
                                                    // Pad enough to only reset scroll when well into the left wedge areas.
                                                    Padding = new MarginPadding { Right = 40 },
                                                    RelativeSizeAxes = Axes.Both,
                                                    Child = new BmsLeftSideInteractionContainer(() => Carousel.ScrollToSelection())
                                                    {
                                                        RelativeSizeAxes = Axes.Both,
                                                    },
                                                },
                                                WedgesContainer = new FillFlowContainer
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Spacing = new Vector2(0f, 4f),
                                                    Direction = FillDirection.Vertical,
                                                    Children =
                                                    [
                                                        new ShearAligningWrapper(TitleWedge = new BeatmapTitleWedge
                                                        {
                                                            TopPadding = TopPadding,
                                                        }),
                                                        new ShearAligningWrapper(DetailsArea = new BmsBeatmapDetailsArea()),
                                                    ],
                                                },
                                            ],
                                        },
                                        Empty(),
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Children =
                                            [
                                                rightGradientBackground = new Box
                                                {
                                                    Anchor = Anchor.TopRight,
                                                    Origin = Anchor.TopRight,
                                                    Colour = ColourInfo.GradientHorizontal(Color4.Black.Opacity(0.0f), Color4.Black.Opacity(0.5f)),
                                                    RelativeSizeAxes = Axes.Both,
                                                },
                                                new Container
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Padding = new MarginPadding
                                                    {
                                                        Top = FilterControl.HEIGHT_FROM_SCREEN_TOP + 5,
                                                        Bottom = 5,
                                                    },
                                                    Children =
                                                    [
                                                        Carousel = new BmsBeatmapCarousel
                                                        {
                                                            BleedTop = FilterControl.HEIGHT_FROM_SCREEN_TOP + 5,
                                                            BleedBottom = ScreenFooter.HEIGHT + 5,
                                                            RelativeSizeAxes = Axes.Both,
                                                            RequestPresentBeatmap = b => SelectAndRun(b, OnStart),
                                                            RequestSelection = queueBeatmapSelection,
                                                            RequestRecommendedSelection = requestRecommendedSelection,
                                                            NewItemsPresented = newItemsPresented,
                                                        },
                                                        NoResultsPlaceholder = new NoResultsPlaceholder
                                                        {
                                                            RequestClearFilterText = () => FilterControl.Search(string.Empty),
                                                        },
                                                    ],
                                                },
                                                FilterControl = new FilterControl
                                                {
                                                    Anchor = Anchor.TopRight,
                                                    Origin = Anchor.TopRight,
                                                    RelativeSizeAxes = Axes.X,
                                                    ScopedBeatmapSet = { BindTarget = ScopedBeatmapSet },
                                                },
                                            ],
                                        },
                                    },
                                },
                            },
                        ],
                    },
                },
            },
            skinnableContent = new SkinnableContainer(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.SongSelect))
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
            },
            modSpeedHotkeyHandler = new ModSpeedHotkeyHandler(),
        ]);

        // IOverlayManager is internal to osu.Game, so a ruleset-owned page cannot
        // register this at game level. Keep the overlay functional by owning it here.
        AddInternal(ModSelectOverlay = createModSelectOverlay());
        // A lower depth is drawn later by CompositeDrawable and therefore stays above
        // locally-owned song-select content while also receiving non-positional input first.
        ChangeInternalChildDepth(ModSelectOverlay, float.MinValue);

        configBackgroundBlur = config.GetBindable<bool>(OsuSetting.SongSelectBackgroundBlur);
        configBackgroundBlur.BindValueChanged(_ =>
        {
            if (!this.IsCurrentScreen())
                return;

            updateBackgroundDim();
        });

        showConvertedBeatmaps = config.GetBindable<bool>(OsuSetting.ShowConvertedBeatmaps);
    }

    // Colour scheme for mod overlay is left as default (green) to match mods button.
    // Not sure about this, but we'll iterate based on feedback.
    private ModSelectOverlay createModSelectOverlay() => new UserModSelectOverlay
    {
        ShowPresets = true,
    };

    internal void AddCourseController(Drawable controller) => AddInternal(controller);

    private void requestRecommendedSelection(IEnumerable<GroupedBeatmap> groupedBeatmaps)
    {
        var recommendedBeatmap = difficultyRecommender?.GetRecommendedBeatmap(groupedBeatmaps.Select(gb => gb.Beatmap)) ?? groupedBeatmaps.First().Beatmap;
        queueBeatmapSelection(groupedBeatmaps.First(bug => bug.Beatmap.Equals(recommendedBeatmap)));
    }

    /// <summary>
    /// Called when a selection is made to progress away from the song select screen.
    ///
    /// This is the default action which should be provided to <see cref="SelectAndRun"/>.
    /// </summary>
    protected abstract void OnStart();

    public override IReadOnlyList<ScreenFooterButton> CreateFooterButtons() =>
    [
        new FooterButtonMods(ModSelectOverlay)
        {
            Hotkey = GlobalAction.ToggleModSelection,
            Mods = Mods,
            Ruleset = Ruleset,
            RequestDeselectAllMods = () =>
            {
                if (ModSelectOverlay.State.Value == Visibility.Visible)
                    ModSelectOverlay.DeselectAll();
                else
                    Mods.Value = [];
            },
        },
        new FooterButtonRandom
        {
            NextRandom = () =>
            {
                if (!Carousel.NextRandom())
                    errorSample?.Play();
            },
            PreviousRandom = () =>
            {
                if (!Carousel.PreviousRandom())
                    errorSample?.Play();
            },
        },
        new FooterButtonOptions
        {
            Hotkey = GlobalAction.ToggleBeatmapOptions,
        },
    ];

    protected override void LoadComplete()
    {
        base.LoadComplete();

        inputManager = GetContainingInputManager()!;

        FilterControl.CriteriaChanged += criteriaChanged;

        ModSelectOverlay.State.BindValueChanged(v =>
        {
            if (!this.IsCurrentScreen())
                return;

            if (ShowOsuLogo)
                logo?.FadeTo(v.NewValue == Visibility.Visible ? 0f : 1f, 200, Easing.OutQuint);
        });
    }

    protected override void Update()
    {
        base.Update();

        DetailsArea.Height = WedgesContainer.ChildSize.Y - TitleWedge.LayoutSize.Y - 4;

        var widescreenBonusWidth = Math.Max(0, DrawWidth / DrawHeight - 2f);

        mainGridContainer.ColumnDimensions =
        [
            new Dimension(GridSizeMode.Relative, 0.5f, maxSize: 700 + widescreenBonusWidth * 100),
            new Dimension(),
            new Dimension(GridSizeMode.Relative, 0.5f, minSize: 500, maxSize: 700 + widescreenBonusWidth * 300),
        ];

        if (this.IsCurrentScreen())
            updateDebounce();
    }

    #region Selection debounce

    private BeatmapInfo? debounceQueuedSelection;
    private double debounceElapsedTime;

    private void debounceQueueSelection(BeatmapInfo beatmap)
    {
        debounceQueuedSelection = beatmap;
        debounceElapsedTime = 0;
    }

    private void updateDebounce()
    {
        if (debounceQueuedSelection == null) return;

        var elapsed = Clock.ElapsedFrameTime;

        // When a key is being held, assume the user is traversing the carousel using key repeat.
        // We want to change panels less often in this state (basically making debounce longer than initial key repeat, at least).
        double debounceInterval = inputManager.CurrentState.Keyboard.Keys.HasAnyButtonPressed ? selection_debounce * 2 : selection_debounce;

        // avoid debounce running early if there's a single long frame.
        if (!DebugUtils.IsNUnitRunning && Clock.FramesPerSecond > 0)
            elapsed = Math.Min(1000 / Clock.FramesPerSecond, elapsed);

        debounceElapsedTime += elapsed;

        if (debounceElapsedTime >= debounceInterval)
            performDebounceSelection();
    }

    private void performDebounceSelection()
    {
        if (debounceQueuedSelection == null) return;

        try
        {
            if (this is BmsSoloSongSelect && UnavailableTableBeatmapFactory.Resolve(debounceQueuedSelection, BmsRulesetRuntime.DifficultyTableStore) != null)
                return;

            if (Beatmap.Value.BeatmapInfo.Equals(debounceQueuedSelection))
                return;

            Beatmap.Value = beatmaps.GetWorkingBeatmap(debounceQueuedSelection);
        }
        finally
        {
            cancelDebounceSelection();
        }
    }

    private void cancelDebounceSelection()
    {
        debounceQueuedSelection = null;
        debounceElapsedTime = 0;
    }

    #endregion

    #region Audio

    [Resolved]
    private MusicController music { get; set; } = null!;

    private readonly WeakReference<ITrack?> lastTrack = new(null);

    /// <summary>
    /// Ensures some music is playing for the current track.
    /// Will resume playback from a manual user pause if the track has changed.
    /// </summary>
    private void ensurePlayingSelected()
    {
        if (!controlGlobalMusic)
            return;

        ITrack track = music.CurrentTrack;

        var isNewTrack = !lastTrack.TryGetTarget(out var last) || last != track;

        if (!track.IsRunning && (!music.UserPauseRequested || isNewTrack))
        {
            Logger.Log($"Song select decided to {nameof(ensurePlayingSelected)}");

            // Only restart playback if a new track.
            // This is important so that when exiting gameplay, the track is not restarted back to the preview point.
            music.Play(isNewTrack);
        }

        lastTrack.SetTarget(track);
    }

    private bool isHandlingLooping;

    private void beginLooping()
    {
        Debug.Assert(!isHandlingLooping);

        isHandlingLooping = true;

        ensureTrackLooping(Beatmap.Value, TrackChangeDirection.None);

        music.TrackChanged += ensureTrackLooping;
    }

    private void endLooping()
    {
        // may be called multiple times during screen exit process.
        if (!isHandlingLooping)
            return;

        music.CurrentTrack.Looping = isHandlingLooping = false;

        music.TrackChanged -= ensureTrackLooping;
    }

    private void ensureTrackLooping(IWorkingBeatmap beatmap, TrackChangeDirection changeDirection)
        => beatmap.PrepareTrackForPreview(true);

    #endregion

    #region Selection handling

    /// <summary>
    /// Finalises selection on the given <see cref="BeatmapInfo"/> and runs the provided action if possible.
    /// </summary>
    /// <param name="beatmap">The beatmap which should be selected. If not provided, the current globally selected beatmap will be used.</param>
    /// <param name="startAction">The action to perform if conditions are met to be able to proceed. May not be invoked if in an invalid state.</param>
    protected void SelectAndRun(BeatmapInfo beatmap, Action startAction)
    {
        if (!this.IsCurrentScreen())
            return;

        if (this is BmsSoloSongSelect solo && BmsUnavailableTableBeatmapPlayHandler.TryHandle(solo, beatmap))
            return;

        if (!checkBeatmapValidForSelection(beatmap))
            return;

        // To ensure sanity, cancel any pending selection as we are about to force a selection.
        // Carousel selection will update to the forced selection via a call of `ensureGlobalBeatmapValid` below, or when song select becomes current again.
        cancelDebounceSelection();

        // Forced refetch is important here to guarantee correct invalidation across all difficulties (editor specific).
        Beatmap.Value = beatmaps.GetWorkingBeatmap(beatmap, true);

        if (Beatmap.IsDefault)
            return;

        startAction();
    }

    /// <summary>
    /// Prepares the proposed beatmap for global selection based on a carousel user-performed action.
    /// </summary>
    /// <remarks>
    /// Calling this method will:
    /// - Immediately update the selection the carousel.
    /// - After <see cref="selection_debounce"/>, update the global beatmap. This in turn causes song select visuals (title, details, leaderboard) to update.
    ///   This debounce is intended to avoid high overheads from churning lookups while a user is changing selection via rapid keyboard operations.
    /// </remarks>
    /// <param name="groupedBeatmap">The beatmap to be selected.</param>
    private void queueBeatmapSelection(GroupedBeatmap groupedBeatmap)
    {
        if (!this.IsCurrentScreen())
            return;

        Carousel.CurrentGroupedBeatmap = groupedBeatmap;

        // Debounce consideration is to avoid beatmap churn on key repeat selection.
        debounceQueueSelection(groupedBeatmap.Beatmap);
    }

    private void ensureGlobalBeatmapValid(bool refetch = true)
    {
        if (!this.IsCurrentScreen())
            return;

        performDebounceSelection();

        // While filtering, let's not ever attempt to change selection.
        // This will be resolved after the filter completes, see `newItemsPresented`.
        if (IsFiltering)
            return;

        // Refetch to be confident that the current selection is still valid. It may have been deleted or hidden.
        var currentBeatmap = refetch ? beatmaps.GetWorkingBeatmap(Beatmap.Value.BeatmapInfo, true) : Beatmap.Value;
        var validSelection = checkBeatmapValidForSelection(currentBeatmap.BeatmapInfo);

        if (validSelection)
        {
            Carousel.CurrentBeatmap = currentBeatmap.BeatmapInfo;
            return;
        }

        // If there was no beatmap selected, pick a random one.
        if (Beatmap.IsDefault)
        {
            validSelection = Carousel.NextRandom();
            performDebounceSelection();
            return;
        }

        // If a previous non-default selection became non-valid, it was likely hidden or deleted.
        if (!validSelection)
        {
            // In the case a difficulty was hidden or removed, prefer selecting another difficulty from the same set.
            var activeSet = currentBeatmap.BeatmapSetInfo;

            var validBeatmaps = activeSet.Beatmaps.Where(checkBeatmapValidForSelection).ToArray();

            if (validBeatmaps.Any())
            {
                var beatmap = difficultyRecommender?.GetRecommendedBeatmap(validBeatmaps) ?? validBeatmaps.First();
                Carousel.CurrentBeatmap = beatmap;
                debounceQueueSelection(beatmap);
                return;
            }
        }

        // If all else fails, use the default beatmap.
        Beatmap.SetDefault();
        performDebounceSelection();

    }

    private CancellationTokenSource? resumeValidationCancellation;
    private int resumeValidationGeneration;

    private void validateSelectionAfterResume()
    {
        resumeValidationCancellation?.Cancel();
        var cancellation = resumeValidationCancellation = new CancellationTokenSource();
        var generation = ++resumeValidationGeneration;
        var selected = Beatmap.Value;

        Task.Run(() => beatmaps.GetWorkingBeatmap(selected.BeatmapInfo, true), cancellation.Token).ContinueWith(task => Scheduler.Add(() =>
        {
            if (generation != resumeValidationGeneration || !task.IsCompletedSuccessfully || cancellation.IsCancellationRequested || !this.IsCurrentScreen()
                || !ReferenceEquals(Beatmap.Value, selected))
                return;

            Beatmap.Value = task.Result;
        }), TaskScheduler.Default);
    }

    private bool checkBeatmapValidForSelection(BeatmapInfo beatmap)
    {
        if (!BmsForeignBeatmapConverterRegistry.AllowsGameplay(beatmap, Ruleset.Value, showConvertedBeatmaps.Value))
            return false;

        if (beatmap.Hidden)
            return false;

        if (beatmap.BeatmapSet == null)
            return false;

        if (beatmap.BeatmapSet.Protected || beatmap.BeatmapSet.DeletePending)
            return false;

        return true;
    }

    #endregion

    #region Transitions

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        this.FadeIn();
        onArrivingAtScreen();
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);

        this.FadeIn(fade_duration, Easing.OutQuint);
        onArrivingAtScreen();

        DetailsArea.Refresh();
        validateSelectionAfterResume();

        if (controlGlobalMusic)
        {
            // restart playback on returning to song select, regardless.
            // not sure this should be a permanent thing (we may want to leave a user pause paused even on returning)
            music.ResetTrackAdjustments();
            music.Play(requestedByUser: true);
        }
    }

    public override void OnSuspending(ScreenTransitionEvent e)
    {
        Carousel.VisuallyFocusSelected = true;

        this.FadeOut(fade_duration, Easing.OutQuint);
        onLeavingScreen();

        base.OnSuspending(e);
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        resumeValidationCancellation?.Cancel();
        this.FadeOut(fade_duration, Easing.OutQuint);
        onLeavingScreen();

        return base.OnExiting(e);
    }

    private void onArrivingAtScreen()
    {
        ModSelectOverlay.Beatmap.BindTo(Beatmap);
        ModSelectOverlay.Ruleset.BindTo(Ruleset);
        // required due to https://github.com/ppy/osu-framework/issues/3218
        ModSelectOverlay.SelectedMods.Disabled = false;
        ModSelectOverlay.SelectedMods.BindTo(Mods);

        Carousel.VisuallyFocusSelected = false;

        if (controlGlobalMusic)
        {
            // Avoid abruptly starting playback at preview point.
            // Importantly, this should be done before looping is setup to ensure we get the correct imminent `IsPlaying` state.
            if (!music.IsPlaying)
            {
                music.DuckMomentarily(0, new DuckParameters
                {
                    DuckDuration = 0,
                    DuckVolumeTo = 0,
                    RestoreDuration = 800,
                    RestoreEasing = Easing.OutQuint,
                });
            }

            beginLooping();
        }

        Beatmap.BindValueChanged(updateVariousState, true);
    }

    private void updateVariousState(ValueChangedEvent<WorkingBeatmap> e)
    {
        if (!this.IsCurrentScreen())
            return;

        // The working beatmap already represents this change; resuming refetches it asynchronously.
        // Refetching here duplicates that query on the update thread and invalidates its fresh result again.
        ensureGlobalBeatmapValid(refetch: false);

        ensurePlayingSelected();
        updateBackgroundDim();
        updateWedgeVisibility();
        fetchOnlineInfo(force: ReferenceEquals(e.OldValue, e.NewValue));
    }

    private void onLeavingScreen()
    {
        resumeValidationCancellation?.Cancel();
        backgroundMetadataCancellation?.Cancel();
        backgroundMetadataCancellation = null;

        restoreBackground();

        Beatmap.ValueChanged -= updateVariousState;

        ModSelectOverlay.SelectedMods.UnbindFrom(Mods);
        ModSelectOverlay.Ruleset.UnbindFrom(Ruleset);
        ModSelectOverlay.Beatmap.UnbindFrom(Beatmap);

        updateWedgeVisibility();

        endLooping();
    }

    protected override void LogoArriving(OsuLogo logo, bool resuming)
    {
        base.LogoArriving(logo, resuming);

        if (!ShowOsuLogo)
            return;

        if (logo.Alpha > 0.8f && resuming)
            Footer?.StartTrackingLogo(logo, 400, Easing.OutQuint);
        else
        {
            logo.Hide();
            logo.ScaleTo(0.2f);
            Footer?.StartTrackingLogo(logo);
        }

        logo.FadeIn(240, Easing.OutQuint);
        logo.ScaleTo(logo_scale, 240, Easing.OutQuint);

        logo.Action = () =>
        {
            ensureGlobalBeatmapValid();
            SelectAndRun(Beatmap.Value.BeatmapInfo, OnStart);
            return false;
        };
    }

    protected override void LogoSuspending(OsuLogo logo)
    {
        base.LogoSuspending(logo);

        if (!ShowOsuLogo)
            return;

        Footer?.StopTrackingLogo();
    }

    protected override void LogoExiting(OsuLogo logo)
    {
        base.LogoExiting(logo);

        if (!ShowOsuLogo)
            return;

        Footer?.StopTrackingLogo();

        logo.ScaleTo(0.2f, 120, Easing.Out);
        logo.FadeOut(120, Easing.Out);
    }

    private void updateWedgeVisibility()
    {
        // Ensure we don't show an invalid selection before the carousel has finished initially filtering.
        // This avoids a flicker of a placeholder or invalid beatmap before a proper selection.
        //
        // After the carousel finishes filtering, it will attempt a selection then call this method again.
        if (!CarouselItemsPresented && !checkBeatmapValidForSelection(Beatmap.Value.BeatmapInfo))
            return;

        if (Carousel.VisuallyFocusSelected)
        {
            TitleWedge.Hide();
            DetailsArea.Hide();
            FilterControl.Hide();
        }
        else
        {
            TitleWedge.Show();
            DetailsArea.Show();
            FilterControl.Show();
        }
    }

    private void updateBackgroundDim()
    {
        var selected = Beatmap.Value;

        if (selected is BmsWorkingBeatmap bmsWorking)
        {
            backgroundMetadataCancellation?.Cancel();
            var cancellation = backgroundMetadataCancellation = new CancellationTokenSource();
            var generation = ++backgroundMetadataGeneration;

            bmsWorking.PrepareBackgroundMetadataAsync(cancellation.Token).ContinueWith(task => Scheduler.Add(() =>
            {
                if (generation != backgroundMetadataGeneration || !task.IsCompletedSuccessfully || cancellation.IsCancellationRequested
                    || !this.IsCurrentScreen() || !ReferenceEquals(Beatmap.Value, selected))
                    return;

                applyBackgroundDim(selected);
            }), TaskScheduler.Default);

            return;
        }

        applyBackgroundDim(selected);
    }

    private void applyBackgroundDim(WorkingBeatmap selected) => ApplyToBackground(backgroundModeBeatmap =>
    {
        backgroundModeBeatmap.Beatmap = selected;
        backgroundModeBeatmap.IgnoreUserSettings.Value = true;

        backgroundModeBeatmap.DimWhenUserSettingsIgnored.Value = 0.1f;

        // Required to undo results screen dimming the background.
        // Probably needs more thought because this needs to be in every `ApplyToBackground` currently to restore sane defaults.
        backgroundModeBeatmap.FadeColour(Color4.White, 250);

        var backgroundRevealActive = revealBackgroundDelegate?.State == ScheduledDelegate.RunState.Running || revealBackgroundDelegate?.State == ScheduledDelegate.RunState.Complete;
        backgroundModeBeatmap.BlurAmount.Value = configBackgroundBlur.Value && !backgroundRevealActive ? 20 : 0f;
    });

    #endregion

    #region Filtering

    /// <summary>
    /// Whether the carousel has finished initial presentation of beatmap panels.
    /// </summary>
    public bool CarouselItemsPresented { get; private set; }

    private CourseModeRestoration? pendingInitialRestoration;

    internal void RestoreSelectionAfterInitialPresentation(CourseModeRestoration restoration)
    {
        pendingInitialRestoration = restoration;

        if (CarouselItemsPresented)
            applyInitialRestoration();
    }

    /// <summary>
    /// Whether the carousel is or will be undergoing a filter operation.
    /// </summary>
    public bool IsFiltering => Carousel.IsFiltering || filterDebounce?.State == ScheduledDelegate.RunState.Waiting;

    private const double filter_delay = 250;

    private ScheduledDelegate? filterDebounce;

    private void criteriaChanged(FilterCriteria criteria)
    {
        filterDebounce?.Cancel();

        // The first filter needs to be applied immediately as this triggers the initial carousel load.
        var isFirstFilter = filterDebounce == null;

        // Criteria change may have included a ruleset change which made the current selection invalid.
        var isSelectionValid = checkBeatmapValidForSelection(Beatmap.Value.BeatmapInfo);

        filterDebounce = Scheduler.AddDelayed(() => Carousel.Filter(criteria, !isSelectionValid), isFirstFilter || !isSelectionValid ? 0 : filter_delay);
    }

    private void newItemsPresented(IEnumerable<CarouselItem> carouselItems)
    {
        if (Carousel.Criteria == null)
            return;

        CarouselItemsPresented = true;

        applyInitialRestoration();

        updateNoResultsPlaceholder();

        FilterControl.StatusText = SongSelectStrings.MatchesCount(Carousel.MatchedBeatmapsCount);

        // If there's already a selection update in progress, let's not interrupt it.
        // Interrupting could cause the debounce interval to be reduced.
        //
        // `ensureGlobalBeatmapValid` is run post-selection which will resolve any pending incompatibilities (see `Beatmap` bindable callback).
        if (debounceQueuedSelection == null)
            ensureGlobalBeatmapValid();

        updateWedgeVisibility();
    }

    private void applyInitialRestoration()
    {
        var restoration = pendingInitialRestoration;
        if (restoration == null)
            return;

        pendingInitialRestoration = null;
        Beatmap.Value = restoration.Beatmap;
        Mods.Value = restoration.Mods;
    }

    private void updateNoResultsPlaceholder()
    {
        var count = Carousel.MatchedBeatmapsCount;

        if (count == 0)
        {
            if (NoResultsPlaceholder.State.Value == Visibility.Hidden)
            {
                // Duck audio temporarily when the no results placeholder becomes visible.
                //
                // Temporary ducking makes it easier to avoid scenarios where the ducking interacts badly
                // with other global UI components (like overlays).
                music.DuckMomentarily(400, new DuckParameters
                {
                    DuckVolumeTo = 1,
                    DuckCutoffTo = 500,
                    DuckDuration = 250,
                    RestoreDuration = 2000,
                });
            }

            NoResultsPlaceholder.Show();
            NoResultsPlaceholder.Filter = Carousel.Criteria!;

            rightGradientBackground.ResizeWidthTo(3, 1000, Easing.OutPow10);
        }
        else
        {
            NoResultsPlaceholder.Hide();

            rightGradientBackground.ResizeWidthTo(1, 400, Easing.OutPow10);
        }
    }

    #endregion

    #region Background reveal

    private ScheduledDelegate? revealBackgroundDelegate;

    public CursorContainer? Cursor => null;

    bool IProvideCursor.ProvidingUserCursor => revealBackgroundDelegate?.Completed == true;

    protected override bool OnHover(HoverEvent e) => true;

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        var containingInputManager = GetContainingInputManager();

        // I don't know why this works, but it does.
        // If the carousel panels are hovered, hovered no longer contains the screen.
        // Maybe there's a better way of doing this, but I couldn't immediately find a good setup.
        var mouseDownPriority = containingInputManager!.HoveredDrawables.Contains(this);

        // Touch input synthesises right clicks, which allow absolute scroll of the carousel.
        // For simplicity, disable this functionality on mobile.
        var isTouchInput = e.CurrentState.Mouse.LastSource is ISourcedFromTouch;

        if (!Carousel.AbsoluteScrolling && !isTouchInput && mouseDownPriority && revealBackgroundDelegate == null)
        {
            revealBackgroundDelegate = Scheduler.AddDelayed(() =>
            {
                if (containingInputManager.DraggedDrawable != null)
                {
                    revealBackgroundDelegate = null;
                    return;
                }

                mainContent.ResizeWidthTo(1.2f, 600, Easing.OutQuint);
                mainContent.ScaleTo(1.2f, 600, Easing.OutQuint);
                mainContent.FadeOut(200, Easing.OutQuint);

                skinnableContent.ResizeWidthTo(1.2f, 600, Easing.OutQuint);
                skinnableContent.ScaleTo(1.2f, 600, Easing.OutQuint);
                skinnableContent.FadeOut(200, Easing.OutQuint);

                updateBackgroundDim();

                Footer?.Hide();
            }, 200);
        }

        return base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        restoreBackground();
        base.OnMouseUp(e);
    }

    private void restoreBackground()
    {
        if (revealBackgroundDelegate == null)
            return;

        if (revealBackgroundDelegate.State == ScheduledDelegate.RunState.Complete)
        {
            mainContent.ResizeWidthTo(1f, 500, Easing.OutQuint);
            mainContent.ScaleTo(1, 500, Easing.OutQuint);
            mainContent.FadeIn(500, Easing.OutQuint);

            skinnableContent.ResizeWidthTo(1f, 500, Easing.OutQuint);
            skinnableContent.ScaleTo(1, 500, Easing.OutQuint);
            skinnableContent.FadeIn(500, Easing.OutQuint);

            Footer?.Show();
        }

        revealBackgroundDelegate.Cancel();
        revealBackgroundDelegate = null;

        updateBackgroundDim();
    }

    #endregion

    #region Input

    public virtual bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!this.IsCurrentScreen()) return false;

        if (game == null)
            return false;

        var flattenedMods = ModUtils.FlattenMods(game.AvailableMods.Value.SelectMany(kv => kv.Value));

        switch (e.Action)
        {
            case GlobalAction.Select:
                // in most circumstances this is handled already by the carousel itself, but there are cases where it will not be.
                // one of which is filtering out all visible beatmaps and attempting to start gameplay.
                // in that case, users still expect a `Select` press to advance to gameplay anyway, using the ambient selected beatmap if there is one,
                // which matches the behaviour resulting from clicking the osu! cookie in that scenario.
                ensureGlobalBeatmapValid();
                SelectAndRun(Beatmap.Value.BeatmapInfo, OnStart);
                return true;

            case GlobalAction.IncreaseModSpeed:
                return modSpeedHotkeyHandler.ChangeSpeed(0.05, flattenedMods);

            case GlobalAction.DecreaseModSpeed:
                return modSpeedHotkeyHandler.ChangeSpeed(-0.05, flattenedMods);
        }

        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Repeat) return false;

        switch (e.Key)
        {
            case Key.Delete:
                if (e.ShiftPressed)
                {
                    if (!Beatmap.IsDefault)
                        Delete(Beatmap.Value.BeatmapSetInfo);
                    return true;
                }

                break;
        }

        return base.OnKeyDown(e);
    }

    #endregion

    #region Online lookups

    /// <summary>
    /// Result of the latest online beatmap set lookup.
    /// Note that this being <see langword="null"/> or <see cref="BeatmapSetLookupResult.InProgress"/> is different from
    /// being a <see cref="BeatmapSetLookupResult.Completed"/> with a <see cref="BeatmapSetLookupResult.Result"/> of null.
    /// The former indicates a lookup never occurring or being in progress, while the latter indicates a completed lookup with no result.
    /// </summary>
    [Cached(typeof(IBindable<BeatmapSetLookupResult?>))]
    private readonly Bindable<BeatmapSetLookupResult?> lastLookupResult = new();

    private CancellationTokenSource? onlineLookupCancellation;
    private Task<APIBeatmapSet?>? currentOnlineLookup;

    private void fetchOnlineInfo(bool force = false)
    {
        var beatmapSetInfo = Beatmap.Value.BeatmapSetInfo;

        if (lastLookupResult.Value?.Result?.OnlineID == beatmapSetInfo.OnlineID && !force)
            return;

        onlineLookupCancellation?.Cancel();
        onlineLookupCancellation = null;

        if (beatmapSetInfo.OnlineID < 0)
        {
            lastLookupResult.Value = BeatmapSetLookupResult.Completed(null);
            return;
        }

        lastLookupResult.Value = BeatmapSetLookupResult.InProgress();
        onlineLookupCancellation = new CancellationTokenSource();
        currentOnlineLookup = onlineLookupSource.GetBeatmapSetAsync(beatmapSetInfo.OnlineID, onlineLookupCancellation.Token);
        currentOnlineLookup.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                Schedule(() => lastLookupResult.Value = BeatmapSetLookupResult.Completed(t.GetResultSafely()));

            if (t.Exception != null)
            {
                Logger.Log($"Error when fetching online beatmap set: {t.Exception}", LoggingTarget.Network);
                Schedule(() => lastLookupResult.Value = BeatmapSetLookupResult.Completed(null));
            }
        });
    }

    #endregion

    #region Implementation of ISongSelect

    void ISongSelect.AddToSearch(string query) => FilterControl.AddToSearch(query);

    bool ISongSelect.CanPresentScore => true;

    void ISongSelect.PresentScore(ScoreInfo score, ScorePresentType presentType)
    {
        switch (presentType)
        {
            case ScorePresentType.Results:
                Debug.Assert(Beatmap.Value.BeatmapInfo.Equals(score.BeatmapInfo));
                Debug.Assert(Ruleset.Value.Equals(score.Ruleset));

                this.Push(score.Ruleset.ShortName == Constant.SHORT_NAME ? new BmsResultsScreen(score) : new SoloResultsScreen(score));
                break;

            case ScorePresentType.Gameplay:
                (game as OsuGame)?.PresentScore(score, presentType);
                break;
        }
    }

    #endregion

    #region IHandlePresentBeatmap

    void IHandlePresentBeatmap.PresentBeatmap(WorkingBeatmap workingBeatmap, RulesetInfo ruleset)
    {
        cancelDebounceSelection();

        var beatmapInfo = workingBeatmap.BeatmapInfo;

        // Don't change the local ruleset if the user is on another ruleset and is showing converted beatmaps.
        // Eventually we probably want to check whether conversion is actually possible for the current ruleset.
        var requiresRulesetSwitch = !beatmapInfo.Ruleset.Equals(Ruleset.Value)
                                    && (beatmapInfo.Ruleset.OnlineID > 0 || !showConvertedBeatmaps.Value);

        if (requiresRulesetSwitch)
        {
            Ruleset.Value = beatmapInfo.Ruleset;
            Beatmap.Value = workingBeatmap;

            Logger.Log($"Completing {nameof(IHandlePresentBeatmap.PresentBeatmap)} with beatmap {workingBeatmap} ruleset {beatmapInfo.Ruleset}");
        }
        else
        {
            Beatmap.Value = workingBeatmap;

            Logger.Log($"Completing {nameof(IHandlePresentBeatmap.PresentBeatmap)} with beatmap {workingBeatmap} (maintaining ruleset)");
        }
    }

    #endregion

    #region Beatmap management

    [Resolved]
    private ManageCollectionsDialog? manageCollectionsDialog { get; set; }

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    public virtual IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
    {
        yield return new OsuMenuItem(GlobalActionKeyBindingStrings.Select, MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart))
        {
            Icon = FontAwesome.Solid.Check,
        };

        yield return new OsuMenuItemSpacer();

        if (beatmap.OnlineID > 0)
        {
            yield return new OsuMenuItem(CommonStrings.Details, MenuItemType.Standard, () => beatmapOverlay?.FetchAndShowBeatmap(beatmap.OnlineID));

            if (beatmap.GetOnlineURL(api, Ruleset.Value) is string url)
                yield return new OsuMenuItem(CommonStrings.CopyLink, MenuItemType.Standard, () => (game as OsuGame)?.CopyToClipboard(url));
        }

        yield return new OsuMenuItemSpacer();

        foreach (var i in CreateCollectionMenuActions(beatmap))
            yield return i;
    }

    protected IEnumerable<OsuMenuItem> CreateCollectionMenuActions(BeatmapInfo beatmap)
    {
        var collectionItems = realm.Realm.All<BeatmapCollection>()
            .OrderBy(c => c.Name)
            .AsEnumerable()
            .Select(c => new CollectionToggleMenuItem(c.ToLive(realm), beatmap)).Cast<OsuMenuItem>().ToList();

        collectionItems.Add(new OsuMenuItem(CommonStrings.Manage, MenuItemType.Standard, () => manageCollectionsDialog?.Show()));

        yield return new OsuMenuItem(CommonStrings.Collections) { Items = collectionItems };
    }

    public void ManageCollections() => collectionsDialog?.Show();

    public void Delete(BeatmapSetInfo beatmapSet) => dialogOverlay?.Push(new BeatmapDeleteDialog(beatmapSet));

    public void RestoreAllHidden(BeatmapSetInfo beatmapSet) => beatmaps.Restore(beatmapSet);

    private GroupedBeatmap? beforeScopedSelection;

    private readonly Bindable<BeatmapSetInfo?> scopedBeatmapSet = new();

    public IBindable<BeatmapSetInfo?> ScopedBeatmapSet => scopedBeatmapSet;

    public void ScopeToBeatmapSet(BeatmapSetInfo beatmapSet)
    {
        beforeScopedSelection = Carousel.CurrentGroupedBeatmap;

        scopedBeatmapSet.Value = beatmapSet;
    }

    public void UnscopeBeatmapSet()
    {
        if (scopedBeatmapSet.Value == null)
            return;

        if (beforeScopedSelection != null)
            queueBeatmapSelection(beforeScopedSelection);

        scopedBeatmapSet.Value = null;
        beforeScopedSelection = null;
    }

    #endregion

}
