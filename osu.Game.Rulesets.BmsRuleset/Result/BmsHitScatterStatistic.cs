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
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result;

public sealed partial class BmsHitScatterStatistic : CompositeDrawable
{
    private const float graph_height = 200;
    private const float key_graph_height = 140;
    private const float axis_width = 52;
    private const float x_axis_height = 28;
    private const float label_width = 96;
    private const float point_padding = 3;
    private const double minimum_offset_range = 150;
    private const double maximum_offset_range = 300;

    private static readonly Color4 fast_colour = new(90, 175, 255, 255);
    private static readonly Color4 slow_colour = new(255, 130, 92, 255);

    private readonly HitScatterStatistics statistics;
    private FillFlowContainer content = null!;
    private bool expanded;

    public BmsHitScatterStatistic(IReadOnlyList<HitEvent> hitEvents, IBeatmap playableBeatmap)
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

    internal static HitScatterStatistics CreateStatistics(IBeatmap playableBeatmap, IReadOnlyList<HitEvent> hitEvents)
    {
        var scatterHits = hitEvents.Where(isScatterHit).ToArray();
        var bmsHitEvents = scatterHits.Where(e => e.HitObject is BmsHitObject).ToArray();

        var variant = playableBeatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
        var totalColumns = playableBeatmap is BmsBeatmap { TotalColumns: > 0 } bmsWithColumns
            ? bmsWithColumns.TotalColumns
            : Math.Max(BmsLayout.GetTotalColumns(variant), bmsHitEvents.Select(e => ((BmsHitObject)e.HitObject).Column + 1).DefaultIfEmpty(0).Max());

        var hitsByColumn = bmsHitEvents.ToLookup(e => ((BmsHitObject)e.HitObject).Column);
        var keyIndex = 0;

        var keyGroups = Enumerable.Range(0, totalColumns)
            .Select(column => new KeyHitScatterStatistics(labelFor(column, variant, ref keyIndex), CreateData(hitsByColumn[column].ToArray())))
            .ToArray();

        return new HitScatterStatistics(CreateData(scatterHits), keyGroups);
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

        content.Add(createLegend(statistics.Overall));
        content.Add(createRow(BmsStrings.Overall, statistics.Overall, graph_height));

        if (expanded)
        {
            foreach (var key in statistics.Keys)
                content.Add(createRow(localiseLabel(key.Label), key.Data, key_graph_height));
        }
    }

    protected override bool OnClick(ClickEvent e)
    {
        expanded = !expanded;
        rebuild();

        return true;
    }

    internal static ScatterData CreateData(IReadOnlyList<HitEvent> hitEvents)
    {
        var points = hitEvents
            .Where(isScatterHit)
            .Select(e => new ScatterPoint(e.HitObject.StartTime, e.TimeOffset, e.Result))
            .OrderBy(p => p.Time)
            .ToArray();

        var duration = Math.Max(1, points.Select(p => p.Time).DefaultIfEmpty(0).Max());
        var maxMagnitude = Math.Clamp(
            points.Where(p => p.Result is not (HitResult.Meh or HitResult.Miss)).Select(p => Math.Abs(p.Offset)).DefaultIfEmpty(0).Max(),
            minimum_offset_range,
            maximum_offset_range);
        var offsetRange = Math.Ceiling(maxMagnitude / 50) * 50;
        var ticks = new[] { -offsetRange, -offsetRange / 2, 0, offsetRange / 2, offsetRange };
        points = points.Select(p => p with { Offset = displayedOffsetFor(p, offsetRange) }).ToArray();

        return new ScatterData(points, duration, offsetRange, ticks);
    }

    private static double displayedOffsetFor(ScatterPoint point, double offsetRange) => point.Result switch
    {
        HitResult.Miss => -offsetRange,
        HitResult.Meh => Math.Clamp(point.Offset, -offsetRange, offsetRange),
        _ => point.Offset,
    };

    private static bool isScatterHit(HitEvent e)
    {
        if (e.HitObject is BmsLandmine)
            return false;

        return e.Result switch
        {
            HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh => e.HitObject is BmsHitObject,
            HitResult.Miss => e.HitObject is BmsHitObject or HitObject,
            _ => false,
        };
    }

    private static Drawable createLegend(ScatterData data)
    {
        var children = new List<Drawable>
        {
            new OsuSpriteText
            {
                Text = BmsStrings.HitCount(data.Points.Count),
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            },
        };

        foreach (var result in BmsRuleset.STATIC_VALID_HIT_RESULTS)
        {
            children.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Children =
                [
                    new Circle
                    {
                        Size = new Vector2(8),
                        Colour = BmsHitResultColours.ForHitResult(result),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new OsuSpriteText
                    {
                        Text = BmsRuleset.HIT_RESULT_LABELS[result],
                        Font = OsuFont.GetFont(size: 11),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                ],
            });
        }

        return new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(10, 0),
            Children = children,
        };
    }

    private static LocalisableString localiseLabel(string label)
    {
        if (label == "Scratch")
            return BmsStrings.Scratch;

        return int.TryParse(label.AsSpan("Key ".Length), out var key) ? BmsStrings.Key(key) : label;
    }

    private static Drawable createRow(LocalisableString label, ScatterData data, float height) => new GridContainer
    {
        RelativeSizeAxes = Axes.X,
        Height = height + x_axis_height,
        ColumnDimensions =
        [
            new Dimension(GridSizeMode.Absolute, label_width),
            new Dimension(),
        ],
        Content = new[]
        {
            new[]
            {
                createLabel(label, data),
                createGraph(data, height),
            },
        },
    };

    private static Drawable createLabel(LocalisableString label, ScatterData data) => new FillFlowContainer
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
                Text = BmsStrings.HitCount(data.Points.Count),
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
        ],
    };

    private static Drawable createGraph(ScatterData data, float height) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = height + x_axis_height,
        Children =
        [
            new GridContainer
            {
                RelativeSizeAxes = Axes.X,
                Height = height + x_axis_height,
                RowDimensions =
                [
                    new Dimension(GridSizeMode.Absolute, height),
                    new Dimension(GridSizeMode.Absolute, x_axis_height),
                ],
                Content = new[]
                {
                    new[] { createPlot(data) },
                    new[] { createXAxis(data) },
                },
            },
            new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopRight,
                Width = axis_width,
                Height = height,
                Child = createYAxis(data),
            },
        ],
    };

    private static Drawable createYAxis(ScatterData data) => new Container
    {
        RelativeSizeAxes = Axes.Y,
        Width = axis_width,
        Children = data.OffsetTicks.SelectMany(tick => new[] { createYAxisTick(data, tick), createYAxisMark(data, tick) }).ToArray(),
    };

    private static Drawable createYAxisTick(ScatterData data, double tick)
    {
        var y = yFor(data, tick);

        return new OsuSpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.CentreRight,
            RelativePositionAxes = Axes.Y,
            Y = y,
            X = -10,
            Text = $"{tick:+0;-0;0} ms",
            Colour = tick < 0 ? fast_colour : tick > 0 ? slow_colour : Color4.White,
            Alpha = tick == 0 ? 0.75f : 0.55f,
            Font = OsuFont.GetFont(size: 10, weight: tick == 0 ? FontWeight.SemiBold : FontWeight.Regular),
        };
    }

    private static Drawable createYAxisMark(ScatterData data, double tick) => new Box
    {
        Anchor = Anchor.TopRight,
        Origin = Anchor.CentreRight,
        RelativePositionAxes = Axes.Y,
        Y = yFor(data, tick),
        X = 0,
        Width = tick == 0 ? 8 : 5,
        Height = tick == 0 ? 2 : 1,
        Colour = Color4.White,
        Alpha = tick == 0 ? 0.45f : 0.25f,
    };

    private static Drawable createPlot(ScatterData data)
    {
        var dataAreaChildren = new List<Drawable>();

        foreach (var tick in data.OffsetTicks)
            dataAreaChildren.Add(createGridLine(data, tick));

        dataAreaChildren.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding(point_padding),
            Children = data.Points.Select(point => createPoint(data, point)).ToArray(),
        });

        dataAreaChildren.Add(createTimingDirectionLabel(BmsStrings.Fast, fast_colour, Anchor.TopRight));
        dataAreaChildren.Add(createTimingDirectionLabel(BmsStrings.Slow, slow_colour, Anchor.BottomRight));

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            Children =
            [
                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.18f },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = dataAreaChildren,
                },
            ],
        };
    }

    private static Drawable createGridLine(ScatterData data, double tick)
    {
        var y = yFor(data, tick);

        return new Box
        {
            RelativeSizeAxes = Axes.X,
            Height = tick == 0 ? 2 : 1,
            RelativePositionAxes = Axes.Y,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.CentreLeft,
            Y = y,
            Colour = Color4.White,
            Alpha = tick == 0 ? 0.32f : 0.1f,
        };
    }

    private static Drawable createTimingDirectionLabel(LocalisableString text, Color4 colour, Anchor anchor) => new OsuSpriteText
    {
        Anchor = anchor,
        Origin = anchor,
        X = -6,
        Y = anchor == Anchor.TopRight ? 6 : -6,
        Text = text,
        Colour = colour,
        Alpha = 0.72f,
        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
    };

    private static Drawable createPoint(ScatterData data, ScatterPoint point) => new Circle
    {
        Origin = Anchor.Centre,
        RelativePositionAxes = Axes.Both,
        X = (float)Math.Clamp(point.Time / data.Duration, 0, 1),
        Y = yFor(data, point.Offset),
        Size = new Vector2(point.Result == HitResult.Miss ? 5.2f : 4.4f),
        Colour = BmsHitResultColours.ForHitResult(point.Result),
        Alpha = point.Result == HitResult.Miss ? 0.95f : 0.82f,
    };

    private static Drawable createXAxis(ScatterData data) => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children =
        [
            new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = BmsStrings.Start,
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
            new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = BmsStrings.Time,
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
            },
            new OsuSpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Text = formatTime(data.Duration),
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
        ],
    };

    private static float yFor(ScatterData data, double offset) => (float)Math.Clamp((offset + data.OffsetRange) / (data.OffsetRange * 2), 0, 1);

    private static string formatTime(double milliseconds)
    {
        var seconds = milliseconds / 1000;

        return seconds < 60 ? $"{seconds:0}s" : $"{Math.Floor(seconds / 60):0}:{seconds % 60:00}";
    }

    internal sealed record HitScatterStatistics(ScatterData Overall, IReadOnlyList<KeyHitScatterStatistics> Keys);

    internal sealed record KeyHitScatterStatistics(string Label, ScatterData Data);

    internal sealed record ScatterData(IReadOnlyList<ScatterPoint> Points, double Duration, double OffsetRange, IReadOnlyList<double> OffsetTicks);

    internal readonly record struct ScatterPoint(double Time, double Offset, HitResult Result);
}
