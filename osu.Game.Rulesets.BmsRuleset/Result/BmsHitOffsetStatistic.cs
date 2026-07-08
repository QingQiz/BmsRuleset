using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result;

public sealed partial class BmsHitOffsetStatistic : CompositeDrawable
{
    private const float graph_height = 200;
    private const float key_graph_height = 100;
    private const double offset_range = 150;
    private const int bin_count = 200;
    private const float label_width = 96;

    private readonly HitOffsetStatistics statistics;

    private FillFlowContainer content = null!;
    private bool expanded;

    public BmsHitOffsetStatistic(IReadOnlyList<HitEvent> hitEvents, IBeatmap playableBeatmap)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        statistics = CreateStatistics(playableBeatmap, hitEvents);
    }

    public override bool HandlePositionalInput => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = content = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
        };

        rebuild();
    }

    internal static HitOffsetStatistics CreateStatistics(IBeatmap playableBeatmap, IReadOnlyList<HitEvent> hitEvents)
    {
        var bmsHitEvents = hitEvents.Where(e => e.HitObject is BmsHitObject and not BmsLandmine && e.Result.IsBasic() && e.Result.IsHit()).ToArray();

        var variant = playableBeatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
        var totalColumns = playableBeatmap is BmsBeatmap { TotalColumns: > 0 } bmsWithColumns
            ? bmsWithColumns.TotalColumns
            : Math.Max(BmsLayout.GetTotalColumns(variant), bmsHitEvents.Select(e => ((BmsHitObject)e.HitObject).Column + 1).DefaultIfEmpty(0).Max());

        var hitsByColumn = bmsHitEvents.ToLookup(e => ((BmsHitObject)e.HitObject).Column);
        var keyIndex = 0;

        var keyGroups = Enumerable.Range(0, totalColumns)
            .Select(column => new KeyHitOffsetStatistics(labelFor(column, variant, ref keyIndex), createSummary(hitsByColumn[column].Select(e => (e.TimeOffset, e.Result)))))
            .ToArray();

        return new HitOffsetStatistics(createSummary(bmsHitEvents.Select(e => (e.TimeOffset, e.Result))), keyGroups);
    }

    private static string labelFor(int column, BmsLayoutVariant variant, ref int keyIndex)
    {
        if (BmsLayout.IsScratchColumn(column, variant))
            return "Scratch";

        return $"Key {++keyIndex}";
    }

    private void rebuild()
    {
        content.Clear();

        content.Add(createRow("Overall", statistics.Overall, graph_height));

        if (expanded)
        {
            foreach (var key in statistics.Keys)
                content.Add(createRow(key.Label, key.Summary, key_graph_height));
        }
    }

    protected override bool OnClick(ClickEvent e)
    {
        expanded = !expanded;
        rebuild();

        return true;
    }

    private static Drawable createRow(string label, HitOffsetSummary summary, float height) => new GridContainer
    {
        RelativeSizeAxes = Axes.X,
        Height = height,
        ColumnDimensions =
        [
            new Dimension(GridSizeMode.Absolute, label_width),
            new Dimension(),
        ],
        Content = new[]
        {
            new[]
            {
                createLabel(label, summary),
                new OffsetHistogram(summary),
            },
        },
    };

    private static Drawable createLabel(string label, HitOffsetSummary summary) => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Direction = FillDirection.Vertical,
        Spacing = new Vector2(0, 2),
        Children =
        [
            new OsuSpriteText
            {
                Text = label,
                Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
            },
            new OsuSpriteText
            {
                Text = $"{summary.AverageOffset:+0.0;-0.0;0.0} ms",
                Colour = summary.AverageOffset < 0 ? early_colour : late_colour,
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
            },
            new OsuSpriteText
            {
                Text = $"{summary.Count} hits",
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
        ],
    };

    private static HitOffsetSummary createSummary(IEnumerable<(double offset, HitResult result)> hits)
    {
        var hitList = hits.ToList();
        var values = hitList.Select(h => h.offset).ToArray();
        var binsByResult = new Dictionary<HitResult, int[]>();

        foreach (var (offset, result) in hitList)
        {
            var clamped = Math.Clamp(offset, -offset_range, offset_range);
            var index = (int)Math.Floor((clamped + offset_range) / (offset_range * 2) * bin_count);
            var bin = Math.Clamp(index, 0, bin_count - 1);

            if (!binsByResult.TryGetValue(result, out var bins))
                binsByResult[result] = bins = new int[bin_count];
            bins[bin]++;
        }

        // Descending order (best → worst) so the best result stacks at the bottom and the worst on top.
        var results = binsByResult.Keys.OrderByDescending(r => r).ToArray();

        return new HitOffsetSummary(
            values.Length,
            values.Length == 0 ? 0 : values.Average(),
            values.Count(v => v < 0),
            values.Count(v => v > 0),
            results,
            binsByResult);
    }

    private static readonly Color4 early_colour = new(90, 175, 255, 255);
    private static readonly Color4 late_colour = new(255, 130, 92, 255);

    internal sealed record HitOffsetStatistics(HitOffsetSummary Overall, IReadOnlyList<KeyHitOffsetStatistics> Keys);

    internal sealed record KeyHitOffsetStatistics(string Label, HitOffsetSummary Summary);

    internal sealed record HitOffsetSummary(
        int Count,
        double AverageOffset,
        int EarlyCount,
        int LateCount,
        IReadOnlyList<HitResult> Results,
        IReadOnlyDictionary<HitResult, int[]> BinsByResult);

    private partial class OffsetHistogram : CompositeDrawable
    {
        private readonly HitOffsetSummary summary;

        public OffsetHistogram(HitOffsetSummary summary)
        {
            this.summary = summary;

            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var maxTotal = Math.Max(1, Enumerable.Range(0, bin_count)
                .Select(b => summary.Results.Sum(r => summary.BinsByResult[r][b]))
                .DefaultIfEmpty(0)
                .Max());

            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions =
                [
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 24),
                ],
                Content = new[]
                {
                    new[] { createPlot(maxTotal) },
                    new[] { createAxis() },
                },
            };
        }

        private Drawable createPlot(int maxTotal) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.18f,
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions = Enumerable.Range(0, bin_count).Select(_ => new Dimension()).ToArray(),
                    Content = new[]
                    {
                        Enumerable.Range(0, bin_count).Select(b => createBar(b, maxTotal)).ToArray(),
                    },
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = Color4.White,
                    Alpha = 0.32f,
                },
            ],
        };

        private Drawable createAxis() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Text = $"early {summary.EarlyCount}",
                    Colour = early_colour,
                    Alpha = 0.75f,
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
                },
                createTickLabel(-150, 0, Anchor.TopLeft),
                createTickLabel(-75, 0.25f, Anchor.TopCentre),
                createTickLabel(0, 0.5f, Anchor.TopCentre),
                createTickLabel(75, 0.75f, Anchor.TopCentre),
                createTickLabel(150, 1, Anchor.TopRight),
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Text = $"late {summary.LateCount}",
                    Colour = late_colour,
                    Alpha = 0.75f,
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
                },
            ],
        };

        private static OsuSpriteText createTickLabel(int offset, float x, Anchor origin) => new()
        {
            Anchor = Anchor.TopLeft,
            Origin = origin,
            RelativePositionAxes = Axes.X,
            X = x,
            Text = $"{offset:+0;-0;0} ms",
            Colour = Color4.White,
            Alpha = 0.55f,
            Font = OsuFont.GetFont(size: 9),
        };

        private Drawable createBar(int binIndex, int maxTotal)
        {
            // No horizontal padding: bars sit edge-to-edge so the histogram reads as one continuous shape.
            var bar = new Container
            {
                RelativeSizeAxes = Axes.Both,
            };

            var total = summary.Results.Sum(r => summary.BinsByResult[r][binIndex]);

            if (total == 0)
            {
                bar.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Height = 0.03f,
                    Colour = Color4.White,
                    Alpha = 0.12f,
                });
                return bar;
            }

            float cumulativeBelow = 0;

            foreach (var result in summary.Results)
            {
                var count = summary.BinsByResult[result][binIndex];
                if (count == 0)
                    continue;

                var height = (float)count / maxTotal;
                bar.Add(new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativePositionAxes = Axes.Y,
                    Y = -cumulativeBelow,
                    Height = height,
                    Colour = BmsHitResultColours.ForHitResult(result),
                    Alpha = 0.86f,
                });
                cumulativeBelow += height;
            }

            return bar;
        }
    }
}
