using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Ranking.Statistics;
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
    private readonly StatisticCell[] cells;

    internal BmsResultStatisticsGrid(StatisticItem[] items)
    {
        RelativeSizeAxes = Axes.Both;
        cells = items.Select(item => new StatisticCell(item)).ToArray();

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

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // Timeline contains three subplots, so its shared row needs more height than either lower chart.
        var rowHeight = DrawHeight / (Math.Max(0, cells.Length - 2) + first_row_weight);
        for (var i = 0; i < cells.Length; i++)
            cells[i].FitSummaryToHeight(rowHeight * (i < 2 ? first_row_weight : 1));
    }

    private partial class StatisticCell : CompositeDrawable
    {
        private readonly Drawable statistic;
        private readonly OsuSpriteText heading;

        internal StatisticCell(StatisticItem item)
        {
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
                        heading = new OsuSpriteText { Text = item.Name, Font = OsuFont.GetFont(size: 17, weight: FontWeight.SemiBold) },
                        statistic = item.CreateContent(),
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
