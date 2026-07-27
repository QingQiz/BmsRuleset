using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

internal static class ManiaTimingMapConverter
{
    // osu! control points are time-based and need finer precision than a native 192-tick BMS grid.
    private const int tick_resolution = 192_000;
    private const double ticks_per_beat = tick_resolution / 4d;
    private const double timing_epsilon = 0.001;

    public static BmsTimingMap Create(IBeatmap source)
    {
        var endTime = getEndTime(source);
        var projection = new TimingProjection(source.ControlPointInfo, endTime);
        var measures = createMeasures(projection, endTime);
        var scrollEvents = createScrollEvents(source.ControlPointInfo, projection, endTime);
        var mostCommonBeatLength = source.GetMostCommonBeatLength();
        var referenceBpm = mostCommonBeatLength > 0 && double.IsFinite(mostCommonBeatLength)
            ? 60000 / mostCommonBeatLength
            : TimingControlPoint.DEFAULT.BPM;

        return new BmsTimingMap(
            tick_resolution,
            measures,
            projection.BpmEvents,
            [],
            scrollEvents,
            [],
            referenceBpm);
    }

    private static double getEndTime(IBeatmap source)
    {
        if (source.HitObjects.Count > 0)
            return Math.Max(0, source.HitObjects.Max(h => h.GetEndTime()));

        return Math.Max(0, source.ControlPointInfo.TimingPoints.LastOrDefault()?.Time ?? 0);
    }

    private static IReadOnlyList<BmsMeasureInfo> createMeasures(TimingProjection projection, double endTime)
    {
        var measureStartTicks = new SortedSet<long> { 0 };

        for (var i = 0; i < projection.Segments.Count; i++)
        {
            var segment = projection.Segments[i];
            var barDuration = segment.TimingPoint.BeatLength * segment.TimingPoint.TimeSignature.Numerator;

            if (segment.Time > 0)
                measureStartTicks.Add(segment.Tick);

            if (!double.IsFinite(barDuration) || barDuration <= 0)
                continue;

            var segmentEnd = i + 1 < projection.Segments.Count
                ? projection.Segments[i + 1].Time
                : endTime + barDuration;
            var boundary = firstMeasureBoundaryAfter(segment, barDuration);

            while (boundary <= segmentEnd + timing_epsilon)
            {
                if (boundary > segment.Time + timing_epsilon)
                    measureStartTicks.Add(projection.TickAt(boundary));

                boundary += barDuration;
            }
        }

        var endTick = projection.TickAt(endTime);
        var lastSegment = projection.Segments[^1];
        var fallbackLength = Math.Max(1L, timeToTicks(
            lastSegment.TimingPoint.BeatLength * lastSegment.TimingPoint.TimeSignature.Numerator,
            lastSegment.TimingPoint.BPM));

        if (measureStartTicks.Max <= endTick)
            measureStartTicks.Add(checked(measureStartTicks.Max + fallbackLength));

        var starts = measureStartTicks.ToArray();
        var measures = new List<BmsMeasureInfo>(starts.Length);

        for (var i = 0; i < starts.Length; i++)
        {
            var length = i + 1 < starts.Length
                ? starts[i + 1] - starts[i]
                : i > 0
                    ? starts[i] - starts[i - 1]
                    : tick_resolution;

            length = Math.Max(1, length);
            measures.Add(new BmsMeasureInfo(i, starts[i], length, (double)length / tick_resolution));
        }

        return measures;
    }

    private static double firstMeasureBoundaryAfter(TimingSegment segment, double barDuration)
    {
        if (segment.Time > 0)
            return segment.Time + barDuration;

        var barsFromOrigin = Math.Ceiling((segment.Time - segment.OriginTime) / barDuration);
        var boundary = segment.OriginTime + barsFromOrigin * barDuration;

        if (boundary <= segment.Time + timing_epsilon)
            boundary += barDuration;

        return boundary;
    }

    private static IReadOnlyList<BmsScrollEvent> createScrollEvents(
        ControlPointInfo controlPoints,
        TimingProjection projection,
        double endTime)
    {
        var events = new List<BmsScrollEvent>
        {
            new(0, controlPoints.EffectPointAt(0).ScrollSpeed, 0),
        };
        var sequence = 1;

        foreach (var point in controlPoints.EffectPoints)
        {
            if (point.Time <= 0 || point.Time > endTime)
                continue;

            events.Add(new BmsScrollEvent(projection.TickAt(point.Time), point.ScrollSpeed, sequence++));
        }

        return events;
    }

    private static long timeToTicks(double duration, double bpm) =>
        checked((long)Math.Round(duration * bpm * ticks_per_beat / 60000, MidpointRounding.AwayFromZero));

    private sealed class TimingProjection
    {
        public IReadOnlyList<TimingSegment> Segments => segments;

        private readonly List<TimingSegment> segments = [];

        public IReadOnlyList<BmsBpmEvent> BpmEvents => bpmEvents;

        private readonly List<BmsBpmEvent> bpmEvents = [];

        public TimingProjection(ControlPointInfo controlPoints, double endTime)
        {
            var initial = controlPoints.TimingPointAt(0);
            segments.Add(new TimingSegment(0, initial.Time, 0, initial));
            bpmEvents.Add(new BmsBpmEvent(0, initial.BPM, 0));

            var previousTime = 0d;
            var previousTick = 0L;
            var previousBpm = initial.BPM;
            var sequence = 1;

            foreach (var point in controlPoints.TimingPoints)
            {
                if (point.Time <= 0 || point.Time > endTime)
                    continue;

                var tick = checked(previousTick + timeToTicks(point.Time - previousTime, previousBpm));
                segments.Add(new TimingSegment(point.Time, point.Time, tick, point));
                bpmEvents.Add(new BmsBpmEvent(tick, point.BPM, point.Time, sequence++));

                previousTime = point.Time;
                previousTick = tick;
                previousBpm = point.BPM;
            }
        }

        public long TickAt(double time)
        {
            var low = 0;
            var high = segments.Count - 1;
            var index = 0;

            while (low <= high)
            {
                var middle = low + (high - low) / 2;

                if (segments[middle].Time <= time)
                {
                    index = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var segment = segments[index];
            return checked(segment.Tick + timeToTicks(time - segment.Time, segment.TimingPoint.BPM));
        }
    }

    private readonly record struct TimingSegment(
        double Time,
        double OriginTime,
        long Tick,
        TimingControlPoint TimingPoint);
}
