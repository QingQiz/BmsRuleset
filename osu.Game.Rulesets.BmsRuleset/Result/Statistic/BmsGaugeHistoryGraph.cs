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
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Result.Statistic;

public sealed partial class BmsGaugeHistoryGraph : CompositeDrawable
{
    private const float graph_height = 180;
    private const float final_line_radius = 2.0f;
    private const float secondary_line_radius = 1.2f;
    private const float secondary_line_alpha = 0.55f;
    internal const float FAILURE_MARKER_SIZE = 13;
    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    private readonly IReadOnlyList<GaugeSeries> series;
    private readonly IReadOnlyList<float> stageBoundaries;

    public BmsGaugeHistoryGraph(ScoreInfo score, IBeatmap playableBeatmap)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        series = CreateSeries(score, playableBeatmap);
        stageBoundaries = [];
    }

    internal BmsGaugeHistoryGraph(IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)> stages)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        series = CreateCourseSeries(stages);
        stageBoundaries = CreateCourseStageBoundaries(stages);
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
        var profileFamily = playableBeatmap is BmsBeatmap bmsBeatmap
            ? BmsGaugeProfileFamilyProvider.FromLayout(bmsBeatmap.LayoutVariant)
            : BmsGaugeProfileFamily.SevenKeys;

        if (BmsScoreGaugeHistoryStore.TryGet(score, out var gaugeHistory) && gaugeHistory.Count > 0)
            return createSeries(score, gaugeHistory, graphDuration(playableBeatmap, gaugeHistory.Max(e => e.Time)), profileFamily);

        var hitEvents = (BmsJudgementEventStore.TryGet(score, out var judgementEvents)
                ? BmsJudgementEventProjection.CreateScoringHitEvents(judgementEvents)
                : score.HitEvents)
            .ToArray();

        if (hitEvents.Length == 0)
            return [];

        var noteCount = playableBeatmap.HitObjects.Count(h => h is not BmsLandmine);
        if (noteCount == 0)
            noteCount = 1;

        var total = playableBeatmap is BmsBeatmap bms ? bms.Total : 0;
        var duration = graphDuration(playableBeatmap, hitEvents.Max(e => e.HitObject.GetEndTime()));

        var gaugeTypes = gaugeTypesFor(score.Mods).ToArray();
        var finalGaugeType = finalGaugeTypeFor(score.Mods, gaugeTypes);

        return gaugeTypes.Select(type => createSeries(type, hitEvents, total, noteCount, duration, type == finalGaugeType, profileFamily)).ToArray();
    }

    internal static IReadOnlyList<GaugeSeries> CreateCourseSeries(IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)> stages)
    {
        if (stages.Count == 0)
            return [];

        var stageSeries = stages.Select(stage => stage.Score != null ? CreateSeries(stage.Score, stage.Beatmap) : []).ToArray();
        var durations = stages.Select(courseStageDuration).ToArray();
        var totalDuration = durations.Sum();
        var stageOffsets = new double[stages.Count];

        for (var i = 1; i < stageOffsets.Length; i++)
            stageOffsets[i] = stageOffsets[i - 1] + durations[i - 1];

        var names = stageSeries.SelectMany(stage => stage.Select(gauge => gauge.Name)).Distinct().ToArray();

        return names.Select(name =>
        {
            var matching = stageSeries.Select(stage => stage.FirstOrDefault(gauge => gauge.Name == name)).ToArray();
            var template = matching.Last(gauge => gauge != null)!;
            var points = new List<GaugePoint>();
            var segments = new List<IReadOnlyList<GaugePoint>>();
            GaugePoint? failurePoint = null;

            for (var i = 0; i < matching.Length; i++)
            {
                var gauge = matching[i];
                if (gauge == null)
                    continue;

                var segment = gauge.Points.Select(point => point with
                {
                    Time = (float)((stageOffsets[i] + point.Time * durations[i]) / totalDuration),
                }).ToArray();

                points.AddRange(segment);
                segments.Add(segment);

                if (failurePoint == null && gauge.FailurePoint is { } stageFailure)
                {
                    failurePoint = stageFailure with
                    {
                        Time = (float)((stageOffsets[i] + stageFailure.Time * durations[i]) / totalDuration),
                    };
                }
            }

            return template with
            {
                Points = points,
                Segments = segments,
                FailurePoint = failurePoint,
            };
        }).ToArray();
    }

    internal static IReadOnlyList<float> CreateCourseStageBoundaries(IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)> stages)
    {
        if (stages.Count < 2)
            return [];

        var durations = stages.Select(courseStageDuration).ToArray();
        var totalDuration = durations.Sum();
        var boundaries = new float[stages.Count - 1];
        double elapsed = 0;

        for (var i = 0; i < boundaries.Length; i++)
        {
            elapsed += durations[i];
            boundaries[i] = (float)(elapsed / totalDuration);
        }

        return boundaries;
    }

    private static double courseStageDuration((ScoreInfo? Score, IBeatmap Beatmap) stage)
    {
        if (stage.Score == null)
            return graphDuration(stage.Beatmap, 0);

        if (BmsScoreGaugeHistoryStore.TryGet(stage.Score, out var gaugeHistory) && gaugeHistory.Count > 0)
            return graphDuration(stage.Beatmap, gaugeHistory.Max(e => e.Time));

        var hitEvents = BmsJudgementEventStore.TryGet(stage.Score, out var judgementEvents)
            ? BmsJudgementEventProjection.CreateScoringHitEvents(judgementEvents)
            : stage.Score.HitEvents;
        var lastEventTime = hitEvents.Select(e => e.HitObject.GetEndTime()).DefaultIfEmpty(0).Max();

        return graphDuration(stage.Beatmap, lastEventTime);
    }

    private static IReadOnlyList<GaugeSeries> createSeries(
        ScoreInfo score,
        IReadOnlyList<BmsGaugeHistoryEvent> gaugeHistory,
        double duration,
        BmsGaugeProfileFamily profileFamily)
    {
        var ordered = gaugeHistory.ToArray();

        var gaugeTypes = ordered
            .SelectMany(e => e.States.Select(s => s.GaugeType))
            .Distinct()
            .OrderByDescending(type => (int)type)
            .ToArray();

        var finalGaugeType = finalGaugeTypeFor(score.Mods, gaugeTypes) ?? ordered[^1].ActiveGaugeType;
        return gaugeTypes.Select(type => createSeries(type, ordered, duration, type == finalGaugeType, profileFamily)).ToArray();
    }

    private static double graphDuration(IBeatmap playableBeatmap, double lastEventTime)
    {
        var beatmapEndTime = playableBeatmap.HitObjects
            .Select(hitObject => hitObject.GetEndTime())
            .DefaultIfEmpty(0)
            .Max();

        return Math.Max(1, Math.Max(beatmapEndTime, lastEventTime));
    }

    // Late LN results must retain their application order, so clamp their plotted time instead of reordering gauge states.
    private static float monotonicGraphTime(double eventTime, double duration, float previousTime)
        => Math.Max(previousTime, (float)Math.Clamp(eventTime / duration, 0, 1));

    private static GaugeSeries createSeries(
        BmsGaugeType type,
        IReadOnlyList<BmsGaugeHistoryEvent> history,
        double duration,
        bool isFinalUsedGauge,
        BmsGaugeProfileFamily profileFamily)
    {
        var profile = BmsGaugeProfileFactory.Create(type, profileFamily);
        var initialState = history.FirstOrDefault(e => e.Time <= 0)?.States.FirstOrDefault(state => state.GaugeType == type);
        var health = initialState?.Health ?? profile.InitialHealth;
        GaugePoint? failurePoint = null;
        var points = new List<GaugePoint>
        {
            new(0, normaliseHealth(profile, health)),
        };

        foreach (var gaugeEvent in history)
        {
            var time = monotonicGraphTime(gaugeEvent.Time, duration, points[^1].Time);
            var state = gaugeEvent.States.FirstOrDefault(s => s.GaugeType == type);

            if (state != null)
            {
                health = state.Health;

                if (failurePoint == null && (state.Failed || health <= 0))
                    failurePoint = new GaugePoint(time, normaliseHealth(profile, health));
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
            isFinalUsedGauge ? 1 : secondary_line_alpha,
            [points]);
    }

    private Drawable createGraph()
    {
        var plotBackground = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Color4.Black,
            Alpha = 0.22f,
        };

        var graph = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = graph_height,
            Children =
            [
                plotBackground,
                createHorizontalLine(0.2f, "80%"),
                createHorizontalLine(0.5f, "50%"),
            ],
        };

        foreach (var gauge in series)
        {
            foreach (var segment in gauge.Segments)
            {
                graph.Add(new GaugePath(pointsForPath(segment, gauge.FailurePoint), 0)
                {
                    PathRadius = gauge.LineRadius,
                    Colour = gauge.Colour,
                    Alpha = gauge.LineAlpha,
                    Name = $"{gauge.Name} gauge history",
                });
            }

            if (gauge.FailurePoint is { } failurePoint)
                graph.Add(createFailureMarker(gauge, failurePoint));
        }

        foreach (var boundary in stageBoundaries)
            graph.Add(createStageBoundary(boundary));

        return graph;
    }

    private static Drawable createStageBoundary(float fraction) => new Box
    {
        RelativeSizeAxes = Axes.Y,
        RelativePositionAxes = Axes.X,
        X = fraction,
        Width = 1,
        Colour = Color4.White,
        Alpha = 0.18f,
    };

    private static IReadOnlyList<GaugePoint> pointsForPath(IReadOnlyList<GaugePoint> source, GaugePoint? failurePoint)
    {
        if (failurePoint is not { } pointAtFailure)
            return source;

        var points = new List<GaugePoint>();

        foreach (var point in source)
        {
            points.Add(point);

            if (samePoint(point, pointAtFailure))
                break;
        }

        return points;
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

    private static GaugeSeries createSeries(
        BmsGaugeType type,
        IReadOnlyList<HitEvent> hitEvents,
        double total,
        int noteCount,
        double duration,
        bool isFinalUsedGauge,
        BmsGaugeProfileFamily profileFamily)
    {
        var profile = BmsGaugeProfileFactory.Create(type, profileFamily);
        var calculator = new BmsGaugeCalculator(profile, total, noteCount, profileFamily);
        var health = profile.InitialHealth;
        var failed = false;
        GaugePoint? failurePoint = null;
        var points = new List<GaugePoint>
        {
            new(0, normaliseHealth(profile, health)),
        };

        foreach (var hitEvent in hitEvents)
        {
            var time = monotonicGraphTime(hitEvent.HitObject.GetEndTime(), duration, points[^1].Time);

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
            isFinalUsedGauge ? 1 : secondary_line_alpha,
            [points]);
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
        {
            var resolvedGaugeType = mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType;
            if (resolvedGaugeType is BmsGaugeType.Class or BmsGaugeType.ExClass or BmsGaugeType.ExHardClass)
                return BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN;

            return BmsModAutoGauge.AUTO_GAUGE_CHAIN;
        }

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
        float LineAlpha,
        IReadOnlyList<IReadOnlyList<GaugePoint>> Segments);

    internal readonly record struct GaugePoint(float Time, float Health);

    internal static Vector2 CalculateFailureMarkerPosition(
        IReadOnlyList<GaugePoint> points,
        GaugePoint failurePoint,
        float pathRadius,
        Vector2 size)
        => positionFor(failurePoint, size);

    private static Vector2 positionFor(GaugePoint point, Vector2 size) =>
        new(point.Time * size.X, (1 - point.Health) * size.Y);

    private static bool samePoint(GaugePoint first, GaugePoint second) =>
        Math.Abs(first.Time - second.Time) < 0.0001f
        && Math.Abs(first.Health - second.Health) < 0.0001f;

    private static bool sameSize(Vector2 first, Vector2 second) =>
        Math.Abs(first.X - second.X) < 0.001f && Math.Abs(first.Y - second.Y) < 0.001f;

    internal partial class GaugePath : SmoothPath
    {
        private readonly IReadOnlyList<GaugePoint> points;
        private readonly float verticalPadding;
        private readonly LayoutValue verticesCache = new(Invalidation.RequiredParentSizeToFit);
        private Vector2 lastParentSize = new(float.NaN);

        public GaugePath(IReadOnlyList<GaugePoint> points, float verticalPadding)
        {
            this.points = points;
            this.verticalPadding = verticalPadding;
            AutoSizeAxes = Axes.None;
            AddLayout(verticesCache);
        }

        public override float PathRadius
        {
            get => base.PathRadius;
            set
            {
                if (base.PathRadius == value)
                    return;

                base.PathRadius = value;
                verticesCache.Invalidate();
            }
        }

        protected override void Update()
        {
            base.Update();

            if (Parent == null)
                return;

            var parentSize = Parent.DrawSize;

            if (!sameSize(parentSize, lastParentSize))
                verticesCache.Invalidate();

            if (verticesCache.IsValid)
                return;

            updateVertices(parentSize);
            verticesCache.Validate();
        }

        private void updateVertices(Vector2 parentSize)
        {
            ClearVertices();

            var size = new Vector2(parentSize.X, parentSize.Y - verticalPadding * 2);
            var padding = PathRadius;

            Size = size + new Vector2(padding * 2);
            Position = new Vector2(-padding, verticalPadding - padding);
            lastParentSize = parentSize;

            foreach (var point in points)
                AddVertex(new Vector2(point.Time * size.X + padding, (1 - point.Health) * size.Y + padding));
        }
    }

    internal partial class GaugeFailureMarker : Container
    {
        public const float SIZE = FAILURE_MARKER_SIZE;

        private readonly IReadOnlyList<GaugePoint> points;
        private readonly GaugePoint failurePoint;
        private readonly float pathRadius;
        private readonly LayoutValue positionCache = new(Invalidation.RequiredParentSizeToFit);
        private Vector2 lastParentSize = new(float.NaN);

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

            if (Parent == null)
                return;

            var parentSize = Parent.DrawSize;

            // RequiredParentSizeToFit only fires when this marker's own size changes, but the
            // marker is a fixed square — a parent (graph) resize never invalidates the cache, so
            // the position would stick at its first-computed value. Detect the resize manually.
            if (!sameSize(parentSize, lastParentSize))
                positionCache.Invalidate();

            if (positionCache.IsValid)
                return;

            updatePosition(parentSize);
            positionCache.Validate();
        }

        private void updatePosition(Vector2 parentSize)
        {
            lastParentSize = parentSize;
            Position = CalculateFailureMarkerPosition(points, failurePoint, pathRadius, parentSize);
        }
    }
}
