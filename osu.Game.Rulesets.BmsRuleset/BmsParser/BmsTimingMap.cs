using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed class BmsTimingMap
{

    public int TickResolution { get; }

    public IReadOnlyList<BmsMeasureInfo> Measures => measures;

    public IReadOnlyList<long> BarLines { get; }

    /// <summary>Backed by array for zero-overhead access in hot-path binary searches.</summary>
    private readonly BmsMeasureInfo[] measures;

    public IReadOnlyList<BmsBpmEvent> BpmEvents => bpmEvents;

    private readonly BmsBpmEvent[] bpmEvents;

    public IReadOnlyList<BmsStopEvent> StopEvents => stopEvents;

    private readonly BmsStopEvent[] stopEvents;

    public IReadOnlyList<BmsScrollEvent> ScrollEvents => scrollEvents;

    private readonly BmsScrollEvent[] scrollEvents;

    public IReadOnlyList<BmsSpeedEvent> SpeedEvents => speedEvents;

    private readonly BmsSpeedEvent[] speedEvents;

    /// <summary>
    ///     BPM used to express scroll coordinates as millisecond-like values.
    ///     At the chart's initial valid BPM, native tick-scroll matches the old time-based projection scale.
    /// </summary>
    public double ScrollReferenceBpm { get; }

    private readonly double? explicitScrollReferenceBpm;
    private readonly double mainBpm;

    // ── Precomputed timing point array (build once, query with walking cursor) ──

    private readonly struct TimingPoint(
        double time,
        double nextTime,
        double scrollTick,
        double bpm,
        double scrollFactor,
        double speedFactor,
        int scrollDir,
        bool isStop)
    {
        public readonly double Time = time;                 // segment start (ms)
        public readonly double NextTime = nextTime;         // segment end (ms), double.PositiveInfinity for last
        public readonly double ScrollTick = scrollTick;     // independent of the playback reference BPM
        public readonly double Bpm = bpm;                   // active |BPM| for this segment (always positive)
        public readonly double ScrollFactor = scrollFactor; // active SCROLL factor
        public readonly double SpeedFactor = speedFactor;   // active SPEED factor
        public readonly int ScrollDir = scrollDir;          // 1 = normal, -1 = reverse (negative BPM)
        public readonly bool IsStop = isStop;               // true → scroll position frozen during this segment
    }

    private readonly TimingPoint[] points;

    // ── Tick→time infrastructure ──

    private readonly BmsTickTimeConverter tickTimeConverter;
    private int cursor;

    // ── Construction ──────────────────────────────────────────────────────────

    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                        IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                        IEnumerable<BmsScrollEvent> scrollEvents, IEnumerable<BmsSpeedEvent> speedEvents,
                        double? explicitScrollReferenceBpm = null)
        : this(tickResolution, measures, bpmEvents, stopEvents, scrollEvents, speedEvents, false, null, explicitScrollReferenceBpm)
    {
    }

    private BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                         IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                         IEnumerable<BmsScrollEvent> scrollEvents, IEnumerable<BmsSpeedEvent> speedEvents,
                         bool alreadySorted, double? mainBpm, double? explicitScrollReferenceBpm, IReadOnlyList<long>? barLines = null)
    {
        TickResolution = tickResolution;
        this.measures = (alreadySorted ? measures : measures.OrderBy(m => m.Index)).ToArray();
        BarLines = barLines ?? this.measures.Select(measure => measure.StartTick).ToArray();
        this.bpmEvents = (alreadySorted ? bpmEvents : bpmEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence)).ToArray();
        this.stopEvents = (alreadySorted ? stopEvents : stopEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence)).ToArray();
        this.scrollEvents = (alreadySorted ? scrollEvents : scrollEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence)).ToArray();
        this.speedEvents = (alreadySorted ? speedEvents : speedEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence)).ToArray();
        this.mainBpm = mainBpm ?? initialBpm();
        ScrollReferenceBpm = explicitScrollReferenceBpm ?? initialBpm();
        this.explicitScrollReferenceBpm = explicitScrollReferenceBpm;
        points = buildTimingPoints();
        tickTimeConverter = new BmsTickTimeConverter(tickResolution, this.bpmEvents, this.stopEvents);
    }

    internal static BmsTimingMap FromSortedEvents(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                                                  IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                                                  IEnumerable<BmsScrollEvent> scrollEvents, IEnumerable<BmsSpeedEvent> speedEvents, double mainBpm,
                                                  double? explicitScrollReferenceBpm = null, IReadOnlyList<long>? barLines = null) =>
        new(tickResolution, measures, bpmEvents, stopEvents, scrollEvents, speedEvents, true, mainBpm, explicitScrollReferenceBpm, barLines);

    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                        IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                        double? explicitScrollReferenceBpm = null)
        : this(tickResolution, measures, bpmEvents, stopEvents, [], [], explicitScrollReferenceBpm)
    {
    }

    private BmsTimingMap(BmsTimingMap source, double referenceBpm)
    {
        TickResolution = source.TickResolution;
        measures = source.measures;
        BarLines = source.BarLines;
        bpmEvents = source.bpmEvents;
        stopEvents = source.stopEvents;
        scrollEvents = source.scrollEvents;
        speedEvents = source.speedEvents;
        mainBpm = source.mainBpm;
        explicitScrollReferenceBpm = source.explicitScrollReferenceBpm;
        ScrollReferenceBpm = referenceBpm;
        points = source.points;
        tickTimeConverter = source.tickTimeConverter;
    }

    // Each conversion owns its query cursor; previews and other attempts share only immutable data.
    internal BmsTimingMap WithReferenceBpm(BmsReferenceBpmMode mode) => new(this, explicitScrollReferenceBpm ?? (mode switch
    {
        BmsReferenceBpmMode.MaxBpm => bpmEvents.Max(evt => Math.Abs(evt.Bpm)),
        BmsReferenceBpmMode.MinBpm => bpmEvents.Min(evt => Math.Abs(evt.Bpm)),
        BmsReferenceBpmMode.MainBpm => mainBpm,
        _ => initialBpm(),
    }));

    internal static double FindMainBpm(IReadOnlyList<BmsBpmEvent> events, IEnumerable<long> noteTicks)
    {
        var counts = new Dictionary<double, (int Count, long FirstTick)>();
        foreach (var tick in noteTicks)
        {
            var low = 0;
            var high = events.Count - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (events[middle].Tick <= tick)
                    low = middle;
                else
                    high = middle - 1;
            }

            var bpm = Math.Abs(events[low].Bpm);
            counts[bpm] = counts.TryGetValue(bpm, out var count) ? (count.Count + 1, count.FirstTick) : (1, tick);
        }

        return counts.Count == 0 ? Math.Abs(events[0].Bpm)
            : counts.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Value.FirstTick).First().Key;
    }

    // ── Time-based queries ────────────────────────────────────────────────────

    /// <summary>
    ///     Returns the native BMS scroll coordinate at a tick position.
    ///     SCROLL and SPEED factors are NOT applied here — use <see cref="GetVisualScrollPositionAtTick"/>
    ///     for the display-coordinate equivalent that accounts for <c>#SCROLLxx</c>.
    /// </summary>
    public double GetScrollPositionAtTick(double tick) => ticksToMilliseconds(tick, ScrollReferenceBpm);

    /// <summary>
    ///     Returns the display scroll coordinate for a tick position, with
    ///     <c>#SCROLLxx</c> factors applied per segment. Notes in a 2× SCROLL zone
    ///     get 2× the scroll distance for the same tick range, spreading them apart visually.
    /// </summary>
    public double GetVisualScrollPositionAtTick(double tick)
    {
        if (scrollEvents.Length == 0)
            return ticksToMilliseconds(tick, ScrollReferenceBpm);

        double position = 0;
        long prevTick = 0;
        var factor = 1.0;
        var scrollIdx = 0;

        foreach (var evt in ScrollEvents)
        {
            if (evt.Tick >= tick)
                break;

            if (evt.Tick > prevTick)
            {
                position += ticksToMilliseconds(evt.Tick - prevTick, ScrollReferenceBpm) * factor;
                prevTick = evt.Tick;
            }

            factor = evt.Factor;
            scrollIdx++;

            if (scrollIdx >= scrollEvents.Length)
                break;
        }

        if (tick > prevTick)
            position += ticksToMilliseconds(tick - prevTick, ScrollReferenceBpm) * factor;

        return position;
    }

    /// <summary>
    ///     Returns the native BMS scroll coordinate reached at a projected osu! time,
    ///     accounting for BPM changes, STOP segments, and SCROLL factors.
    /// </summary>
    public double GetScrollPositionAtTime(double time)
    {
        if (points.Length == 0)
            return time;

        var idx = findPoint(time);
        var pt = points[idx];

        if (pt.IsStop)
            return GetScrollPositionAtTick(pt.ScrollTick);

        // Negative BPM → reverse scroll direction.
        var effectiveBpm = pt.Bpm * pt.ScrollFactor * pt.ScrollDir;
        var tickAdvance = millisecondsToTicks(time - pt.Time, effectiveBpm);
        return GetScrollPositionAtTick(tickAdvance) + GetScrollPositionAtTick(pt.ScrollTick);
    }

    /// <summary>
    ///     Returns the SCROLL factor active at the given time, or 1.0 if no points exist.
    /// </summary>
    public double GetScrollFactorAtTime(double time)
    {
        if (points.Length == 0)
            return 1.0;

        var idx = findPoint(time);
        return points[idx].ScrollFactor;
    }

    /// <summary>
    ///     Returns the SPEED factor active at the given time, or 1.0 if no points exist.
    /// </summary>
    public double GetSpeedFactorAtTime(double time)
    {
        if (points.Length == 0)
            return 1.0;

        var idx = findPoint(time);
        return points[idx].SpeedFactor;
    }

    /// <summary>
    ///     Returns the BPM value active at the given tick position.
    /// </summary>
    public double GetBpmAtTick(long tick)
    {
        var bpm = initialBpm();

        foreach (var evt in bpmEvents)
        {
            if (evt.Tick > tick)
                break;

            if (evt.Bpm > 0)
                bpm = evt.Bpm;
        }

        return bpm;
    }

    /// <summary>
    ///     Returns the projected osu! time in milliseconds for a native BMS tick,
    ///     accounting for all BPM changes and STOP segments.
    /// </summary>
    public double ProjectTickToTime(long tick) => tickTimeConverter.ProjectTickToTime(tick);

    // ── Build: precompute all timing points ───────────────────────────────────

    private TimingPoint[] buildTimingPoints()
    {
        var result = new List<TimingPoint>();

        var currentTick = 0L;
        var scrollTick = 0.0;
        var currentTime = 0d;
        var firstBpm = bpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Bpm != 0).Bpm;
        var currentBpm = Math.Abs(firstBpm);
        var currentDir = firstBpm < 0 ? -1 : 1;
        var currentScroll = 1.0;
        var currentSpeed = 1.0;

        if (currentBpm <= 0)
            currentBpm = initialBpm();

        var bpmIndex = 0;
        var stopIndex = 0;
        var scrollIndex = 0;
        var speedIndex = 0;

        // All four event streams are ordered; consume their union directly instead of hashing
        // and sorting another copy of the ticks. Coincident events retain the processing order below.
        while (bpmIndex < bpmEvents.Length || stopIndex < stopEvents.Length || scrollIndex < scrollEvents.Length || speedIndex < speedEvents.Length)
        {
            var tick = Math.Min(
                Math.Min(bpmIndex < bpmEvents.Length ? bpmEvents[bpmIndex].Tick : long.MaxValue,
                    stopIndex < stopEvents.Length ? stopEvents[stopIndex].Tick : long.MaxValue),
                Math.Min(scrollIndex < scrollEvents.Length ? scrollEvents[scrollIndex].Tick : long.MaxValue,
                    speedIndex < speedEvents.Length ? speedEvents[speedIndex].Tick : long.MaxValue));
            if (tick > currentTick)
            {
                var duration = ticksToMilliseconds(tick - currentTick, currentBpm);

                if (duration > 0)
                {
                    result.Add(new TimingPoint(currentTime, currentTime + duration,
                        scrollTick, currentBpm, currentScroll, currentSpeed, currentDir, false));
                }

                currentTime += duration;
                scrollTick += (tick - currentTick) * currentScroll * currentDir;
                currentTick = tick;
            }

            // BPM changes at this tick
            while (bpmIndex < bpmEvents.Length && bpmEvents[bpmIndex].Tick == tick)
            {
                var bpm = bpmEvents[bpmIndex++].Bpm;
                if (bpm != 0)
                {
                    currentBpm = Math.Abs(bpm);
                    currentDir = bpm < 0 ? -1 : 1;
                }
            }

            // STOP at this tick
            while (stopIndex < stopEvents.Length && stopEvents[stopIndex].Tick == tick)
            {
                var stop = stopEvents[stopIndex++];

                if (stop.Duration > 0)
                {
                    result.Add(new TimingPoint(currentTime, currentTime + stop.Duration,
                        scrollTick, currentBpm, currentScroll, currentSpeed, currentDir, true));
                    currentTime += stop.Duration;
                }
            }

            // SCROLL change at this tick
            while (scrollIndex < scrollEvents.Length && scrollEvents[scrollIndex].Tick == tick)
                currentScroll = scrollEvents[scrollIndex++].Factor;

            // SPEED change at this tick
            while (speedIndex < speedEvents.Length && speedEvents[speedIndex].Tick == tick)
                currentSpeed = speedEvents[speedIndex++].Factor;
        }

        // Final infinite segment
        result.Add(new TimingPoint(currentTime, double.PositiveInfinity,
            scrollTick, currentBpm, currentScroll, currentSpeed, currentDir, false));

        return result.ToArray();
    }

    // ── Query: find timing point for a given time ─────────────────────────────

    /// <summary>
    ///     Returns the index of the timing point whose interval contains <paramref name="time"/>.
    ///     Uses a walking cursor for nearby playback queries and binary search for distant queries.
    /// </summary>
    private int findPoint(double time)
    {
        if (points.Length == 0)
            return -1;

        // Before the first point: extrapolate from segment 0.
        if (time < points[0].Time)
        {
            cursor = 0;
            return 0;
        }

        // Cursor hit — same segment as last frame.
        var pt = points[cursor];
        if (time >= pt.Time && time < pt.NextTime)
            return cursor;

        // Lifetime refinement and LN endpoint queries share this cursor with playback.
        // Bound the walk so alternating distant times cannot repeatedly scan the timeline.
        for (var steps = 0; steps < 8 && cursor + 1 < points.Length && time >= points[cursor + 1].Time; steps++)
            cursor++;

        // Cursor now points at the right segment (walking succeeded).
        if (cursor > 0 && time < points[cursor].Time)
            cursor--; // overshoot correction (shouldn't happen normally)

        // Verify: if cursor is correct, return.
        var cp = points[cursor];
        if (time >= cp.Time && time < cp.NextTime)
            return cursor;

        return findPointByBinarySearch(time);
    }

    private int findPointByBinarySearch(double time)
    {
        var lo = 0;
        var hi = points.Length - 1;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var p = points[mid];

            if (time < p.Time)
            {
                hi = mid - 1;
                continue;
            }

            if (time >= p.NextTime)
            {
                lo = mid + 1;
                continue;
            }

            cursor = mid;
            return mid;
        }

        // Past the end: use the last segment.
        cursor = points.Length - 1;
        return cursor;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private double initialBpm()
    {
        var initial = bpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Sequence == 0 && e.Bpm != 0).Bpm;
        return initial != 0 ? Math.Abs(initial) : 130;
    }

    private double ticksToMilliseconds(double ticks, double bpm) =>
        ticks * (60000 / bpm) / (TickResolution / 4d);

    private double millisecondsToTicks(double milliseconds, double bpm) =>
        milliseconds * (bpm * (TickResolution / 4d)) / 60000;
}

public readonly record struct BmsMeasureInfo(int Index, long StartTick, long LengthTicks, double LengthRatio);

public readonly record struct BmsBpmEvent(long Tick, double Bpm, double Time, int Sequence = 0);

public readonly record struct BmsStopEvent(long Tick, double Duration, double StopValue, double Bpm, int Sequence);
