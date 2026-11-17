using System.Collections.Generic;
using System.Linq;
using System;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultOverview : CompositeDrawable
{
    private static readonly HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss];
    private readonly GridContainer layout;
    private readonly Container scoreContent;
    private readonly Container accuracyComboContent;
    private readonly Container timingContent;
    private readonly Container judgementsContent;
    private readonly BmsResultSoloHeader? soloHeader;
    private readonly bool showComparison;
    private Vector2 lastLayoutSize;

    internal ScoreInfo Score { get; private set; } = null!;

    internal BmsResultOverview(ScoreInfo score, Drawable? header = null, bool showComparison = true)
    {
        this.showComparison = showComparison;
        RelativeSizeAxes = Axes.Both;
        InternalChild = layout = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Content = new[]
            {
                new Drawable[]
                {
                    header == null ? soloHeader = new BmsResultSoloHeader(score) : new BmsResultSection(header),
                },
                new Drawable[] { new BmsResultSection(scoreContent = new Container { RelativeSizeAxes = Axes.Both }) },
                new Drawable[] { new BmsResultSection(accuracyComboContent = new Container { RelativeSizeAxes = Axes.Both }) },
                new Drawable[] { new BmsResultSection(timingContent = new Container { RelativeSizeAxes = Axes.Both }, 2) },
                new Drawable[] { new BmsResultSection(judgementsContent = new Container { RelativeSizeAxes = Axes.Both }) },
            },
        };
        SetScore(score);
    }

    internal void SetScore(ScoreInfo score, IReadOnlyList<HitEvent>? hitEvents = null)
    {
        Score = score;
        var profile = GameplayProfile.FromMods(score.Mods);
        var maximum = profile.StoredMaximumScore(score);
        var totalJudgements = results.Sum(result => score.Statistics.GetValueOrDefault(result));
        scoreContent.Child = createScore(profile.ScoreLabel, profile.StoredScore(score), maximum);
        accuracyComboContent.Child = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = [new Dimension(GridSizeMode.Relative, 0.37f), new Dimension()],
            Content = new[]
            {
                new[] { metric(BmsStrings.ResultAccuracy, BmsStrings.ResultPercentage(score.Accuracy), "Result accuracy") },
                new[] { createCombo(score.MaxCombo, score.GetMaximumAchievableCombo()) },
            },
        };
        timingContent.Child = new BmsResultFastSlow(profile, hitEvents ?? score.HitEvents);
        var judgements = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Content = results.Select(result => new Drawable[]
            {
                new BmsResultJudgementRow(profile, result, score.Statistics.GetValueOrDefault(result), totalJudgements),
            }).ToArray(),
        };
        // Course and practice statistics cannot be compared against full-chart solo scores.
        judgementsContent.Child = soloHeader == null || !showComparison
            ? judgements
            : new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = [new Dimension(GridSizeMode.Absolute, 28), new Dimension()],
                Content = new Drawable[][]
                {
                    [
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children =
                            [
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Right = 36 },
                                    Child = new BmsResultFittedText(BmsStrings.ResultJudgements, 16),
                                },
                                new BmsResultComparisonButton(score) { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight },
                            ],
                        },
                    ],
                    [judgements],
                },
            };
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (lastLayoutSize == DrawSize)
            return;

        lastLayoutSize = DrawSize;
        // Keep metadata compact on short windows so the additional timing summary still fits.
        var bannerHeight = Math.Clamp(DrawHeight * 0.16f, 86, 94);
        var rankHeight = Math.Max(0, DrawWidth - 24) * 260 / 300;
        var headerHeight = soloHeader == null
            ? DrawHeight * 0.51f
            : Math.Min(bannerHeight + rankHeight + 24 + 16, DrawHeight * 0.51f) + 8;
        var statisticsHeight = Math.Max(0, DrawHeight - headerHeight);
        soloHeader?.RowDimensions = [new Dimension(GridSizeMode.Absolute, bannerHeight + 8), new Dimension()];
        layout.RowDimensions =
        [
            new Dimension(GridSizeMode.Absolute, headerHeight),
            new Dimension(GridSizeMode.Absolute, Math.Min(88, statisticsHeight * 0.28f)),
            new Dimension(GridSizeMode.Absolute, Math.Clamp(statisticsHeight * 0.24f, 80, 96)),
            new Dimension(GridSizeMode.Absolute, Math.Clamp(statisticsHeight * 0.14f, 34, 50)),
            new Dimension(),
        ];
    }

    private static Drawable createScore(LocalisableString label, double? score, double? maximum) => new GridContainer
    {
        Name = "Result score",
        RelativeSizeAxes = Axes.Both,
        RowDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, maximum is > 0 ? 20 : 0)],
        Content = new[]
        {
            new Drawable[]
            {
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = [new Dimension(GridSizeMode.Relative, 0.39f), new Dimension()],
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Right = 6 },
                                Child = new BmsResultFittedText(BmsStrings.ResultNumber(score), 76, Anchor.BottomLeft, useFullGlyphHeight: false)
                                {
                                    Name = "Score value",
                                },
                            },
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions = [new Dimension(GridSizeMode.Relative, 0.4f), new Dimension()],
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new BmsResultFittedText(BmsStrings.ResultPerformance, 18, Anchor.TopRight, useFullGlyphHeight: false),
                                    },
                                    new Drawable[] { new BmsResultTextGroup(maximum.HasValue
                                        ? [(label, 28), (BmsStrings.ResultMaximum(maximum.Value), 18)]
                                        : [(label, 28)]) },
                                },
                            },
                        },
                    },
                },
            },
            new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = maximum is > 0 ? 4 : 0 },
                    Alpha = maximum is > 0 ? 1 : 0,
                    Child = new BmsResultBar(score.HasValue && maximum is > 0 ? score.Value / maximum.Value : 0, BmsResultColours.PROGRESS),
                },
            },
        },
    };

    private static Drawable createCombo(int combo, int? maximum) => new GridContainer
    {
        Name = "Result combo",
        RelativeSizeAxes = Axes.Both,
        RowDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, maximum is > 0 ? 20 : 0)],
        Content = new[]
        {
            new Drawable[]
            {
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = [new Dimension(GridSizeMode.Relative, 0.45f), new Dimension()],
                    Padding = new MarginPadding { Top = 2 },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new BmsResultFittedText(BmsStrings.ResultCombo, 22, useFullGlyphHeight: false),
                            new BmsResultTextGroup(maximum.HasValue
                                ? [(BmsStrings.ResultNumber(combo), 40), (BmsStrings.ResultMaximum(maximum.Value), 24)]
                                : [(BmsStrings.ResultNumber(combo), 40)], Anchor.CentreRight, useFullGlyphHeight: false),
                        },
                    },
                },
            },
            new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = maximum is > 0 ? 4 : 0 },
                    Alpha = maximum is > 0 ? 1 : 0,
                    Child = new BmsResultBar(maximum is > 0 ? (double)combo / maximum.Value : 0, BmsResultColours.PROGRESS)
                    {
                        Name = "Result combo progress",
                    },
                },
            },
        },
    };

    private static Drawable metric(LocalisableString label, LocalisableString value, string name) => new GridContainer
    {
        Name = name,
        RelativeSizeAxes = Axes.Both,
        Padding = new MarginPadding { Bottom = 2 },
        ColumnDimensions = [new Dimension(GridSizeMode.Relative, 0.45f), new Dimension()],
        Content = new[]
        {
            new Drawable[]
            {
                new BmsResultFittedText(label, 22, useFullGlyphHeight: false),
                new BmsResultFittedText(value, 34, Anchor.CentreRight, useFullGlyphHeight: false),
            },
        },
    };
}
