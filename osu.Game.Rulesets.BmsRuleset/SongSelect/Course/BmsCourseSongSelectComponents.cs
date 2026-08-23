using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Result.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Play.Leaderboards;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseFilterControl : VisibilityContainer
{
    internal BmsCourseFilterControl(Bindable<string> searchTerm)
    {
        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Shear = OsuGame.SHEAR;
        Margin = new MarginPadding { Top = -Panel.CORNER_RADIUS, Right = -40 };
        X = 150;

        InternalChildren =
        [
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                CornerRadius = Panel.CORNER_RADIUS,
                Masking = true,
                Child = new BmsCourseWedgeBackground
                {
                    Anchor = Anchor.TopRight,
                    Scale = new Vector2(-1, 1),
                },
            },
            new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding { Top = Panel.CORNER_RADIUS + 5, Bottom = 7, Right = 40, Left = 2 },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Shear = -OsuGame.SHEAR,
                    Child = new BmsCourseSearchTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Current = searchTerm,
                        HoldFocus = true,
                        PlaceholderText = BmsStrings.CourseSearchPlaceholder,
                    },
                },
            },
        ];
    }

    protected override bool StartHidden => true;

    private partial class BmsCourseSearchTextBox : ShearedSearchTextBox
    {
        protected override InnerSearchTextBox CreateInnerTextBox() => new CourseInnerSearchTextBox();

        private partial class CourseInnerSearchTextBox : InnerSearchTextBox
        {
            public override bool HandleLeftRightArrows => false;

            public override bool OnPressed(KeyBindingPressEvent<PlatformAction> e)
            {
                // These platform text-selection bindings conflict with song select's Shift+Left/Right group navigation.
                if (e.Action == PlatformAction.SelectBackwardChar || e.Action == PlatformAction.SelectForwardChar)
                    return false;

                // Keep parity with SongSelectSearchTextBox: Shift+Delete belongs to song select rather than text editing.
                if (e.Action == PlatformAction.Cut && e.ShiftPressed && e.CurrentState.Keyboard.Keys.IsPressed(Key.Delete))
                    return false;

                return base.OnPressed(e);
            }
        }
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }
}

internal partial class BmsCourseTitleWedge : VisibilityContainer
{
    internal float TopPadding { get; init; }

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private OsuSpriteText tableText = null!;
    private OsuSpriteText titleText = null!;
    private OsuSpriteText summaryText = null!;
    private OsuSpriteText constraintText = null!;

    internal BmsCourseTitleWedge(IBindable<BmsCourseDefinition?> selectedCourse)
    {
        this.selectedCourse = selectedCourse;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        X = -150;
    }

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = Panel.CORNER_RADIUS;

        InternalChildren =
        [
            new BmsCourseWedgeBackground(),
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding
                {
                    Top = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN + TopPadding,
                    Left = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN,
                    Right = 36,
                    Bottom = 18,
                },
                Spacing = new Vector2(0, 4),
                Children =
                [
                    unShear(tableText = new OsuSpriteText
                    {
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                    }),
                    unShear(titleText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Title.With(italics: false),
                    }),
                    unShear(summaryText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold, italics: false),
                    }),
                    unShear(constraintText = new OsuSpriteText
                    {
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                    }),
                ],
            },
        ];

        selectedCourse.BindValueChanged(_ => updateDisplay(), true);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    private void updateDisplay()
    {
        var course = selectedCourse.Value;

        tableText.Text = course?.TableName ?? string.Empty;
        titleText.Text = course?.Name ?? BmsStrings.Courses;
        summaryText.Text = course == null
            ? BmsStrings.SelectCourseForDetails
            : BmsStrings.CourseTitleSummary(course.Stages.Count);
        constraintText.Text = course == null
            ? string.Empty
            : string.Join(" · ", course.Constraints.Select(formatConstraint));
    }

    private static string formatConstraint(string constraint) => constraint.ToLowerInvariant() switch
    {
        "grade" => "GRADE",
        "grade_mirror" => "GRADE MIRROR",
        "grade_random" => "GRADE RANDOM",
        "no_speed" => "NO SPEED",
        "no_good" => "NO GOOD",
        "no_great" => "NO GREAT",
        "gauge_lr2" => "GAUGE LR2",
        "gauge_5k" => "GAUGE 5K",
        "gauge_7k" => "GAUGE 7K",
        "gauge_9k" => "GAUGE 9K",
        "gauge_24k" => "GAUGE 24K",
        "ln" => "LN",
        "cn" => "CN",
        "hcn" => "HCN",
        _ => constraint,
    };

    private static Drawable unShear(Drawable drawable)
    {
        drawable.Shear = -OsuGame.SHEAR;
        return new ShearAligningWrapper(drawable);
    }
}

internal partial class BmsCourseStagePanel : Panel
{
    private BmsCourseStage? stage;
    private OsuSpriteText keyCountText = null!;
    private OsuSpriteText titleText = null!;
    private OsuSpriteText artistText = null!;
    private OsuSpriteText difficultyText = null!;
    private FillFlowContainer mainFill = null!;
    private BmsCourseStageScoreDisplay stageScoreDisplay = null!;
    private StarRatingDisplay starRatingDisplay = null!;
    private PanelBeatmapStandalone.SpreadDisplay spreadDisplay = null!;
    private PanelSetBackground panelBackground = null!;
    private ConstrainedIconContainer difficultyIcon = null!;
    private BmsLampDisplay lamp = null!;
    private Box missingBackground = null!;
    private Color4 availableIconColour;
    private Color4 defaultAccentColour;
    private ScheduledDelegate? scheduledBackgroundRetrieval;
    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    internal BeatmapInfo? ResolvedBeatmap { get; private set; }

    public BmsCourseStagePanel()
    {
        PanelXOffset = 40;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = PanelBeatmapStandalone.HEIGHT;
        AccentColour = defaultAccentColour = colourProvider.Highlight1;
        availableIconColour = colourProvider.Background5;

        Icon = difficultyIcon = new ConstrainedIconContainer
        {
            Icon = new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle },
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = Color4.OrangeRed,
            Alpha = 1,
            AlwaysPresent = true,
        };

        Background = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                lamp = new BmsLampDisplay(BmsLamp.NoPlay)
                {
                    RelativeSizeAxes = Axes.Both,
                    Size = Vector2.One,
                },
                missingBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Highlight1,
                    Alpha = 0,
                },
            ],
        };
        stageScoreDisplay = new BmsCourseStageScoreDisplay(lamp)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Scale = new Vector2(0.8f),
        };

        var ratingRow = new FillFlowContainer
        {
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(3),
            AutoSizeAxes = Axes.Both,
        };

        ratingRow.Children =
        [
            starRatingDisplay = new StarRatingDisplay(default, StarRatingDisplaySize.Small, animated: true)
            {
                Origin = Anchor.CentreLeft,
                Anchor = Anchor.CentreLeft,
                Scale = new Vector2(0.875f),
            },
            spreadDisplay = new PanelBeatmapStandalone.SpreadDisplay
            {
                Origin = Anchor.CentreLeft,
                Anchor = Anchor.CentreLeft,
                Selected = { BindTarget = Selected },
            },
        ];

        Content.Children =
        [
            panelBackground = new PanelSetBackground(),
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Spacing = new Vector2(5),
                Margin = new MarginPadding { Left = 6.5f },
                Direction = FillDirection.Horizontal,
                Children =
                [
                    stageScoreDisplay,
                    mainFill = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Bottom = 4.8f },
                        AutoSizeAxes = Axes.Both,
                        Children =
                        [
                            titleText = new OsuSpriteText
                            {
                                Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold, italics: false),
                            },
                            artistText = new OsuSpriteText
                            {
                                Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                                Padding = new MarginPadding { Top = -2 },
                            },
                            new FillFlowContainer
                            {
                                Direction = FillDirection.Horizontal,
                                AutoSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Top = 2, Bottom = 2 },
                                Children =
                                [
                                    keyCountText = new OsuSpriteText
                                    {
                                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold, italics: false),
                                        Anchor = Anchor.BottomLeft,
                                        Origin = Anchor.BottomLeft,
                                        Alpha = 0,
                                    },
                                    difficultyText = new OsuSpriteText
                                    {
                                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold, italics: false),
                                        Anchor = Anchor.BottomLeft,
                                        Origin = Anchor.BottomLeft,
                                    },
                                ],
                            },
                            ratingRow,
                        ],
                    },
                ],
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateKeyCount());
        mods.BindValueChanged(_ => updateKeyCount(), true);
    }

    protected override void PrepareForUse()
    {
        resetModelState();
        base.PrepareForUse();

        var model = (BmsGroupedCourseStage)Item!.Model;
        stage = model.Stage;
        ResolvedBeatmap = model.Beatmap;
        Name = $"Course stage {model.StageIndex + 1} song panel";

        var metadata = ResolvedBeatmap?.BeatmapSet?.Metadata ?? ResolvedBeatmap?.Metadata;
        titleText.Text = metadata == null
            ? stage.Title
            : new RomanisableString(metadata.TitleUnicode, metadata.Title);
        artistText.Text = metadata == null
            ? stage.Artist ?? string.Empty
            : new RomanisableString(metadata.ArtistUnicode, metadata.Artist);
        difficultyText.Text = ResolvedBeatmap?.DifficultyName ?? (stage.IsAvailable ? stage.Difficulty : BmsStrings.CourseStageMissing);
        difficultyText.Colour = stage.IsAvailable ? Color4.White : Color4.OrangeRed;

        difficultyIcon.Icon = ResolvedBeatmap?.Ruleset.CreateInstance().CreateIcon()
                              ?? new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle };
        difficultyIcon.Colour = ResolvedBeatmap != null ? availableIconColour : Color4.OrangeRed;
        difficultyIcon.Alpha = ResolvedBeatmap != null ? 0 : 1;

        lamp.Alpha = ResolvedBeatmap != null ? 1 : 0;
        missingBackground.Alpha = ResolvedBeatmap != null ? 0 : 1;
        stageScoreDisplay.Beatmap = ResolvedBeatmap;
        stageScoreDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        starRatingDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        spreadDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        spreadDisplay.Beatmap.Value = ResolvedBeatmap;
        keyCountText.Alpha = 0;
        starRatingDisplay.Current.Value = default;

        if (ResolvedBeatmap != null)
            scheduledBackgroundRetrieval = Scheduler.AddDelayed(b =>
            {
                if (ReferenceEquals(ResolvedBeatmap, b))
                    panelBackground.Beatmap = beatmaps.GetWorkingBeatmap(b);
            }, ResolvedBeatmap, 50);

        computeStarRating();
        updateKeyCount();
    }

    protected override void FreeAfterUse()
    {
        resetModelState();
        base.FreeAfterUse();
    }

    private void resetModelState()
    {
        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = null;
        starDifficultyBindable = null;
        panelBackground.Beatmap = null;
        stageScoreDisplay.Beatmap = null;
        spreadDisplay.Beatmap.Value = null;
        spreadDisplay.StarDifficulty.Value = default;
        spreadDisplay.Current.Colour = Color4.White;
        starRatingDisplay.Current.Value = default;
        mainFill.Margin = new MarginPadding();
        keyCountText.Alpha = 0;
        AccentColour = defaultAccentColour;
        ResolvedBeatmap = null;
        stage = null;
    }

    protected override void Update()
    {
        base.Update();

        if (stage == null || ResolvedBeatmap == null)
            return;

        AccentColour = starRatingDisplay.DisplayedDifficultyColour;
        spreadDisplay.Current.Colour = starRatingDisplay.DisplayedDifficultyColour;
        mainFill.Margin = new MarginPadding
        {
            Left = 1 / stageScoreDisplay.Scale.X * (stageScoreDisplay.HasRank ? 0 : -3),
        };
    }

    protected override void Dispose(bool isDisposing)
    {
        starDifficultyCancellationSource?.Cancel();
        base.Dispose(isDisposing);
    }

    private void computeStarRating()
    {
        starDifficultyCancellationSource?.Cancel();

        if (ResolvedBeatmap == null)
            return;

        var targetBeatmap = ResolvedBeatmap;
        starDifficultyCancellationSource = new CancellationTokenSource();
        starDifficultyBindable = difficultyCache.GetBindableDifficulty(
            targetBeatmap,
            starDifficultyCancellationSource.Token,
            osu.Game.Screens.Select.SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(difficulty =>
        {
            if (!ReferenceEquals(ResolvedBeatmap, targetBeatmap))
                return;

            starRatingDisplay.Current.Value = difficulty.NewValue;

            spreadDisplay.StarDifficulty.Value = difficulty.NewValue;
        }, true);
    }

    private void updateKeyCount()
    {
        if (ResolvedBeatmap == null || stage == null)
            return;

        var rulesetInstance = ruleset.Value.CreateInstance();

        if (rulesetInstance.AvailableVariants.Count() > 1)
        {
            var variant = rulesetInstance.GetVariantForBeatmap(ResolvedBeatmap, mods.Value);
            keyCountText.Alpha = 1;
            keyCountText.Text = LocalisableString.Interpolate($"[{rulesetInstance.GetVariantName(variant)}] ");
        }
        else
            keyCountText.Alpha = 0;
    }

    public override MenuItem[] ContextMenuItems => [];

    protected override bool OnClick(ClickEvent e) => true;

    internal static BeatmapInfo? QueryBeatmap(BeatmapManager beatmaps, string hash) => hash.Length switch
    {
        32 => beatmaps.QueryBeatmap(info => info.MD5Hash == hash),
        64 => beatmaps.QueryBeatmap(info => info.Hash == hash),
        _ => null,
    };
}

internal partial class BmsCourseHistoryArea : VisibilityContainer
{
    private const float header_height = 35;
    private const int drawable_batch_size = 10;

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private readonly Action<ScoreInfo, BmsCourseSession?> presentScore;
    private readonly Func<bool> isActive;
    private BmsCourseResultStore? resultStore;
    private BmsCourseHistoryHeader header = null!;
    private FillFlowContainer content = null!;
    private CancellationTokenSource? refreshCancellation;
    private long refreshGeneration;
    private volatile bool refreshPending;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    internal BmsCourseHistoryArea(
        IBindable<BmsCourseDefinition?> selectedCourse,
        Action<ScoreInfo, BmsCourseSession?> presentScore,
        Func<bool> isActive)
    {
        this.selectedCourse = selectedCourse;
        this.presentScore = presentScore;
        this.isActive = isActive;
        RelativeSizeAxes = Axes.X;
        X = -150;
    }

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren =
        [
            new ShearAligningWrapper(header = new BmsCourseHistoryHeader
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.X,
                Height = header_height,
            }),
            new ShearAligningWrapper(new Container
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = header_height },
                Child = new OsuScrollContainer
                {
                    Shear = OsuGame.SHEAR,
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BeatmapLeaderboardWedge.SPACING_BETWEEN_SCORES),
                        Padding = new MarginPadding
                        {
                            Top = 5,
                            Left = 80,
                            Bottom = BeatmapLeaderboardScore.HEIGHT * 3,
                        },
                    },
                },
            })
            {
                Depth = 1,
            },
        ];

        selectedCourse.BindValueChanged(_ => requestRefresh());
        header.Sorting.BindValueChanged(_ => requestRefresh());
        header.FilterBySelectedMods.BindValueChanged(_ => requestRefresh());
        BmsRulesetRuntime.CourseResultsChanged += resultStoreChanged;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        mods.BindValueChanged(_ =>
        {
            if (header.FilterBySelectedMods.Value)
                requestRefresh();
        });
        Refresh();
    }

    protected override void Dispose(bool isDisposing)
    {
        BmsRulesetRuntime.CourseResultsChanged -= resultStoreChanged;
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        cancelPendingRefresh();
        base.Dispose(isDisposing);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, osu.Game.Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(osu.Game.Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    internal void Refresh()
    {
        refreshPending = false;
        updateResultStore();
        cancelPendingRefresh();

        var course = selectedCourse.Value;
        if (course == null || resultStore == null)
        {
            content.Clear();
            Hide();
            return;
        }

        var store = resultStore;
        var generation = refreshGeneration;
        var cancellation = refreshCancellation = new CancellationTokenSource();
        AlwaysPresent = true;
        var filterBySelectedMods = header.FilterBySelectedMods.Value;
        var selectedModAcronyms = mods.Value.Where(isFilterableMod).Select(mod => mod.Acronym).ToHashSet();
        var sorting = header.Sorting.Value;

        store.GetHistoryAsync(course.Id, cancellation.Token).ContinueWith(historyTask =>
        {
            if (historyTask.IsCanceled || cancellation.IsCancellationRequested)
                return;

            if (historyTask.IsFaulted)
            {
                handleRefreshFailure(course, store, generation, cancellation, historyTask.Exception!);
                return;
            }

            Scheduler.Add(() => beginSessionLoad(
                course,
                store,
                historyTask.Result,
                filterBySelectedMods,
                selectedModAcronyms,
                sorting,
                generation,
                cancellation));
        }, TaskScheduler.Default);
    }

    internal void RefreshIfPending()
    {
        if (refreshPending && isActive())
            Refresh();
    }

    internal void CancelPendingRefresh()
    {
        refreshPending = true;
        cancelPendingRefresh();
    }

    private void beginSessionLoad(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<BmsCourseResult> history,
        bool filterBySelectedMods,
        HashSet<string> selectedModAcronyms,
        LeaderboardSortMode sorting,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        realm.RunAsync(r =>
        {
            var entries = new List<CourseHistoryEntry>();

            foreach (var result in history)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                if (result.Attempt == null || createHistorySession(course, result.Attempt, r, cancellation.Token) is not { } session)
                    continue;

                var score = BmsCourseResultPresentation.CreateAggregateScore(session);

                if (!filterBySelectedMods || matchesSelectedMods(score, selectedModAcronyms))
                    entries.Add(new CourseHistoryEntry(session, score));
            }

            var orderedScores = entries.Select(entry => entry.Score).OrderByCriteria(sorting);
            return orderedScores.Select(score => entries.Single(entry => ReferenceEquals(entry.Score, score))).ToArray();
        }, cancellation.Token).ContinueWith(entriesTask =>
        {
            if (entriesTask.IsCanceled || cancellation.IsCancellationRequested)
                return;

            if (entriesTask.IsFaulted)
            {
                handleRefreshFailure(course, store, generation, cancellation, entriesTask.Exception!);
                return;
            }

            Scheduler.Add(() => beginDrawableLoad(course, store, entriesTask.Result, generation, cancellation));
        }, TaskScheduler.Default);
    }

    private void beginDrawableLoad(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<CourseHistoryEntry> entries,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        if (entries.Count == 0)
        {
            content.Clear();
            Hide();
            AlwaysPresent = false;
            return;
        }

        loadDrawableBatch(course, store, entries, 0, generation, cancellation);
    }

    private void loadDrawableBatch(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<CourseHistoryEntry> entries,
        int offset,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        var drawables = entries.Skip(offset).Take(drawable_batch_size).Select((entry, index) => new BeatmapLeaderboardScore(entry.Score)
        {
            Rank = offset + index + 1,
            Shear = Vector2.Zero,
            SelectedMods = { BindTarget = mods },
            Action = () => presentScore(entry.Score, entry.Session),
        }).ToArray();

        LoadComponentsAsync(drawables, loadedDrawables =>
        {
            if (!refreshIsCurrent(course, store, generation, cancellation))
                return;

            if (offset == 0)
                content.Clear();

            content.AddRange(loadedDrawables);
            Show();

            var nextOffset = offset + drawables.Length;
            if (nextOffset < entries.Count)
                Scheduler.Add(() => loadDrawableBatch(course, store, entries, nextOffset, generation, cancellation));
            else
                AlwaysPresent = false;
        }, cancellation.Token);
    }

    private void updateResultStore()
    {
        var current = BmsRulesetRuntime.CourseResults;
        if (ReferenceEquals(resultStore, current))
            return;

        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        resultStore = current;
        if (resultStore != null)
            resultStore.Changed += courseResultChanged;

    }

    private void courseResultChanged(string courseId)
    {
        if (selectedCourse.Value?.Id == courseId)
            requestRefresh();
    }

    private void resultStoreChanged() => requestRefresh();

    private void requestRefresh()
    {
        refreshPending = true;
        cancelPendingRefresh();
    }

    private void cancelPendingRefresh()
    {
        Interlocked.Increment(ref refreshGeneration);
        // Deliberately no Dispose: in-flight Realm tasks and continuations may still access
        // `cancellation.Token` after cancellation (see refreshIsCurrent), and accessing a token
        // on a disposed source throws ObjectDisposedException. The CTS has a finaliser that
        // releases its kernel handle, and refreshes are infrequent.
        refreshCancellation?.Cancel();
        refreshCancellation = null;
        AlwaysPresent = false;
    }

    private BmsCourseSession? createHistorySession(BmsCourseDefinition course, BmsCourseAttemptData? attempt, Realms.Realm scoreRealm, CancellationToken cancellationToken)
    {
        if (attempt == null || attempt.Stages.Length != course.Stages.Count)
            return null;

        var resolvedStages = new BmsResolvedCourseStage[attempt.Stages.Length];
        var restoredStages = new BmsRestoredCourseStage[attempt.Stages.Length];

        for (var i = 0; i < attempt.Stages.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var beatmap = BmsCourseStagePanel.QueryBeatmap(beatmaps, attempt.Stages[i].BeatmapHash);
            if (beatmap == null)
                return null;

            resolvedStages[i] = new BmsResolvedCourseStage(course.Stages[i], beatmap);
            ScoreInfo? stageScore = null;

            if (attempt.Stages[i].ScoreId is { } scoreId)
                stageScore = scoreRealm.Find<ScoreInfo>(scoreId) is { DeletePending: false } score ? score.DeepClone() : null;

            if (attempt.Stages[i].Status != BmsCourseStageStatus.NotPlayed && stageScore == null)
                return null;

            restoredStages[i] = new BmsRestoredCourseStage(
                attempt.Stages[i].Status,
                stageScore,
                attempt.Stages[i].EndingHealth);
        }

        var ruleset = resolvedStages[0].Beatmap.Ruleset.CreateInstance();
        var courseMods = attempt.ModAcronyms.Select(ruleset.CreateModFromAcronym).OfType<Mod>();
        return BmsCourseSession.Restore(course, resolvedStages, courseMods, attempt.GaugeType, attempt.Status, restoredStages);
    }

    private bool refreshIsCurrent(BmsCourseDefinition course, BmsCourseResultStore store, long generation, CancellationTokenSource cancellation) =>
        !IsDisposed
        && !cancellation.IsCancellationRequested
        && generation == Interlocked.Read(ref refreshGeneration)
        && ReferenceEquals(resultStore, store)
        && selectedCourse.Value?.Id == course.Id
        && isActive();

    private static bool matchesSelectedMods(ScoreInfo score, HashSet<string> selectedAcronyms)
    {
        return selectedAcronyms.SetEquals(score.Mods.Where(isFilterableMod).Select(mod => mod.Acronym));
    }

    private static void logRefreshFailure(Exception exception)
    {
        var error = exception is AggregateException aggregate ? aggregate.GetBaseException() : exception;

        if (error is not OperationCanceledException)
            BmsLogger.Error(error, $"Failed to load BMS course history: {error.Message}");
    }

    private void handleRefreshFailure(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        long generation,
        CancellationTokenSource cancellation,
        Exception exception)
    {
        logRefreshFailure(exception);
        Scheduler.Add(() =>
        {
            if (refreshIsCurrent(course, store, generation, cancellation))
                AlwaysPresent = false;
        });
    }

    private static bool isFilterableMod(Mod mod) => mod.Type != ModType.System && mod is not BmsModGauge;

    private sealed record CourseHistoryEntry(BmsCourseSession Session, ScoreInfo Score);
}

internal partial class BmsCourseHistoryHeader : CompositeDrawable
{
    private ShearedDropdown<LeaderboardSortMode> sortDropdown = null!;
    private ShearedToggleButton selectedModsToggle = null!;

    internal IBindable<LeaderboardSortMode> Sorting => sortDropdown.Current;

    internal IBindable<bool> FilterBySelectedMods => selectedModsToggle.Active;

    [BackgroundDependencyLoader]
    private void load(OsuConfigManager config)
    {
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Left = osu.Game.Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN, Right = 5 },
            Child = new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.X,
                Height = 30,
                Spacing = new Vector2(5),
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Left = 258 },
                Children =
                [
                    selectedModsToggle = new ShearedToggleButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.X,
                        Text = UserInterfaceStrings.SelectedMods,
                        Height = 30,
                        Margin = new MarginPadding { Left = -9.2f },
                    },
                    sortDropdown = new ShearedDropdown<LeaderboardSortMode>(BeatmapLeaderboardWedgeStrings.Sort)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.4f,
                        Items = Enum.GetValues<LeaderboardSortMode>(),
                    },
                    new CourseScopeDropdown
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.4f,
                        Current = { Value = BeatmapLeaderboardScope.Local },
                    },
                ],
            },
        };

        config.BindWith(OsuSetting.BeatmapLeaderboardSortMode, sortDropdown.Current);
        config.BindWith(OsuSetting.BeatmapDetailModsFilter, selectedModsToggle.Active);
    }

    private partial class CourseScopeDropdown : ShearedDropdown<BeatmapLeaderboardScope>
    {
        internal CourseScopeDropdown()
            : base(BeatmapLeaderboardWedgeStrings.Scope)
        {
            Items = [BeatmapLeaderboardScope.Local];
        }

        protected override LocalisableString GenerateItemText(BeatmapLeaderboardScope item) => item.GetLocalisableDescription();
    }
}

internal partial class BmsCourseNoResultsPlaceholder : VisibilityContainer
{
    internal LocalisableString Message
    {
        set => message.Text = value;
    }

    private OsuSpriteText message = null!;

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        InternalChild = new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Width = 360,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children =
            [
                new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Margin = new MarginPadding(10),
                    Size = new Vector2(50),
                    Child = new GhostIcon
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                },
                message = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    Font = OsuFont.Style.Heading1,
                    Text = BmsStrings.NoCoursesAvailable,
                },
            ],
        };
    }

    protected override void PopIn() => this.FadeIn(600, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
}

internal sealed partial class BmsCourseWedgeBackground : InputBlockingContainer
{
    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren =
        [
            new Box
            {
                Blending = BlendingParameters.Additive,
                RelativeSizeAxes = Axes.Both,
                Width = 0.6f,
                Alpha = 0.5f,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0)),
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.7f,
                Colour = colourProvider.Background5.Opacity(0.9f),
            },
            new Box
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                RelativeSizeAxes = Axes.Both,
                Width = 0.3f,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(0.9f), colourProvider.Background5.Opacity(0.6f)),
            },
        ];
    }
}
