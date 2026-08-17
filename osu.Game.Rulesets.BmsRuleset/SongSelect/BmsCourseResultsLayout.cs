using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Play.HUD;
using osu.Game.Users;
using osu.Game.Users.Drawables;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseResultsLayout : CompositeDrawable
{
    internal BmsCourseAggregateStatistics AggregateStatistics { get; }

    internal BmsCourseResultsLayout(BmsCourseSession session, Bindable<int?> selectedStage)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChildren =
        [
            new BmsCourseSummaryCard(session, selectedStage)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = StatisticsPanel.SIDE_PADDING,
                Y = -25,
            },
            new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = AggregateStatistics = new BmsCourseAggregateStatistics(session)
                {
                    RelativeSizeAxes = Axes.Both,
                },
            },
        ];
    }
}

internal partial class BmsCourseSummaryCard : CompositeDrawable
{
    private const float expanded_height = 586;
    private const float expanded_top_layer_height = 53;
    private const float vertical_fudge = 20;

    internal BmsCourseSummaryCard(BmsCourseSession session, Bindable<int?> selectedStage)
    {
        Size = new Vector2(ScorePanel.EXPANDED_WIDTH, expanded_height);
        var aggregate = BmsCourseResultPresentation.CreateAggregateScore(session);

        InternalChild = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Y = vertical_fudge,
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                new Container
                {
                    Name = "Top layer",
                    RelativeSizeAxes = Axes.X,
                    Height = 120,
                    Y = -expanded_top_layer_height / 2,
                    Children =
                    [
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            CornerRadius = 20,
                            CornerExponent = 2.5f,
                            Masking = true,
                            Child = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(Color4Extensions.FromHex("#444"), Color4Extensions.FromHex("#333")),
                            },
                        },
                        new ExpandedPanelTopContent(aggregate.User),
                    ],
                },
                new Container
                {
                    Name = "Middle layer",
                    RelativeSizeAxes = Axes.Both,
                    Y = expanded_top_layer_height / 2,
                    Children =
                    [
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            CornerRadius = 20,
                            CornerExponent = 2.5f,
                            Masking = true,
                            Children =
                            [
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = ColourInfo.GradientVertical(Color4Extensions.FromHex("#555"), Color4Extensions.FromHex("#333")),
                                },
                                new UserCoverBackground
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    User = aggregate.User,
                                    Colour = ColourInfo.GradientVertical(Color4.White.Opacity(0.5f), Color4Extensions.FromHex("#444").Opacity(0)),
                                },
                                new BmsCourseScoreMiddleContent(session, aggregate, selectedStage),
                            ],
                        },
                    ],
                },
            ],
        };
    }
}

internal partial class BmsCourseScoreMiddleContent : CompositeDrawable
{
    private readonly List<StatisticDisplay> statistics = new();

    internal BmsCourseScoreMiddleContent(BmsCourseSession session, ScoreInfo aggregate, Bindable<int?> selectedStage)
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        Padding = new MarginPadding(10);

        var topStatistics = new StatisticDisplay[]
        {
            new BmsCourseAccuracyStatistic(aggregate.Accuracy),
            new BmsCourseExScoreStatistic(aggregate),
            new ComboStatistic(aggregate.MaxCombo, aggregate.GetMaximumAchievableCombo()),
        };
        statistics.AddRange(topStatistics);

        var hitStatistics = aggregate.GetStatisticsForDisplay()
                                     .Select(result => (StatisticDisplay)new HitResultStatistic(result))
                                     .ToArray();
        statistics.AddRange(hitStatistics);

        InternalChild = new OsuScrollContainer
        {
            RelativeSizeAxes = Axes.Both,
            ScrollbarVisible = false,
            Padding = new MarginPadding { Bottom = aggregate.Date == default ? 0 : 16 },
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(8),
                Children =
                [
                    new BmsCourseMetadataHeader(session, selectedStage),
                    new BmsCourseStageCardList(session, selectedStage)
                    {
                        Margin = new MarginPadding { Top = 6 },
                    },
                    new FillFlowContainer
                    {
                        Name = "Course ruleset and mods",
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        AutoSizeAxes = Axes.Both,
                        Margin = new MarginPadding { Vertical = 6 },
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(5),
                        Children =
                        [
                            new DifficultyIcon(aggregate.BeatmapInfo!, aggregate.Ruleset)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(20),
                                TooltipType = DifficultyIconTooltipType.None,
                            },
                            new ModDisplay
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                ExpansionMode = ExpansionMode.AlwaysExpanded,
                                Current = { Value = aggregate.Mods },
                                Scale = new Vector2(0.5f),
                            },
                        ],
                    },
                    new GridContainer
                    {
                        Name = "Course hit statistics",
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        RowDimensions = [new Dimension(GridSizeMode.AutoSize)],
                        Content = new[] { topStatistics.Cast<Drawable>().ToArray() },
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        RowDimensions = [new Dimension(GridSizeMode.AutoSize)],
                        Content = new[] { hitStatistics.Cast<Drawable>().ToArray() },
                    },
                ],
            },
        };

        if (aggregate.Date != default)
        {
            AddInternal(new PlayedOnText(aggregate.Date, true)
            {
                Name = "Course played time",
            });
        }
    }

    [BackgroundDependencyLoader]
    private void load() => ScheduleAfterChildren(() => statistics.ForEach(statistic => statistic.Appear()));
}

internal partial class BmsCourseMetadataHeader : OsuClickableContainer
{
    internal BmsCourseMetadataHeader(BmsCourseSession session, Bindable<int?> selectedStage)
    {
        Name = "Course summary result";
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Action = () => selectedStage.Value = null;
        Children =
        [
            new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children =
                [
                    new TruncatingSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        MaxWidth = ScorePanel.EXPANDED_WIDTH - 20,
                        Font = OsuFont.Torus.With(size: 20, weight: FontWeight.SemiBold),
                        Text = session.Course.Name,
                    },
                    new TruncatingSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        MaxWidth = ScorePanel.EXPANDED_WIDTH - 20,
                        Font = OsuFont.Torus.With(size: 14, weight: FontWeight.SemiBold),
                        Text = session.Course.TableName,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.Torus.With(size: 11, weight: FontWeight.SemiBold),
                        Text = BmsCourseResultPresentation.StatusText(session.Status),
                        Colour = BmsCourseResultPresentation.StatusColour(session.Status),
                    },
                ],
            },
        ];
    }
}

internal partial class BmsCourseStageCardList : CompositeDrawable
{
    internal BmsCourseStageCardList(BmsCourseSession session, Bindable<int?> selectedStage)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = session.Stages.Select((stage, index) => (Drawable)new BmsCourseStageCard(stage, index, selectedStage)).ToArray(),
        };
    }
}

internal partial class BmsCourseBeatmapBackground : CompositeDrawable
{
    private readonly Sprite fallback;

    internal BmsCourseBeatmapBackground(IBeatmapInfo beatmap)
    {
        InternalChildren =
        [
            fallback = new Sprite
            {
                Name = "Course beatmap fallback background",
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fill,
            },
            new UpdateableBeatmapBackgroundSprite
            {
                RelativeSizeAxes = Axes.Both,
                BackgroundLoadDelay = 0,
                Beatmap = { Value = beatmap },
            },
        ];
    }

    [BackgroundDependencyLoader]
    private void load(LargeTextureStore textures) => fallback.Texture = textures.Get(@"Backgrounds/bg2");
}

internal partial class BmsCourseStageCard : OsuClickableContainer
{
    private const float statistics_scale = 0.56f;

    private readonly int stageIndex;
    private readonly Bindable<int?> selectedStage;
    private readonly List<StatisticDisplay> statistics = new();
    private Box background = null!;

    [Resolved]
    private OverlayColourProvider colours { get; set; } = null!;

    internal BmsCourseStageCard(BmsCourseStageAttempt attempt, int stageIndex, Bindable<int?> selectedStage)
    {
        this.stageIndex = stageIndex;
        this.selectedStage = selectedStage;

        RelativeSizeAxes = Axes.X;
        Height = 72;
        CornerRadius = 7;
        CornerExponent = 2.5f;
        Masking = true;
        Name = $"Course stage {stageIndex + 1} result";
        Action = attempt.Score == null ? null : () => selectedStage.Value = selectedStage.Value == stageIndex ? null : stageIndex;

        var metadata = attempt.Stage.Beatmap.BeatmapSet?.Metadata ?? attempt.Stage.Beatmap.Metadata;

        Children =
        [
            new Box
            {
                Name = "Stage status background",
                RelativeSizeAxes = Axes.Both,
                Colour = BmsCourseResultPresentation.StageStatusColour(attempt.Status),
            },
            new Container
            {
                Name = "Stage cover",
                RelativeSizeAxes = Axes.Y,
                Width = 72,
                CornerRadius = 7,
                CornerExponent = 2.5f,
                Masking = true,
                Child = new BmsCourseBeatmapBackground(attempt.Stage.Beatmap)
                {
                    RelativeSizeAxes = Axes.Both,
                },
            },
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
            },
            new Container
            {
                Name = "Stage details panel",
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -8,
                RelativeSizeAxes = Axes.Y,
                Width = 270,
                CornerRadius = 7,
                CornerExponent = 2.5f,
                Masking = true,
                Children =
                [
                    new BmsCourseBeatmapBackground(attempt.Stage.Beatmap)
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = ScorePanel.EXPANDED_WIDTH - 20,
                        X = -62,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(
                            Color4Extensions.FromHex("#4b565d").Opacity(0.64f),
                            Color4Extensions.FromHex("#30383d").Opacity(0.74f)),
                    },
                    createMetadata(metadata),
                    attempt.Score == null ? Empty() : createScoreStatistics(attempt.Score),
                ],
            },
        ];
    }

    private static Drawable createMetadata(IBeatmapMetadataInfo metadata) => new Container
    {
        Name = "Stage metadata",
        RelativeSizeAxes = Axes.X,
        Height = 32,
        Padding = new MarginPadding { Left = 18, Right = 8, Top = 5 },
        Child = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children =
            [
                new TruncatingSpriteText
                {
                    RelativeSizeAxes = Axes.X,
                    Font = OsuFont.Torus.With(size: 13, weight: FontWeight.SemiBold),
                    Text = metadata.Title,
                },
                new TruncatingSpriteText
                {
                    RelativeSizeAxes = Axes.X,
                    Font = OsuFont.Torus.With(size: 10, weight: FontWeight.SemiBold),
                    Text = metadata.Artist,
                    Alpha = 0.8f,
                },
            ],
        },
    };

    private Drawable createScoreStatistics(ScoreInfo score)
    {
        StatisticDisplay[] topStatistics =
        [
            new BmsCourseAccuracyStatistic(score.Accuracy),
            new BmsCourseExScoreStatistic(score),
            new ComboStatistic(score.MaxCombo, score.GetMaximumAchievableCombo()),
        ];
        var hitStatistics = score.GetStatisticsForDisplay()
                                 .Select(result => (StatisticDisplay)new HitResultStatistic(result))
                                 .ToArray();
        statistics.AddRange(topStatistics);
        statistics.AddRange(hitStatistics);

        return new Container
        {
            Name = "Stage score statistics",
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Height = 40,
            Padding = new MarginPadding { Left = 18, Right = 8, Bottom = 3 },
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                Width = 1 / statistics_scale,
                AutoSizeAxes = Axes.Y,
                Scale = new Vector2(statistics_scale),
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children =
                [
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        RowDimensions = [new Dimension(GridSizeMode.AutoSize)],
                        Content = new[] { topStatistics.Cast<Drawable>().ToArray() },
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        RowDimensions = [new Dimension(GridSizeMode.AutoSize)],
                        Content = new[] { hitStatistics.Cast<Drawable>().ToArray() },
                    },
                ],
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        selectedStage.BindValueChanged(selectionChanged, true);
        ScheduleAfterChildren(() => statistics.ForEach(statistic => statistic.Appear()));
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateColours();
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        updateColours();
        base.OnHoverLost(e);
    }

    protected override void Dispose(bool isDisposing)
    {
        selectedStage.ValueChanged -= selectionChanged;
        base.Dispose(isDisposing);
    }

    private void selectionChanged(ValueChangedEvent<int?> _) => updateColours();

    private void updateColours()
    {
        var selected = selectedStage.Value == stageIndex;
        background.Colour = selected ? colours.Background1 : colours.Background2;
        background.Alpha = selected ? 0.35f : IsHovered ? 0.18f : 0;
    }
}

internal partial class BmsCourseAccuracyStatistic : StatisticDisplay
{
    private readonly double accuracy;

    internal BmsCourseAccuracyStatistic(double accuracy)
        : base("ACC")
    {
        this.accuracy = accuracy;
    }

    protected override Drawable CreateContent() => new OsuSpriteText
    {
        Font = OsuFont.Torus.With(size: 20, fixedWidth: true),
        Spacing = new Vector2(-2, 0),
        Text = accuracy.FormatAccuracy(),
    };
}

internal partial class BmsCourseExScoreStatistic : StatisticDisplay
{
    private readonly int exScore;
    private readonly int maximumExScore;

    internal BmsCourseExScoreStatistic(ScoreInfo score)
        : base("EXSCORE")
    {
        maximumExScore = BmsExScore.Calculate(score.MaximumStatistics);
        exScore = BmsExScore.Calculate(score, maximumExScore);
    }

    protected override Drawable CreateContent()
    {
        var perfect = maximumExScore > 0 && exScore == maximumExScore;

        return new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Colour = perfect
                ? ColourInfo.GradientVertical(Color4Extensions.FromHex("#66FFCC"), Color4Extensions.FromHex("#FF9AD7"))
                : Color4.White,
            Children =
            [
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Font = OsuFont.Torus.With(size: 20, fixedWidth: true),
                    Spacing = new Vector2(-2, 0),
                    Text = $"{exScore:N0}",
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Font = OsuFont.Torus.With(size: 12, fixedWidth: true),
                    Spacing = new Vector2(-2, 0),
                    Text = $"/{maximumExScore:N0}",
                },
            ],
        };
    }
}

internal partial class BmsCourseAggregateStatistics : StatisticsPanel
{
    protected override bool StartHidden => false;

    private readonly BmsCourseSession session;
    private Task<BmsCourseStageStatisticData[]> courseData = null!;

    internal BmsCourseAggregateStatistics(BmsCourseSession session)
    {
        this.session = session;
    }

    [BackgroundDependencyLoader]
    private void load(BeatmapManager beatmapManager)
    {
        var stages = session.Stages.Where(stage => stage.Score != null)
                            .Select(stage => (Score: stage.Score!, WorkingBeatmap: beatmapManager.GetWorkingBeatmap(stage.Stage.Beatmap, true)))
                            .ToArray();

        courseData = Task.Run(() => stages.Select(stage => new BmsCourseStageStatisticData(
            stage.Score,
            stage.WorkingBeatmap.GetPlayableBeatmap(stage.Score.Ruleset, stage.Score.Mods))).ToArray());

        Score.Value = BmsCourseResultPresentation.CreateAggregateScore(session);
    }

    protected override IEnumerable<StatisticItem> CreateStatisticItems(ScoreInfo newScore, IBeatmap playableBeatmap)
    {
        yield return createItem(BmsStrings.GaugeHistory, data => new BmsGaugeHistoryGraph(
            data.Select(stage => (stage.Score, stage.Beatmap)).ToArray()));
        yield return createItem(BmsStrings.Timeline, data => new BmsTimelineStatistic(
            data.Select(stage => (stage.Score, stage.Beatmap)).ToArray()));
        yield return createItem(BmsStrings.HitScatter, data => new BmsHitScatterStatistic(
            data.Select(stage => (stage.Beatmap, (IReadOnlyList<HitEvent>)stage.Score.HitEvents)).ToArray()));
        yield return createItem(BmsStrings.HitOffset, data => new BmsHitOffsetStatistic(
            data.Select(stage => (stage.Beatmap, (IReadOnlyList<HitEvent>)stage.Score.HitEvents)).ToArray()));
    }

    private StatisticItem createItem(LocalisableString name, Func<BmsCourseStageStatisticData[], Drawable> createContent) =>
        new(name, () => new BmsCourseDeferredStatistic(courseData, createContent), requiresHitEvents: false);
}

internal sealed record BmsCourseStageStatisticData(ScoreInfo Score, IBeatmap Beatmap);

internal partial class BmsCourseDeferredStatistic : CompositeDrawable
{
    private readonly Task<BmsCourseStageStatisticData[]> data;
    private readonly Func<BmsCourseStageStatisticData[], Drawable> createContent;

    internal BmsCourseDeferredStatistic(Task<BmsCourseStageStatisticData[]> data, Func<BmsCourseStageStatisticData[], Drawable> createContent)
    {
        this.data = data;
        this.createContent = createContent;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        data.ContinueWith(task => Schedule(() =>
        {
            if (!task.IsCompletedSuccessfully)
                return;

            LoadComponentAsync(createContent(task.Result), drawable => InternalChild = drawable);
        }));
    }
}

internal static class BmsCourseResultPresentation
{
    internal static ScoreInfo CreateAggregateScore(BmsCourseSession session)
    {
        var played = session.Stages.Where(stage => stage.Score != null).Select(stage => stage.Score!).ToArray();
        var first = played.FirstOrDefault();
        var beatmap = first?.BeatmapInfo ?? session.Stages[0].Stage.Beatmap;
        var totalWeight = played.Sum(scoreWeight);
        var accuracy = totalWeight == 0 ? 0 : played.Sum(score => score.Accuracy * scoreWeight(score)) / totalWeight;
        var statistics = sumStatistics(played.Select(score => score.Statistics));
        var rank = session.Status == BmsCourseStatus.Passed
            ? new BmsScoreProcessor().RankFromScore(accuracy, statistics)
            : ScoreRank.F;

        return new ScoreInfo
        {
            User = first?.User ?? new APIUser(),
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = beatmap.Ruleset,
            Passed = session.Status == BmsCourseStatus.Passed,
            Rank = rank,
            TotalScore = played.Sum(score => score.TotalScore),
            TotalScoreWithoutMods = played.Sum(score => score.TotalScoreWithoutMods),
            Accuracy = accuracy,
            MaxCombo = played.Sum(score => score.MaxCombo),
            PP = played.All(score => score.PP.HasValue) ? played.Sum(score => score.PP!.Value) : null,
            Mods = session.Mods.Select(mod => mod.DeepClone()).ToArray(),
            Statistics = statistics,
            MaximumStatistics = sumStatistics(played.Select(score => score.MaximumStatistics)),
            Date = played.Select(score => score.Date).DefaultIfEmpty().Max(),
        };
    }

    private static Dictionary<HitResult, int> sumStatistics(IEnumerable<IReadOnlyDictionary<HitResult, int>> statistics) =>
        statistics.SelectMany(values => values)
                  .GroupBy(value => value.Key)
                  .ToDictionary(group => group.Key, group => group.Sum(value => value.Value));

    private static int scoreWeight(ScoreInfo score)
    {
        var maximum = score.MaximumStatistics.Values.Sum();
        return maximum > 0 ? maximum : score.Statistics.Values.Sum();
    }

    internal static LocalisableString StatusText(BmsCourseStatus status) => status switch
    {
        BmsCourseStatus.Passed => BmsStrings.CoursePassed,
        BmsCourseStatus.Failed => BmsStrings.CourseFailed,
        BmsCourseStatus.Aborted => BmsStrings.CourseAborted,
        _ => BmsStrings.Courses,
    };

    internal static Colour4 StatusColour(BmsCourseStatus status) => status switch
    {
        BmsCourseStatus.Passed => Colour4.LimeGreen,
        BmsCourseStatus.Failed => Colour4.OrangeRed,
        BmsCourseStatus.Aborted => Colour4.Gold,
        _ => Colour4.White,
    };

    internal static Colour4 StageStatusColour(BmsCourseStageStatus status) => status switch
    {
        BmsCourseStageStatus.Passed => Colour4.White,
        BmsCourseStageStatus.Failed => Colour4.Red,
        BmsCourseStageStatus.Aborted => Colour4.LightCoral,
        _ => Colour4.Gray,
    };
}
