using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result;

public sealed partial class BmsHitOffsetStatistic : CompositeDrawable
{
    private const float graph_height = 200;
    private const float key_graph_height = 100;
    private const int bins_per_side = 50;
    private const int bin_count = bins_per_side * 2 + 1;
    private const int centre_bin_index = bins_per_side;
    private const int axis_points = 5;
    private const float minimum_bar_height = 0.02f;
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

    internal BmsHitOffsetStatistic(IReadOnlyList<(IBeatmap Beatmap, IReadOnlyList<HitEvent> HitEvents)> stages)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        statistics = createCourseStatistics(stages);
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
        var displayedHitEvents = hitEvents.Where(e =>
            e.HitObject is not BmsLandmine
            && e.Result.IsBasic()
            && (e.HitObject is BmsHitObject || e.Result == HitResult.Miss)).ToArray();
        var bmsHitEvents = displayedHitEvents.Where(e => e.HitObject is BmsHitObject).ToArray();

        var variant = playableBeatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
        var totalColumns = playableBeatmap is BmsBeatmap { TotalColumns: > 0 } bmsWithColumns
            ? bmsWithColumns.TotalColumns
            : Math.Max(BmsLayout.GetTotalColumns(variant), bmsHitEvents.Select(e => ((BmsHitObject)e.HitObject).Column + 1).DefaultIfEmpty(0).Max());

        var hitsByColumn = bmsHitEvents.ToLookup(e => ((BmsHitObject)e.HitObject).Column);
        var keyIndex = 0;

        var keyGroups = Enumerable.Range(0, totalColumns)
            .Select(column => new KeyHitOffsetStatistics(labelFor(column, variant, ref keyIndex), createSummary(hitsByColumn[column].Select(e => (e.TimeOffset, e.Result)))))
            .ToArray();

        return new HitOffsetStatistics(createSummary(displayedHitEvents.Select(e => (e.TimeOffset, e.Result))), keyGroups);
    }

    private static HitOffsetStatistics createCourseStatistics(IReadOnlyList<(IBeatmap Beatmap, IReadOnlyList<HitEvent> HitEvents)> stages)
    {
        var overall = new List<(double offset, HitResult result)>();
        var hitsByKey = new Dictionary<int, List<(double offset, HitResult result)>>();
        var labels = new List<string>();

        foreach (var (beatmap, hitEvents) in stages)
        {
            var displayed = hitEvents.Where(e =>
                e.HitObject is not BmsLandmine
                && e.Result.IsBasic()
                && (e.HitObject is BmsHitObject || e.Result == HitResult.Miss)).ToArray();
            overall.AddRange(displayed.Select(e => (e.TimeOffset, e.Result)));

            var variant = beatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
            var totalColumns = beatmap is BmsBeatmap { TotalColumns: > 0 } bmsWithColumns
                ? bmsWithColumns.TotalColumns
                : Math.Max(BmsLayout.GetTotalColumns(variant), displayed.Where(e => e.HitObject is BmsHitObject)
                    .Select(e => ((BmsHitObject)e.HitObject).Column + 1).DefaultIfEmpty(0).Max());
            var keyIndex = 0;

            for (var column = 0; column < totalColumns; column++)
            {
                var label = labelFor(column, variant, ref keyIndex);
                if (!hitsByKey.TryGetValue(column, out var hits))
                {
                    hitsByKey[column] = hits = [];
                    labels.Add(label);
                }

                hits.AddRange(displayed.Where(e => e.HitObject is BmsHitObject hitObject && hitObject.Column == column)
                                       .Select(e => (e.TimeOffset, e.Result)));
            }
        }

        return new HitOffsetStatistics(
            createSummary(overall),
            labels.Select((label, index) => new KeyHitOffsetStatistics(label, createSummary(hitsByKey[index]))).ToArray());
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

        content.Add(createRow(BmsStrings.Overall, statistics.Overall, graph_height));

        if (expanded)
        {
            foreach (var key in statistics.Keys)
                content.Add(createRow(localiseLabel(key.Label), key.Summary, key_graph_height));
        }
    }

    protected override bool OnClick(ClickEvent e)
    {
        expanded = !expanded;
        rebuild();

        return true;
    }

    private static LocalisableString localiseLabel(string label)
    {
        if (label == "Scratch")
            return BmsStrings.Scratch;

        return int.TryParse(label.AsSpan("Key ".Length), out var key) ? BmsStrings.Key(key) : label;
    }

    private static Drawable createRow(LocalisableString label, HitOffsetSummary summary, float height) => new GridContainer
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

    private static Drawable createLabel(LocalisableString label, HitOffsetSummary summary) => new FillFlowContainer
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
                Colour = summary.AverageOffset < 0 ? fast_colour : slow_colour,
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
            },
            new OsuSpriteText
            {
                Text = BmsStrings.StandardDeviation(summary.StandardDeviation),
                Colour = Color4.White,
                Alpha = 0.75f,
                Font = OsuFont.GetFont(size: 10),
            },
            new OsuSpriteText
            {
                Text = BmsStrings.HitCount(summary.Count),
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
        ],
    };

    private static HitOffsetSummary createSummary(IEnumerable<(double offset, HitResult result)> hits)
    {
        var hitList = hits.ToList();
        var timedValues = hitList.Where(h => h.result is not (HitResult.Meh or HitResult.Miss)).Select(h => h.offset).ToArray();
        var averageOffset = timedValues.Length == 0 ? 0 : timedValues.Average();
        var standardDeviation = timedValues.Length == 0
            ? 0
            : Math.Sqrt(timedValues.Sum(value => Math.Pow(value - averageOffset, 2)) / timedValues.Length);
        var binsByResult = new Dictionary<HitResult, int[]>();
        var binSize = Math.Max(1, Math.Ceiling(timedValues.Select(Math.Abs).DefaultIfEmpty(0).Max() / bins_per_side));
        var roundUp = true;

        foreach (var (offset, result) in hitList)
        {
            if (result is HitResult.Meh or HitResult.Miss)
            {
                if (!binsByResult.TryGetValue(result, out var untimedBins))
                    binsByResult[result] = untimedBins = new int[bin_count];
                untimedBins[result == HitResult.Miss || offset < 0 ? 0 : bin_count - 1]++;
                continue;
            }

            var binOffset = offset / binSize;

            // Alternating exact midpoints avoids visually biasing the distribution toward either side.
            if (Math.Abs(binOffset - (int)binOffset) == 0.5)
            {
                binOffset = (int)binOffset + Math.Sign(binOffset) * (roundUp ? 1 : 0);
                roundUp = !roundUp;
            }

            var bin = Math.Clamp(centre_bin_index + (int)Math.Round(binOffset, MidpointRounding.AwayFromZero), 0, bin_count - 1);

            if (!binsByResult.TryGetValue(result, out var bins))
                binsByResult[result] = bins = new int[bin_count];
            bins[bin]++;
        }

        var results = binsByResult.Keys.OrderBy(r => r.GetIndexForOrderedDisplay()).ToArray();

        return new HitOffsetSummary(
            hitList.Count,
            averageOffset,
            standardDeviation,
            timedValues.Count(v => v < 0),
            timedValues.Count(v => v > 0),
            binSize,
            results,
            binsByResult);
    }

    private static readonly Color4 fast_colour = new(90, 175, 255, 255);
    private static readonly Color4 slow_colour = new(255, 130, 92, 255);

    internal sealed record HitOffsetStatistics(HitOffsetSummary Overall, IReadOnlyList<KeyHitOffsetStatistics> Keys);

    internal sealed record KeyHitOffsetStatistics(string Label, HitOffsetSummary Summary);

    internal sealed record HitOffsetSummary(
        int Count,
        double AverageOffset,
        double StandardDeviation,
        int FastCount,
        int SlowCount,
        double BinSize,
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

            Padding = new MarginPadding { Horizontal = 5 };

            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions =
                [
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 13),
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
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = Enumerable.Range(0, bin_count).Select(_ => new Dimension()).ToArray(),
                Content = new[]
                {
                    Enumerable.Range(0, bin_count).Select(b => createBar(b, maxTotal)).ToArray(),
                },
            },
        };

        private Drawable createAxis()
        {
            var axis = new Container
            {
                RelativeSizeAxes = Axes.Both,
            };

            axis.Add(new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "0",
                Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
            });

            var maxValue = bins_per_side * summary.BinSize;
            var axisValueStep = maxValue / axis_points;

            for (var i = 1; i <= axis_points; i++)
            {
                var axisValue = i * axisValueStep;
                var position = (float)(axisValue / maxValue);
                var alpha = 1f - position * 0.8f;

                axis.AddRange([
                    createTickLabel(-axisValue, -position / 2, alpha),
                    createTickLabel(axisValue, position / 2, alpha),
                ]);
            }

            return axis;
        }

        private static OsuSpriteText createTickLabel(double offset, float x, float alpha) => new()
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativePositionAxes = Axes.X,
            X = x,
            Text = offset.ToString("+0;-0;0"),
            Alpha = alpha,
            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
        };

        private Drawable createBar(int binIndex, int maxTotal)
        {
            var bar = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
            };

            var values = summary.Results
                .Select(result => (result, count: summary.BinsByResult[result][binIndex]))
                .Where(value => value.count > 0)
                .ToArray();

            if (values.Length == 0)
            {
                bar.Add(new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Height = minimum_bar_height,
                    Colour = binIndex == centre_bin_index ? Color4.White : Color4.Gray,
                });
                return bar;
            }

            float cumulativeBelow = 0;

            for (var i = 0; i < values.Length; i++)
            {
                var (result, count) = values[i];
                var height = minimum_bar_height + (1 - minimum_bar_height) * count / maxTotal;
                bar.Add(new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    RelativePositionAxes = Axes.Y,
                    Y = -(1 - minimum_bar_height) * cumulativeBelow / maxTotal,
                    Height = height,
                    Colour = binIndex == centre_bin_index && i == 0 && result is not (HitResult.Meh or HitResult.Miss)
                        ? Color4.White
                        : BmsHitResultColours.ForHitResult(result),
                });
                cumulativeBelow += count;
            }

            return bar;
        }
    }
}
