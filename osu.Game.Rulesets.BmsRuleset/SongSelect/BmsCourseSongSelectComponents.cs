using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Play.Leaderboards;
using Realms;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

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
    }

    private static Drawable unShear(Drawable drawable)
    {
        drawable.Shear = -OsuGame.SHEAR;
        return new ShearAligningWrapper(drawable);
    }
}

internal partial class BmsCourseStagePanel : Panel
{
    private readonly BmsCourseStage stage;
    private OsuSpriteText keyCountText = null!;
    private FillFlowContainer mainFill = null!;
    private BmsCourseStageScoreDisplay? stageScoreDisplay;
    private StarRatingDisplay? starRatingDisplay;
    private PanelBeatmapStandalone.SpreadDisplay? spreadDisplay;
    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

    internal BeatmapInfo? ResolvedBeatmap { get; private set; }

    internal BmsCourseStagePanel(int index, BmsCourseStage stage)
    {
        this.stage = stage;
        Name = $"Course stage {index} song panel";
        PanelXOffset = 40;
    }

    [BackgroundDependencyLoader]
    private void load(BeatmapManager beatmaps, OverlayColourProvider colourProvider)
    {
        Height = PanelBeatmapStandalone.HEIGHT;
        AccentColour = colourProvider.Highlight1;

        if (stage.IsAvailable && !string.IsNullOrEmpty(stage.BeatmapHash))
        {
            var hash = stage.BeatmapHash;
            ResolvedBeatmap = QueryBeatmap(beatmaps, hash);
        }

        Icon = new ConstrainedIconContainer
        {
            Icon = ResolvedBeatmap != null
                ? ResolvedBeatmap.Ruleset.CreateInstance().CreateIcon()
                : new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle },
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = ResolvedBeatmap != null ? colourProvider.Background5 : Color4.OrangeRed,
            Alpha = ResolvedBeatmap != null ? 0 : 1,
            AlwaysPresent = true,
        };

        Drawable scoreDisplay;

        if (ResolvedBeatmap != null)
        {
            var lamp = new BmsLampDisplay(BmsLamp.NoPlay)
            {
                RelativeSizeAxes = Axes.Both,
                Size = Vector2.One,
            };
            Background = lamp;
            scoreDisplay = stageScoreDisplay = new BmsCourseStageScoreDisplay(ResolvedBeatmap, lamp)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Scale = new Vector2(0.8f),
            };
        }
        else
        {
            Background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider.Highlight1,
            };
            scoreDisplay = new Container();
        }

        var metadata = ResolvedBeatmap?.BeatmapSet?.Metadata ?? ResolvedBeatmap?.Metadata;
        var panelBackground = new PanelSetBackground();

        if (ResolvedBeatmap != null)
            panelBackground.Beatmap = beatmaps.GetWorkingBeatmap(ResolvedBeatmap);

        var ratingRow = new FillFlowContainer
        {
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(3),
            AutoSizeAxes = Axes.Both,
        };

        if (ResolvedBeatmap != null)
        {
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
                    Beatmap = { Value = ResolvedBeatmap },
                    Selected = { BindTarget = Selected },
                },
            ];
        }

        var difficultyText = ResolvedBeatmap?.DifficultyName ?? stage.Difficulty;

        Content.Children =
        [
            panelBackground,
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
                    scoreDisplay,
                    mainFill = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Bottom = 4.8f },
                        AutoSizeAxes = Axes.Both,
                        Children =
                        [
                            new OsuSpriteText
                            {
                                Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold, italics: false),
                                Text = metadata == null
                                    ? stage.Title
                                    : new RomanisableString(metadata.TitleUnicode, metadata.Title),
                            },
                            new OsuSpriteText
                            {
                                Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold, italics: false),
                                Padding = new MarginPadding { Top = -2 },
                                Text = metadata == null
                                    ? stage.Artist ?? string.Empty
                                    : new RomanisableString(metadata.ArtistUnicode, metadata.Artist),
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
                                    new OsuSpriteText
                                    {
                                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold, italics: false),
                                        Anchor = Anchor.BottomLeft,
                                        Origin = Anchor.BottomLeft,
                                        Colour = stage.IsAvailable ? Color4.White : Color4.OrangeRed,
                                        Text = stage.IsAvailable ? difficultyText : BmsStrings.CourseStageMissing,
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
        computeStarRating();
    }

    protected override void Update()
    {
        base.Update();

        if (starRatingDisplay == null)
            return;

        AccentColour = starRatingDisplay.DisplayedDifficultyColour;
        spreadDisplay!.Current.Colour = starRatingDisplay.DisplayedDifficultyColour;
        mainFill.Margin = new MarginPadding
        {
            Left = 1 / stageScoreDisplay!.Scale.X * (stageScoreDisplay.HasRank ? 0 : -3),
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

        if (ResolvedBeatmap == null || starRatingDisplay == null)
            return;

        starDifficultyCancellationSource = new CancellationTokenSource();
        starDifficultyBindable = difficultyCache.GetBindableDifficulty(
            ResolvedBeatmap,
            starDifficultyCancellationSource.Token,
            osu.Game.Screens.Select.SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(difficulty =>
        {
            starRatingDisplay.Current.Value = difficulty.NewValue;

            if (spreadDisplay != null)
                spreadDisplay.StarDifficulty.Value = difficulty.NewValue;
        }, true);
    }

    private void updateKeyCount()
    {
        if (ResolvedBeatmap == null)
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

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private readonly Action<ScoreInfo, BmsCourseSession?> presentScore;
    private BmsCourseResultStore? resultStore;
    private IDisposable? scoreSubscription;
    private BmsCourseHistoryHeader header = null!;
    private FillFlowContainer content = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    internal BmsCourseHistoryArea(IBindable<BmsCourseDefinition?> selectedCourse, Action<ScoreInfo, BmsCourseSession?> presentScore)
    {
        this.selectedCourse = selectedCourse;
        this.presentScore = presentScore;
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

        selectedCourse.BindValueChanged(_ => Refresh());
        header.Sorting.BindValueChanged(_ => Refresh());
        header.FilterBySelectedMods.BindValueChanged(_ => Refresh());
        scoreSubscription = realm.RegisterForNotifications(r => r.All<ScoreInfo>(), scoresChanged);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        mods.BindValueChanged(_ =>
        {
            if (header.FilterBySelectedMods.Value)
                Refresh();
        });
        Refresh();
    }

    protected override void Dispose(bool isDisposing)
    {
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        scoreSubscription?.Dispose();

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
        updateResultStore();
        content.Clear();

        var course = selectedCourse.Value;
        if (course == null || resultStore == null)
        {
            Hide();
            return;
        }

        var scores = resultStore.GetHistory(course.Id)
            .Where(result => result.Attempt != null)
            .Select(result => (Result: result, Session: createHistorySession(course, result.Attempt)))
            .Where(entry => entry.Session != null)
            .Select(entry => (entry.Result, Session: entry.Session!, Score: BmsCourseResultPresentation.CreateAggregateScore(entry.Session!)))
            .ToArray();

        if (scores.Length == 0)
        {
            Hide();
            return;
        }

        if (header.FilterBySelectedMods.Value)
            scores = scores.Where(entry => matchesSelectedMods(entry.Score)).ToArray();

        var orderedScores = scores.Select(entry => entry.Score).OrderByCriteria(header.Sorting.Value);

        content.AddRange(orderedScores.Select((score, index) => new BeatmapLeaderboardScore(score)
        {
            Rank = index + 1,
            Shear = Vector2.Zero,
            SelectedMods = { BindTarget = mods },
            Action = () =>
            {
                var entry = scores.Single(entry => ReferenceEquals(entry.Score, score));
                presentScore(score, entry.Session);
            },
        }));
        Show();
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
            Scheduler.Add(Refresh);
    }

    private void scoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        if (changes?.HasCollectionChanges() == false)
            return;

        if (selectedCourse.Value != null)
            Scheduler.Add(Refresh);
    }

    private BmsCourseSession? createHistorySession(BmsCourseDefinition course, BmsCourseAttemptData? attempt)
    {
        if (attempt == null || attempt.Stages.Length != course.Stages.Count)
            return null;

        var resolvedStages = new BmsResolvedCourseStage[attempt.Stages.Length];
        var restoredStages = new BmsRestoredCourseStage[attempt.Stages.Length];

        for (var i = 0; i < attempt.Stages.Length; i++)
        {
            var beatmap = BmsCourseStagePanel.QueryBeatmap(beatmaps, attempt.Stages[i].BeatmapHash);
            if (beatmap == null)
                return null;

            resolvedStages[i] = new BmsResolvedCourseStage(course.Stages[i], beatmap);
            ScoreInfo? stageScore = null;

            if (attempt.Stages[i].ScoreId is { } scoreId)
                stageScore = realm.Run(r => r.Find<ScoreInfo>(scoreId) is { DeletePending: false } score ? score.DeepClone() : null);

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

    private bool matchesSelectedMods(ScoreInfo score)
    {
        var selectedAcronyms = mods.Value.Where(isFilterableMod).Select(mod => mod.Acronym).ToHashSet();
        return selectedAcronyms.SetEquals(score.Mods.Where(isFilterableMod).Select(mod => mod.Acronym));
    }

    private static bool isFilterableMod(Mod mod) => mod.Type != ModType.System && mod is not BmsModGauge;
}

internal partial class BmsCourseHistoryHeader : CompositeDrawable
{
    private ShearedDropdown<BeatmapLeaderboardScope> scopeDropdown = null!;
    private ShearedDropdown<LeaderboardSortMode> sortDropdown = null!;
    private ShearedToggleButton selectedModsToggle = null!;

    internal IBindable<BeatmapLeaderboardScope> Scope => scopeDropdown.Current;

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
                    scopeDropdown = new CourseScopeDropdown
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
        get => message.Text;
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
