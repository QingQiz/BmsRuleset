using System.Linq;
using System;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal interface IBmsResultStatistic
{
    void FitSummaryToHeight(float height);
}

internal partial class BmsResultStatisticsGrid : CompositeDrawable
{
    private const float gap = 8;
    private const float content_padding = 8;
    private const float heading_spacing = 6;
    private const float first_row_weight = 1.4f;
    private const float scatter_row_weight = 1.3f;
    private const float offset_row_weight = 0.7f;
    private readonly StatisticCell[] cells;
    private readonly BmsGaugeHistoryGraph gauge;
    private readonly BmsTimelineStatistic timeline;
    private readonly BmsHitScatterStatistic scatter;
    private readonly BmsHitOffsetStatistic offset;

    internal BmsResultStatisticsGrid(GameplayProfile profile)
    {
        RelativeSizeAxes = Axes.Both;
        var empty = BmsResultStatisticsData.Empty();
        gauge = new BmsGaugeHistoryGraph(empty.Gauges, empty.StageBoundaries);
        timeline = new BmsTimelineStatistic(empty.Timeline);
        scatter = new BmsHitScatterStatistic(empty.Scatter, profile);
        offset = new BmsHitOffsetStatistic(empty.Offset, profile);
        cells =
        [
            new StatisticCell(BmsStrings.GaugeHistory, gauge),
            new StatisticCell(BmsStrings.Timeline, timeline),
            new StatisticCell(BmsStrings.HitScatter, scatter),
            new StatisticCell(BmsStrings.HitOffset, offset),
        ];

        InternalChild = new OsuScrollContainer
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            ScrollbarOverlapsContent = false,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children =
                [
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        RowDimensions = [new Dimension(GridSizeMode.AutoSize)],
                        Content = new[] { cells.Take(2).Cast<Drawable>().ToArray() },
                    },
                    .. cells.Skip(2),
                ],
            },
        };
    }

    internal void SetData(BmsResultStatisticsData data)
    {
        gauge.SetData(data.Gauges, data.StageBoundaries);
        timeline.SetData(data.Timeline);
        scatter.SetData(data.Scatter);
        offset.SetData(data.Offset);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // The timeline's three subplots and scatter's timing range need more height than the offset histogram.
        var rowHeight = DrawHeight / (first_row_weight + scatter_row_weight + offset_row_weight);
        for (var i = 0; i < cells.Length; i++)
            cells[i].FitSummaryToHeight(rowHeight * (i < 2 ? first_row_weight : i == 2 ? scatter_row_weight : offset_row_weight));
    }

    private partial class StatisticCell : CompositeDrawable
    {
        private readonly Drawable statistic;
        private readonly OsuSpriteText heading;

        internal StatisticCell(LocalisableString name, Drawable statistic)
        {
            this.statistic = statistic;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Padding = new MarginPadding(gap / 2);
            InternalChildren =
            [
                BmsResultSection.CreateBackground(),
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(content_padding),
                    Spacing = new Vector2(0, heading_spacing),
                    Children =
                    [
                        heading = new OsuSpriteText { Text = name, Font = OsuFont.GetFont(size: 17, weight: FontWeight.SemiBold) },
                        statistic,
                    ],
                },
            ];
        }

        internal void FitSummaryToHeight(float height)
        {
            var available = Math.Max(0, height - gap - content_padding * 2 - heading.DrawHeight - heading_spacing);
            // Only the overall plots share the viewport budget; expanded key rows remain scrollable.
            (statistic as IBmsResultStatistic)?.FitSummaryToHeight(available);
        }
    }
}
