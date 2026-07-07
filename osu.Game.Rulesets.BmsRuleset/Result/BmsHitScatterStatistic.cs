using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result;

public sealed partial class BmsHitScatterStatistic : CompositeDrawable
{
    private const float graph_height = 220;
    private const float axis_width = 52;
    private const float x_axis_height = 28;
    private const double minimum_offset_range = 150;

    private static readonly Color4 early_colour = new(90, 175, 255, 255);
    private static readonly Color4 late_colour = new(255, 130, 92, 255);

    private readonly ScatterData data;

    public BmsHitScatterStatistic(IReadOnlyList<HitEvent> hitEvents)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        data = CreateData(hitEvents);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
            Children =
            [
                createLegend(),
                new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = graph_height + x_axis_height,
                    ColumnDimensions =
                    [
                        new Dimension(GridSizeMode.Absolute, axis_width),
                        new Dimension(),
                    ],
                    RowDimensions =
                    [
                        new Dimension(GridSizeMode.Absolute, graph_height),
                        new Dimension(GridSizeMode.Absolute, x_axis_height),
                    ],
                    Content = new[]
                    {
                        new[] { createYAxis(), createPlot() },
                        new[] { Empty(), createXAxis() },
                    },
                },
            ],
        };
    }

    internal static ScatterData CreateData(IReadOnlyList<HitEvent> hitEvents)
    {
        var points = hitEvents
            .Where(isScatterHit)
            .Select(e => new ScatterPoint(e.HitObject.StartTime, e.TimeOffset, e.Result))
            .OrderBy(p => p.Time)
            .ToArray();

        var duration = Math.Max(1, points.Select(p => p.Time).DefaultIfEmpty(0).Max());
        var maxMagnitude = Math.Max(minimum_offset_range, points.Select(p => Math.Abs(p.Offset)).DefaultIfEmpty(0).Max());
        var offsetRange = Math.Ceiling(maxMagnitude / 50) * 50;
        var ticks = new[] { -offsetRange, -offsetRange / 2, 0, offsetRange / 2, offsetRange };

        return new ScatterData(points, duration, offsetRange, ticks);
    }

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

    private Drawable createLegend()
    {
        var children = new List<Drawable>
        {
            new OsuSpriteText
            {
                Text = $"{data.Points.Count} hits",
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

    private Drawable createYAxis() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children = data.OffsetTicks.SelectMany(tick => new[] { createYAxisTick(tick), createYAxisMark(tick) }).ToArray(),
    };

    private Drawable createYAxisTick(double tick)
    {
        var y = yFor(tick);

        return new OsuSpriteText
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.CentreRight,
            RelativePositionAxes = Axes.Y,
            Y = y,
            X = -6,
            Text = $"{tick:+0;-0;0} ms",
            Colour = tick < 0 ? early_colour : tick > 0 ? late_colour : Color4.White,
            Alpha = tick == 0 ? 0.75f : 0.55f,
            Font = OsuFont.GetFont(size: 10, weight: tick == 0 ? FontWeight.SemiBold : FontWeight.Regular),
        };
    }

    private Drawable createYAxisMark(double tick) => new Box
    {
        Anchor = Anchor.TopRight,
        Origin = Anchor.CentreRight,
        RelativePositionAxes = Axes.Y,
        Y = yFor(tick),
        Width = tick == 0 ? 12 : 8,
        Height = tick == 0 ? 2 : 1,
        Colour = Color4.White,
        Alpha = tick == 0 ? 0.45f : 0.25f,
    };

    private Drawable createPlot()
    {
        var children = new List<Drawable>
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.18f },
        };

        foreach (var tick in data.OffsetTicks)
            children.Add(createGridLine(tick));

        children.Add(createTimingDirectionLabel("early", early_colour, Anchor.BottomRight));
        children.Add(createTimingDirectionLabel("late", late_colour, Anchor.TopRight));

        children.AddRange(data.Points.Select(createPoint));

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            Children = children,
        };
    }

    private Drawable createGridLine(double tick)
    {
        var y = yFor(tick);

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

    private Drawable createTimingDirectionLabel(string text, Color4 colour, Anchor origin) => new OsuSpriteText
    {
        Anchor = Anchor.TopRight,
        Origin = origin,
        RelativePositionAxes = Axes.Y,
        X = -6,
        Y = yFor(0),
        Text = text,
        Colour = colour,
        Alpha = 0.72f,
        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
    };

    private Drawable createPoint(ScatterPoint point) => new Circle
    {
        Origin = Anchor.Centre,
        RelativePositionAxes = Axes.Both,
        X = (float)Math.Clamp(point.Time / data.Duration, 0, 1),
        Y = yFor(point.Offset),
        Size = new Vector2(point.Result == HitResult.Miss ? 5.2f : 4.4f),
        Colour = BmsHitResultColours.ForHitResult(point.Result),
        Alpha = point.Result == HitResult.Miss ? 0.95f : 0.82f,
    };

    private Drawable createXAxis() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children =
        [
            new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = "start",
                Colour = Color4.White,
                Alpha = 0.55f,
                Font = OsuFont.GetFont(size: 10),
            },
            new OsuSpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Text = "time",
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

    private float yFor(double offset) => (float)Math.Clamp((offset + data.OffsetRange) / (data.OffsetRange * 2), 0, 1);

    private static string formatTime(double milliseconds)
    {
        var seconds = milliseconds / 1000;

        return seconds < 60 ? $"{seconds:0}s" : $"{Math.Floor(seconds / 60):0}:{seconds % 60:00}";
    }

    internal sealed record ScatterData(IReadOnlyList<ScatterPoint> Points, double Duration, double OffsetRange, IReadOnlyList<double> OffsetTicks);

    internal readonly record struct ScatterPoint(double Time, double Offset, HitResult Result);
}
