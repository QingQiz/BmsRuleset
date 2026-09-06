using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultOverview : CompositeDrawable
{
    private static readonly HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss];
    private readonly GridContainer layout;
    private readonly Container exScoreContent;
    private readonly Container accuracyComboContent;
    private readonly Container timingContent;
    private readonly Container judgementsContent;
    private readonly BmsResultSoloHeader? soloHeader;
    private Vector2 lastLayoutSize;

    internal ScoreInfo Score { get; private set; } = null!;

    internal BmsResultOverview(ScoreInfo score, Drawable? header = null)
    {
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
                new Drawable[] { new BmsResultSection(exScoreContent = new Container { RelativeSizeAxes = Axes.Both }) },
                new Drawable[] { new BmsResultSection(accuracyComboContent = new Container { RelativeSizeAxes = Axes.Both }) },
                new Drawable[] { new BmsResultSection(timingContent = new Container { RelativeSizeAxes = Axes.Both }, 2) },
                new Drawable[] { new BmsResultSection(judgementsContent = new Container { RelativeSizeAxes = Axes.Both }) },
            },
        };
        SetScore(score);
    }

    internal void SetScore(ScoreInfo score)
    {
        Score = score;
        var maximum = BmsExScore.Calculate(score.MaximumStatistics);
        var totalJudgements = results.Sum(result => score.Statistics.GetValueOrDefault(result));
        exScoreContent.Child = createExScore(BmsExScore.Calculate(score, maximum), maximum);
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
        timingContent.Child = new BmsResultFastSlow(score.HitEvents);
        judgementsContent.Child = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Content = results.Select(result => new Drawable[]
            {
                new BmsResultJudgementRow(result, score.Statistics.GetValueOrDefault(result), totalJudgements),
            }).ToArray(),
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
        if (soloHeader != null)
            soloHeader.RowDimensions = [new Dimension(GridSizeMode.Absolute, bannerHeight + 8), new Dimension()];
        layout.RowDimensions =
        [
            new Dimension(GridSizeMode.Absolute, headerHeight),
            new Dimension(GridSizeMode.Absolute, Math.Min(88, statisticsHeight * 0.28f)),
            new Dimension(GridSizeMode.Absolute, Math.Clamp(statisticsHeight * 0.24f, 80, 96)),
            new Dimension(GridSizeMode.Absolute, Math.Clamp(statisticsHeight * 0.14f, 34, 50)),
            new Dimension(),
        ];
    }

    private static Drawable createExScore(int score, int maximum) => new GridContainer
    {
        Name = "Result EXSCORE",
        RelativeSizeAxes = Axes.Both,
        RowDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 20)],
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
                                    Name = "EXSCORE value",
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
                                    new Drawable[] { new BmsResultTextGroup([(BmsStrings.ExScore, 28), (BmsStrings.ResultMaximum(maximum), 18)]) },
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
                    Padding = new MarginPadding { Top = 4 },
                    Child = new BmsResultBar(maximum > 0 ? (double)score / maximum : 0, BmsResultColours.PROGRESS),
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
