using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsScoreGraph : BmsHudComponent
{
    private const float score_row_height = 28;
    private const float judgement_table_height = 72;
    private const float footer_bottom_padding = 6;
    private const float footer_gap = 4;
    private const float plot_top_padding = 8;
    private const float minimum_plot_height = 62;
    private const float horizontal_padding = 12;
    private const float minimum_width = 160;
    private const float absolute_minimum_height = footer_bottom_padding + plot_top_padding + minimum_plot_height;

    private static readonly ScoreRank[] displayed_ranks =
    [
        ScoreRank.X,
        ScoreRank.S,
        ScoreRank.A,
        ScoreRank.B,
        ScoreRank.C,
    ];

    private static readonly HitResult[] displayed_judgements =
    [
        HitResult.Perfect,
        HitResult.Great,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Meh,
        HitResult.Miss,
    ];

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphCurrentColour), nameof(BmsStrings.ScoreGraphCurrentColourDescription))]
    public BindableColour4 CurrentColour { get; } = new(new Color4(45, 155, 255, 255));

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphPersonalBestColour), nameof(BmsStrings.ScoreGraphPersonalBestColourDescription))]
    public BindableColour4 PersonalBestColour { get; } = new(new Color4(55, 210, 105, 255));

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphTargetColour), nameof(BmsStrings.ScoreGraphTargetColourDescription))]
    public BindableColour4 TargetColour { get; } = new(new Color4(255, 70, 75, 255));

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphShowBars), nameof(BmsStrings.ScoreGraphShowBarsDescription))]
    public BindableBool ShowBars { get; } = new(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphShowScoreDifference), nameof(BmsStrings.ScoreGraphShowScoreDifferenceDescription))]
    public BindableBool ShowScoreDifference { get; } = new(true);

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ScoreGraphShowJudgementComparison), nameof(BmsStrings.ScoreGraphShowJudgementComparisonDescription))]
    public BindableBool ShowJudgementComparison { get; } = new(true);

    private readonly Container plotContainer;
    private readonly Container currentScoreRow;
    private readonly Container personalBestScoreRow;
    private readonly Container targetScoreRow;
    private readonly Container judgementComparison;
    private readonly ScoreBar currentBar;
    private readonly ScoreBar personalBestBar;
    private readonly ScoreBar targetBar;
    private readonly RankLine[] rankLines;
    private readonly OsuSpriteText currentScoreText;
    private readonly OsuSpriteText personalBestScoreText;
    private readonly OsuSpriteText personalBestDifferenceText;
    private readonly OsuSpriteText targetLabelText;
    private readonly OsuSpriteText targetScoreText;
    private readonly OsuSpriteText targetDifferenceText;
    private readonly Dictionary<HitResult, JudgementCountTexts> judgementCountTexts = [];

    private BmsScoreProcessor? scoreProcessor;
    private int maximumExScore = 2000;
    private int totalScoringEvents = 1000;
    private int personalBestFinalScore = 1300;
    private int targetFinalScore = 1334;
    private ScoreRank targetRank = ScoreRank.B;
    private int[] personalBestProgression = [];
    private readonly JudgementProgressCursor personalBestJudgementProgress = new();
    private readonly int[] displayedCurrentJudgementCounts = new int[displayed_judgements.Length];
    private IReadOnlyList<int>? displayedPersonalBestJudgementCounts;
    private int displayedCurrentScore;
    private int displayedJudgedEvents;
    private bool currentDisplayValid;

    [Resolved(CanBeNull = true)]
    private GameplayState? gameplayState { get; set; }

    [Resolved(CanBeNull = true)]
    private RealmAccess? realm { get; set; }

    [Resolved(CanBeNull = true)]
    private ScoreManager? scoreManager { get; set; }

    [Resolved(CanBeNull = true)]
    private DrawableRuleset? drawableRuleset { get; set; }

    public override Vector2 Size
    {
        get => base.Size;
        set => base.Size = new Vector2(Math.Max(minimum_width, value.X), Math.Max(applicableMinimumHeight, value.Y));
    }

    public override float Width
    {
        get => base.Width;
        set => base.Width = Math.Max(minimum_width, value);
    }

    public override float Height
    {
        get => base.Height;
        set => base.Height = Math.Max(applicableMinimumHeight, value);
    }

    private float applicableMinimumHeight => IsLoaded
        ? CalculateMinimumHeight(ShowBars.Value, ShowScoreDifference.Value, ShowJudgementComparison.Value)
        : absolute_minimum_height;

    public BmsScoreGraph()
    {
        OsuSpriteText personalBestJudgementHeader1;
        OsuSpriteText currentJudgementHeader1;
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.CentreLeft;
        X = 12;
        Size = new Vector2(196, 480);

        rankLines = displayed_ranks.Select(rank => new RankLine(rank)).ToArray();
        var judgementRows = createJudgementRows();
        var plotBackground = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(7, 9, 12, 255),
        };
        var judgementBackground = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = new Color4(18, 22, 27, 255),
        };

        InternalChildren =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(13, 16, 20, 242),
            },
            plotContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Top = plot_top_padding,
                    Left = horizontal_padding,
                    Right = horizontal_padding,
                },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children =
                    [
                        plotBackground,
                        currentBar = new ScoreBar(showGhost: false, column: 0),
                        personalBestBar = new ScoreBar(showGhost: true, column: 1),
                        targetBar = new ScoreBar(showGhost: true, column: 2),
                        .. rankLines,
                    ],
                },
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding
                {
                    Left = horizontal_padding,
                    Right = horizontal_padding,
                },
                Children =
                [
                    currentScoreRow = createScoreRow(
                        BmsStrings.ScoreGraphCurrent,
                        showDifference: false,
                        out var currentAccent,
                        out currentScoreText,
                        out _),
                    personalBestScoreRow = createScoreRow(
                        BmsStrings.ScoreGraphPersonalBest,
                        showDifference: true,
                        out var personalBestAccent,
                        out personalBestScoreText,
                        out personalBestDifferenceText),
                    targetScoreRow = createScoreRow(
                        BmsStrings.ScoreGraphTarget(targetRank),
                        showDifference: true,
                        out var targetAccent,
                        out targetScoreText,
                        out targetDifferenceText,
                        out targetLabelText),
                    judgementComparison = new Container
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = judgement_table_height,
                        Children =
                        [
                            judgementBackground,
                            currentJudgementHeader1 = new OsuSpriteText
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Position = new Vector2(-54, 2),
                                Text = BmsStrings.ScoreGraphCurrent,
                                Font = OsuFont.GetFont(size: 8, weight: FontWeight.SemiBold),
                                Colour = CurrentColour.Value,
                            },
                            personalBestJudgementHeader1 = new OsuSpriteText
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Position = new Vector2(-4, 2),
                                Text = BmsStrings.ScoreGraphPersonalBestShort,
                                Font = OsuFont.GetFont(size: 8, weight: FontWeight.SemiBold),
                                Colour = PersonalBestColour.Value,
                            },
                            .. judgementRows,
                        ],
                    },
                ],
            },
        ];

        CurrentColour.BindValueChanged(colour =>
        {
            currentBar.SetColour(colour.NewValue);
            currentAccent.Colour = colour.NewValue;
            currentJudgementHeader1.Colour = colour.NewValue;
        }, true);

        PersonalBestColour.BindValueChanged(colour =>
        {
            personalBestBar.SetColour(colour.NewValue);
            personalBestAccent.Colour = colour.NewValue;
            personalBestJudgementHeader1.Colour = colour.NewValue;
        }, true);

        TargetColour.BindValueChanged(colour =>
        {
            targetBar.SetColour(colour.NewValue);
            targetAccent.Colour = colour.NewValue;
        }, true);

        ShowBars.BindValueChanged(change => updateSectionVisibility(ShowBars, change.NewValue), true);
        ShowScoreDifference.BindValueChanged(change => updateSectionVisibility(ShowScoreDifference, change.NewValue), true);
        ShowJudgementComparison.BindValueChanged(change => updateSectionVisibility(ShowJudgementComparison, change.NewValue), true);

        updateRankLines();
        updateDisplay(1100, 650);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        scoreProcessor = gameplayState?.ScoreProcessor as BmsScoreProcessor;

        if (scoreProcessor == null)
            return;

        maximumExScore = BmsExScore.Calculate(scoreProcessor.MaximumStatistics);
        totalScoringEvents = maximumExScore / 2;
        personalBestFinalScore = 0;
        personalBestProgression = [];
        personalBestJudgementProgress.SetProgression([]);

        updateRankLines();
        loadPersonalBest();
        updateTarget();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        clampHeightToVisibleSections();
    }

    protected override void Update()
    {
        base.Update();

        if (scoreProcessor == null)
            return;

        var currentScore = BmsExScore.Calculate(scoreProcessor.Statistics);
        var judgedEvents = scoreProcessor.ScoringJudgementEventCount;

        if (!currentDisplayValid || currentScore != displayedCurrentScore || judgedEvents != displayedJudgedEvents)
        {
            updateScoreDisplay(currentScore, judgedEvents);
            displayedCurrentScore = currentScore;
            displayedJudgedEvents = judgedEvents;
        }

        updateCurrentJudgementCounts(scoreProcessor.Statistics, !currentDisplayValid);

        var personalBestCounts = drawableRuleset == null
            ? null
            : personalBestJudgementProgress.GetCountsAtTime(drawableRuleset.FrameStableClock.CurrentTime);

        if (!ReferenceEquals(personalBestCounts, displayedPersonalBestJudgementCounts))
        {
            updatePersonalBestJudgementCounts(personalBestCounts);
            displayedPersonalBestJudgementCounts = personalBestCounts;
        }

        currentDisplayValid = true;
    }

    private void loadPersonalBest()
    {
        if (gameplayState == null || realm == null || scoreManager == null || maximumExScore <= 0)
            return;

        try
        {
            var beatmapHash = gameplayState.Score.ScoreInfo.BeatmapHash;
            var rulesetShortName = gameplayState.Ruleset.RulesetInfo.ShortName;
            var localUserId = gameplayState.Score.ScoreInfo.UserID;

            var scores = realm.Run(r => r.All<ScoreInfo>()
                .Where(score => score.BeatmapHash == beatmapHash && !score.DeletePending)
                .ToArray()
                .Where(score => score.Ruleset.ShortName == rulesetShortName)
                .Where(score => score.UserID == localUserId || score.UserID <= 1)
                .Select(score => score.DeepClone())
                .ToArray());

            var personalBest = BmsScoreGraphScoreSelector.SelectBest(scores, gameplayState.Mods, maximumExScore);

            if (personalBest == null)
                return;

            personalBestFinalScore = BmsExScore.Calculate(personalBest, maximumExScore);

            var scoreWithReplay = scoreManager.GetScore(personalBest);

            if (scoreWithReplay != null
                && BmsJudgementEventStore.TryGet(scoreWithReplay.ScoreInfo, out var judgementEvents)
                && judgementEvents.Count > 0)
            {
                personalBestProgression = BmsExScore.CreateProgression(judgementEvents);
                personalBestJudgementProgress.SetProgression(CreateJudgementProgression(judgementEvents));
            }
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to load the BMS score graph personal best.");
        }
    }

    private void updateTarget()
    {
        var personalBestRank = BmsExScore.RankFromScore(personalBestFinalScore, maximumExScore);
        targetRank = BmsExScore.NextRank(personalBestRank);
        targetFinalScore = BmsExScore.MinimumScoreForRank(targetRank, maximumExScore);
        targetLabelText.Text = BmsStrings.ScoreGraphTarget(targetRank);
    }

    private void updateSectionVisibility(BindableBool changedSetting, bool isVisible)
    {
        if (!isVisible && !ShowBars.Value && !ShowScoreDifference.Value && !ShowJudgementComparison.Value)
        {
            changedSetting.Value = true;
            return;
        }

        plotContainer.Alpha = ShowBars.Value ? 1 : 0;
        currentScoreRow.Alpha = personalBestScoreRow.Alpha = targetScoreRow.Alpha = ShowScoreDifference.Value ? 1 : 0;
        judgementComparison.Alpha = ShowJudgementComparison.Value ? 1 : 0;

        var nextBottom = footer_bottom_padding;

        if (ShowJudgementComparison.Value)
        {
            judgementComparison.Y = -nextBottom;
            nextBottom += judgement_table_height;
        }

        if (ShowScoreDifference.Value)
        {
            if (ShowJudgementComparison.Value)
                nextBottom += footer_gap;

            targetScoreRow.Y = -nextBottom;
            nextBottom += score_row_height + footer_gap;
            personalBestScoreRow.Y = -nextBottom;
            nextBottom += score_row_height + footer_gap;
            currentScoreRow.Y = -nextBottom;
            nextBottom += score_row_height;
        }

        if (ShowBars.Value && (ShowScoreDifference.Value || ShowJudgementComparison.Value))
            nextBottom += footer_gap;

        plotContainer.Padding = new MarginPadding
        {
            Top = plot_top_padding,
            Bottom = nextBottom,
            Left = horizontal_padding,
            Right = horizontal_padding,
        };

        if (IsLoaded)
            clampHeightToVisibleSections();
    }

    private void clampHeightToVisibleSections()
    {
        base.Height = Math.Max(base.Height, CalculateMinimumHeight(ShowBars.Value, ShowScoreDifference.Value, ShowJudgementComparison.Value));
    }

    internal static float CalculateMinimumHeight(bool showBars, bool showScoreDifference, bool showJudgementComparison)
    {
        var minimumHeight = footer_bottom_padding;

        if (showJudgementComparison)
            minimumHeight += judgement_table_height;

        if (showScoreDifference)
        {
            if (showJudgementComparison)
                minimumHeight += footer_gap;

            minimumHeight += score_row_height * 3 + footer_gap * 2;
        }

        if (showBars)
        {
            if (showScoreDifference || showJudgementComparison)
                minimumHeight += footer_gap;

            minimumHeight += plot_top_padding + minimum_plot_height;
        }

        return Math.Max(absolute_minimum_height, minimumHeight);
    }

    private void updateRankLines()
    {
        foreach (var rankLine in rankLines)
            rankLine.SetMaximumScore(maximumExScore);
    }

    private void updateDisplay(int currentScore, int judgedEvents)
    {
        updateScoreDisplay(currentScore, judgedEvents);
        updateCurrentJudgementCounts(scoreProcessor?.Statistics, true);
        updatePersonalBestJudgementCounts(null);
    }

    private void updateScoreDisplay(int currentScore, int judgedEvents)
    {
        var clampedJudgedEvents = Math.Clamp(judgedEvents, 0, totalScoringEvents);
        var personalBestScore = ScoreAtProgress(
            personalBestFinalScore,
            personalBestProgression,
            clampedJudgedEvents,
            totalScoringEvents);
        var targetScore = scaleScore(targetFinalScore, clampedJudgedEvents, totalScoringEvents);

        currentScoreText.Text = BmsStrings.ScoreGraphExScore(currentScore);
        personalBestScoreText.Text = BmsStrings.ScoreGraphExScore(personalBestScore);
        personalBestDifferenceText.Text = BmsStrings.ScoreGraphDifference(currentScore - personalBestScore);
        targetScoreText.Text = BmsStrings.ScoreGraphExScore(targetScore);
        targetDifferenceText.Text = BmsStrings.ScoreGraphDifference(currentScore - targetScore);

        currentBar.SetScores(currentScore, currentScore, maximumExScore);
        personalBestBar.SetScores(personalBestScore, personalBestFinalScore, maximumExScore);
        targetBar.SetScores(targetScore, targetFinalScore, maximumExScore);
    }

    private void updateCurrentJudgementCounts(IReadOnlyDictionary<HitResult, int>? currentStatistics, bool force)
    {
        for (var i = 0; i < displayed_judgements.Length; i++)
        {
            var result = displayed_judgements[i];
            var count = currentStatistics?.GetValueOrDefault(result) ?? 0;

            if (!force && count == displayedCurrentJudgementCounts[i])
                continue;

            displayedCurrentJudgementCounts[i] = count;
            var texts = judgementCountTexts[result];
            texts.Current.Text = BmsStrings.ScoreGraphJudgementCount(count);
        }
    }

    private void updatePersonalBestJudgementCounts(IReadOnlyList<int>? counts)
    {
        for (var i = 0; i < displayed_judgements.Length; i++)
        {
            var texts = judgementCountTexts[displayed_judgements[i]];
            var personalBestCount = counts?[i];
            texts.PersonalBest.Text = personalBestCount == null
                ? BmsStrings.ScoreGraphJudgementUnavailable
                : BmsStrings.ScoreGraphJudgementCount(personalBestCount.Value);
        }
    }

    internal static JudgementSnapshot[] CreateJudgementProgression(IEnumerable<BmsJudgementEvent> events)
    {
        var orderedEvents = events.OrderBy(judgementTime).ToArray();
        var progression = new JudgementSnapshot[orderedEvents.Length];
        var counts = new int[displayed_judgements.Length];

        for (var i = 0; i < orderedEvents.Length; i++)
        {
            var judgementEvent = orderedEvents[i];
            var resultIndex = Array.IndexOf(displayed_judgements, judgementEvent.Result);

            if (resultIndex >= 0)
                counts[resultIndex]++;

            progression[i] = new JudgementSnapshot(judgementTime(judgementEvent), [.. counts]);
        }

        return progression;
    }

    private static double judgementTime(BmsJudgementEvent judgementEvent) =>
        judgementEvent.TimingObservations.Max(observation => observation.ActualTime);

    internal static int? JudgementCountAtTime(IReadOnlyList<JudgementSnapshot> progression, double time, HitResult result)
    {
        if (progression.Count == 0)
            return null;

        var resultIndex = Array.IndexOf(displayed_judgements, result);

        if (resultIndex < 0)
            return 0;

        var snapshotIndex = findSnapshotIndexAtTime(progression, time);
        return snapshotIndex < 0 ? 0 : progression[snapshotIndex].Counts[resultIndex];
    }

    private static int findSnapshotIndexAtTime(IReadOnlyList<JudgementSnapshot> progression, double time)
    {
        var lower = 0;
        var upper = progression.Count;

        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;

            if (progression[middle].Time <= time)
                lower = middle + 1;
            else
                upper = middle;
        }

        return lower - 1;
    }

    internal static int ScaleScore(int finalScore, int judgedEvents, int totalEvents) =>
        scaleScore(finalScore, judgedEvents, totalEvents);

    internal static int ScoreAtProgress(
        int finalScore,
        IReadOnlyList<int> progression,
        int judgedEvents,
        int totalEvents)
    {
        var clampedJudgedEvents = Math.Clamp(judgedEvents, 0, Math.Max(0, totalEvents));

        return progression.Count > 1
            ? progression[Math.Min(clampedJudgedEvents, progression.Count - 1)]
            : scaleScore(finalScore, clampedJudgedEvents, totalEvents);
    }

    private static int scaleScore(int finalScore, int judgedEvents, int totalEvents)
    {
        if (totalEvents <= 0)
            return 0;

        return (int)Math.Floor((double)finalScore * Math.Clamp(judgedEvents, 0, totalEvents) / totalEvents);
    }

    private Drawable[] createJudgementRows()
    {
        var rows = new Drawable[displayed_judgements.Length];

        for (var i = 0; i < displayed_judgements.Length; i++)
        {
            var result = displayed_judgements[i];
            rows[i] = createJudgementRow(result, 13 + i * 9.5f, out var current, out var personalBest);
            judgementCountTexts.Add(result, new JudgementCountTexts(current, personalBest));
        }

        return rows;
    }

    private static Container createJudgementRow(
        HitResult result,
        float y,
        out OsuSpriteText current,
        out OsuSpriteText personalBest) => new()
    {
        RelativeSizeAxes = Axes.X,
        Height = 9,
        Y = y,
        Children =
        [
            new OsuSpriteText
            {
                Position = new Vector2(4, 0),
                Text = BmsStrings.ScoreGraphJudgement(result),
                Font = OsuFont.GetFont(size: 8, weight: FontWeight.SemiBold),
                Colour = BmsHitResultColours.ForHitResult(result),
            },
            current = new OsuSpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-54, 0),
                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold, fixedWidth: true),
                Colour = BmsHitResultColours.ForHitResult(result),
            },
            personalBest = new OsuSpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-4, 0),
                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold, fixedWidth: true),
                Colour = BmsHitResultColours.ForHitResult(result),
            },
        ],
    };

    private static Container createScoreRow(
        LocalisableString label,
        bool showDifference,
        out Box accent,
        out OsuSpriteText scoreText,
        out OsuSpriteText differenceText) =>
        createScoreRow(label, showDifference, out accent, out scoreText, out differenceText, out _);

    private static Container createScoreRow(
        LocalisableString label,
        bool showDifference,
        out Box accent,
        out OsuSpriteText scoreText,
        out OsuSpriteText differenceText,
        out OsuSpriteText labelText)
    {
        var row = new Container
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Height = score_row_height,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(22, 26, 32, 255),
                },
                accent = new Box
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = 3,
                },
                labelText = new OsuSpriteText
                {
                    Position = new Vector2(9, 3),
                    Text = label,
                    Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                    Colour = new Color4(188, 197, 208, 255),
                },
                scoreText = new OsuSpriteText
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Position = new Vector2(9, -2),
                    Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold, fixedWidth: true),
                    Colour = Color4.White,
                },
                differenceText = new OsuSpriteText
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Position = new Vector2(-6, -3),
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold, fixedWidth: true),
                    Colour = new Color4(205, 212, 221, 255),
                    Alpha = showDifference ? 1 : 0,
                },
            ],
        };

        return row;
    }

    private sealed partial class RankLine : CompositeDrawable
    {
        private readonly ScoreRank rank;
        private readonly OsuSpriteText label;

        public RankLine(ScoreRank rank)
        {
            this.rank = rank;

            RelativePositionAxes = Axes.Y;
            RelativeSizeAxes = Axes.X;
            Y = 1 - (float)BmsExScore.AccuracyCutoffFromRank(rank);
            Width = 1;
            Height = 1;

            var isMaximum = rank is ScoreRank.X or ScoreRank.XH;

            InternalChildren =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = new Color4(185, 196, 210, 255),
                    Alpha = isMaximum ? 0.3f : 0.2f,
                },
                label = new OsuSpriteText
                {
                    Anchor = isMaximum ? Anchor.TopRight : Anchor.BottomRight,
                    Origin = isMaximum ? Anchor.TopRight : Anchor.BottomRight,
                    Position = new Vector2(-3, isMaximum ? 2 : -2),
                    Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold, fixedWidth: true),
                    Colour = new Color4(205, 214, 225, 255),
                    Shadow = true,
                },
            ];
        }

        public void SetMaximumScore(int maximumScore)
        {
            label.Text = BmsStrings.ScoreGraphRankThreshold(rank, BmsExScore.MinimumScoreForRank(rank, maximumScore));
        }
    }

    private sealed partial class ScoreBar : CompositeDrawable
    {
        private const float minimum_bar_width = 24;
        private const float maximum_bar_width = 72;
        private const float column_fill_ratio = 0.62f;

        private readonly Box ghost;
        private readonly Box fill;

        public ScoreBar(bool showGhost, int column)
        {
            RelativePositionAxes = Axes.X;
            RelativeSizeAxes = Axes.Both;
            X = column / 3f;
            Size = new Vector2(1f / 3, 1);
            InternalChildren =
            [
                ghost = new Box
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 36,
                    Alpha = showGhost ? 0.2f : 0,
                },
                fill = new Box
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 36,
                },
            ];
        }

        protected override void Update()
        {
            base.Update();

            var barWidth = Math.Clamp(DrawWidth * column_fill_ratio, minimum_bar_width, maximum_bar_width);
            ghost.Width = fill.Width = barWidth;
        }

        public void SetColour(Color4 colour)
        {
            ghost.Colour = colour;
            fill.Colour = colour;
        }

        public void SetScores(int score, int finalScore, int maximumScore)
        {
            fill.Height = ratio(score, maximumScore);
            ghost.Height = ratio(finalScore, maximumScore);
        }

        private static float ratio(int score, int maximumScore) => maximumScore <= 0
            ? 0
            : Math.Clamp((float)score / maximumScore, 0, 1);
    }

    private sealed record JudgementCountTexts(OsuSpriteText Current, OsuSpriteText PersonalBest);

    internal sealed class JudgementProgressCursor
    {
        private IReadOnlyList<JudgementSnapshot> progression = [];
        private readonly int[] zeroCounts = new int[displayed_judgements.Length];
        private int snapshotIndex = -1;
        private double lastTime = double.NegativeInfinity;

        public void SetProgression(IReadOnlyList<JudgementSnapshot> snapshots)
        {
            progression = snapshots;
            snapshotIndex = -1;
            lastTime = double.NegativeInfinity;
        }

        public int? GetCountAtTime(double time, HitResult result)
        {
            var counts = GetCountsAtTime(time);

            if (counts == null)
                return null;

            var resultIndex = Array.IndexOf(displayed_judgements, result);

            if (resultIndex < 0)
                return 0;

            return counts[resultIndex];
        }

        public IReadOnlyList<int>? GetCountsAtTime(double time)
        {
            if (progression.Count == 0)
                return null;

            if (time < lastTime)
            {
                snapshotIndex = findSnapshotIndexAtTime(progression, time);
            }
            else
            {
                while (snapshotIndex + 1 < progression.Count && progression[snapshotIndex + 1].Time <= time)
                    snapshotIndex++;
            }

            lastTime = time;
            return snapshotIndex < 0 ? zeroCounts : progression[snapshotIndex].Counts;
        }
    }

    internal readonly record struct JudgementSnapshot(double Time, int[] Counts);
}
