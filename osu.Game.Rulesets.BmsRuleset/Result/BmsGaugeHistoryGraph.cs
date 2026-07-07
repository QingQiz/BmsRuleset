using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Layout;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result;

public sealed partial class BmsGaugeHistoryGraph : CompositeDrawable
{
    private const float graph_height = 180;
    private const float final_line_radius = 2.0f;
    private const float secondary_line_radius = 1.2f;
    private const float secondary_line_alpha = 0.55f;
    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    private static readonly BmsGaugeType[] auto_gauge_chain =
    [
        BmsGaugeType.Hazard,
        BmsGaugeType.ExHard,
        BmsGaugeType.Hard,
        BmsGaugeType.Normal,
        BmsGaugeType.Easy,
        BmsGaugeType.AssistEasy,
    ];

    private readonly IReadOnlyList<GaugeSeries> series;

    public BmsGaugeHistoryGraph(ScoreInfo score, IBeatmap playableBeatmap)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        series = CreateSeries(score, playableBeatmap);
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
                createGraph(),
                createLegend(),
            ],
        };
    }

    internal static IReadOnlyList<GaugeSeries> CreateSeries(ScoreInfo score, IBeatmap playableBeatmap)
    {
        var hitEvents = score.HitEvents
            .OrderBy(e => e.HitObject.StartTime)
            .ToArray();

        if (hitEvents.Length == 0)
            return [];

        var noteCount = playableBeatmap.HitObjects.Count(h => h is not BmsLandmine);
        if (noteCount == 0)
            noteCount = 1;

        var total = playableBeatmap is BmsBeatmap bmsBeatmap ? bmsBeatmap.Total : 0;
        var duration = Math.Max(1, hitEvents.Max(e => e.HitObject.StartTime));

        var gaugeTypes = gaugeTypesFor(score.Mods).ToArray();
        var finalGaugeType = finalGaugeTypeFor(score.Mods, gaugeTypes);

        return gaugeTypes.Select(type => createSeries(type, hitEvents, total, noteCount, duration, type == finalGaugeType)).ToArray();
    }

    private Drawable createGraph()
    {
        var graph = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = graph_height,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.22f,
                },
                createHorizontalLine(0.2f, "80%"),
                createHorizontalLine(0.5f, "50%"),
            ],
        };

        foreach (var gauge in series)
        {
            graph.Add(new GaugePath(gauge.Points)
            {
                AutoSizeAxes = Axes.None,
                RelativeSizeAxes = Axes.Both,
                PathRadius = gauge.LineRadius,
                Colour = gauge.Colour,
                Alpha = gauge.LineAlpha,
                Name = $"{gauge.Name} gauge history",
            });

            if (gauge.FailurePoint is { } failurePoint)
                graph.Add(createFailureMarker(gauge, failurePoint));
        }

        return graph;
    }

    private static Drawable createFailureMarker(GaugeSeries gauge, GaugePoint failurePoint)
    {
        const float shadow_width = 4;
        const float line_width = 2;

        return new GaugeFailureMarker(gauge.Points, failurePoint, gauge.LineRadius)
        {
            Alpha = gauge.IsFinalUsedGauge ? 1 : secondary_line_alpha,
            Children =
            [
                createMarkerLine(Color4.Black, shadow_width, 45, 0.6f),
                createMarkerLine(Color4.Black, shadow_width, -45, 0.6f),
                createMarkerLine(gauge.Colour, line_width, 45, 1),
                createMarkerLine(gauge.Colour, line_width, -45, 1),
            ],
        };

        static Drawable createMarkerLine(Color4 colour, float height, float rotation, float alpha) => new Box
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Width = GaugeFailureMarker.SIZE,
            Height = height,
            Colour = colour,
            Alpha = alpha,
            Rotation = rotation,
        };
    }

    private Drawable createLegend() => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(12, 6),
        Children = series.Select(createLegendItem).ToArray(),
    };

    private Drawable createLegendItem(GaugeSeries gauge) => new FillFlowContainer
    {
        AutoSizeAxes = Axes.Both,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(5, 0),
        Children =
        [
            new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(8),
                Colour = gauge.Colour,
            },
            new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = gauge.Name,
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
            },
        ],
    };

    private Drawable createHorizontalLine(float y, string label) => new Container
    {
        RelativeSizeAxes = Axes.X,
        Height = 1,
        RelativePositionAxes = Axes.Y,
        Y = y,
        Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Colour = Color4.White,
                Alpha = y is 0 or 1 ? 0.18f : 0.08f,
            },
            new OsuSpriteText
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -4,
                Text = label,
                Colour = Color4.White,
                Alpha = 0.45f,
                Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
            },
        ],
    };

    private static GaugeSeries createSeries(BmsGaugeType type, IReadOnlyList<HitEvent> hitEvents, double total, int noteCount, double duration, bool isFinalUsedGauge)
    {
        var profile = BmsGaugeProfileFactory.Create(type);
        var calculator = new BmsGaugeCalculator(profile, total, noteCount);
        var health = profile.InitialHealth;
        var failed = false;
        GaugePoint? failurePoint = null;
        var points = new List<GaugePoint>
        {
            new(0, normaliseHealth(profile, health)),
        };

        foreach (var hitEvent in hitEvents)
        {
            var time = (float)Math.Clamp(hitEvent.HitObject.StartTime / duration, 0, 1);

            if (!failed)
            {
                health = applyHitEvent(hitEvent, calculator, health);

                if (health <= 0)
                {
                    failed = true;
                    failurePoint = new GaugePoint(time, normaliseHealth(profile, health));
                }
            }

            points.Add(new GaugePoint(time, normaliseHealth(profile, health)));
        }

        if (points[^1].Time < 1)
            points.Add(new GaugePoint(1, points[^1].Health));

        if (failurePoint == null && profile.ClearThreshold > 0 && health < profile.ClearThreshold)
            failurePoint = points[^1];

        return new GaugeSeries(
            formatGaugeName(type),
            colourFor(type),
            points,
            failurePoint,
            isFinalUsedGauge,
            isFinalUsedGauge ? final_line_radius : secondary_line_radius,
            isFinalUsedGauge ? 1 : secondary_line_alpha);
    }

    private static double applyHitEvent(HitEvent hitEvent, BmsGaugeCalculator calculator, double health)
    {
        if (hitEvent.HitObject is BmsLandmine mine)
        {
            if (hitEvent.Result != HitResult.Meh)
                return health;

            return mine.LandmineDamagePercent >= max_landmine_damage_percent
                ? 0
                : Math.Max(0, health - mine.LandmineDamagePercent / 100);
        }

        return calculator.ApplyDelta(health, calculator.GetDeltaFor(hitEvent.Result, health));
    }

    private static IEnumerable<BmsGaugeType> gaugeTypesFor(IReadOnlyList<Mod> mods)
    {
        if (mods.OfType<BmsModAutoGauge>().Any())
            return auto_gauge_chain;

        return
        [
            mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType ?? BmsGaugeType.Normal,
        ];
    }

    private static BmsGaugeType? finalGaugeTypeFor(IReadOnlyList<Mod> mods, IReadOnlyList<BmsGaugeType> displayedGaugeTypes)
    {
        var explicitGaugeType = mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType;
        if (explicitGaugeType != null)
            return explicitGaugeType;

        return displayedGaugeTypes.Count == 1 ? displayedGaugeTypes[0] : null;
    }

    private static float normaliseHealth(BmsGaugeProfile profile, double health) =>
        (float)Math.Clamp(health / profile.MaxHealth, 0, 1);

    private static string formatGaugeName(BmsGaugeType gaugeType) => gaugeType switch
    {
        BmsGaugeType.AssistEasy => "Assist Easy",
        BmsGaugeType.Easy => "Easy",
        BmsGaugeType.Normal => "Normal",
        BmsGaugeType.Hard => "Hard",
        BmsGaugeType.ExHard => "ExHard",
        BmsGaugeType.Hazard => "Hazard",
        BmsGaugeType.Class => "Class",
        BmsGaugeType.ExClass => "ExClass",
        BmsGaugeType.ExHardClass => "ExHard Class",
        _ => gaugeType.ToString(),
    };

    private static Color4 colourFor(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => new Color4(75, 210, 235, 255),
        BmsGaugeType.Easy => new Color4(75, 150, 255, 255),
        _ => BmsGaugeProfileFactory.Create(type).Display.FillColour,
    };

    internal sealed record GaugeSeries(
        string Name,
        Color4 Colour,
        IReadOnlyList<GaugePoint> Points,
        GaugePoint? FailurePoint,
        bool IsFinalUsedGauge,
        float LineRadius,
        float LineAlpha);

    internal readonly record struct GaugePoint(float Time, float Health);

    internal static Vector2 CalculateFailureMarkerPosition(
        IReadOnlyList<GaugePoint> points,
        GaugePoint failurePoint,
        float pathRadius,
        Vector2 size)
    {
        var failureIndex = -1;

        for (var i = 0; i < points.Count; i++)
        {
            if (Math.Abs(points[i].Time - failurePoint.Time) < 0.0001f
                && Math.Abs(points[i].Health - failurePoint.Health) < 0.0001f)
            {
                failureIndex = i;
                break;
            }
        }

        if (failureIndex <= 0)
            return positionFor(failurePoint, size);

        var previous = positionFor(points[failureIndex - 1], size);
        var current = positionFor(failurePoint, size);
        var delta = current - previous;
        var length = delta.Length;

        if (length <= 0)
            return current;

        var direction = delta / length;
        var perpendicular = new Vector2(-direction.Y, direction.X);
        var axisPoint = current;
        var t = 1f;

        if (failurePoint.Health <= 0 && delta.Y > 0)
        {
            var centreY = size.Y - Math.Abs(perpendicular.Y) * pathRadius;
            t = Math.Min(t, Math.Clamp((centreY - previous.Y) / delta.Y, 0, 1));
        }

        if (failurePoint.Time >= 1 && delta.X > 0)
        {
            var centreX = size.X - Math.Abs(perpendicular.X) * pathRadius;
            t = Math.Min(t, Math.Clamp((centreX - previous.X) / delta.X, 0, 1));
        }
        else if (failurePoint.Time <= 0 && delta.X < 0)
        {
            var centreX = Math.Abs(perpendicular.X) * pathRadius;
            t = Math.Min(t, Math.Clamp((centreX - previous.X) / delta.X, 0, 1));
        }

        if (t < 1)
            axisPoint = previous + delta * t;

        return axisPoint;
    }

    private static Vector2 positionFor(GaugePoint point, Vector2 size) =>
        new(point.Time * size.X, (1 - point.Health) * size.Y);

    private partial class GaugePath : SmoothPath
    {
        private readonly IReadOnlyList<GaugePoint> points;
        private readonly LayoutValue verticesCache = new(Invalidation.RequiredParentSizeToFit);

        public GaugePath(IReadOnlyList<GaugePoint> points)
        {
            this.points = points;
            AddLayout(verticesCache);
        }

        protected override void Update()
        {
            base.Update();

            if (verticesCache.IsValid)
                return;

            updateVertices();
            verticesCache.Validate();
        }

        private void updateVertices()
        {
            ClearVertices();

            var size = Parent!.DrawSize;

            foreach (var point in points)
                AddVertex(new Vector2(point.Time * size.X, (1 - point.Health) * size.Y));
        }
    }

    private partial class GaugeFailureMarker : Container
    {
        public const float SIZE = 13;

        private readonly IReadOnlyList<GaugePoint> points;
        private readonly GaugePoint failurePoint;
        private readonly float pathRadius;
        private readonly LayoutValue positionCache = new(Invalidation.RequiredParentSizeToFit);

        public GaugeFailureMarker(IReadOnlyList<GaugePoint> points, GaugePoint failurePoint, float pathRadius)
        {
            this.points = points;
            this.failurePoint = failurePoint;
            this.pathRadius = pathRadius;

            Size = new Vector2(SIZE);
            Origin = Anchor.Centre;

            AddLayout(positionCache);
        }

        protected override void Update()
        {
            base.Update();

            if (positionCache.IsValid)
                return;

            updatePosition();
            positionCache.Validate();
        }

        private void updatePosition()
        {
            var size = Parent!.DrawSize;
            Position = CalculateFailureMarkerPosition(points, failurePoint, pathRadius, size);
        }
    }
}
