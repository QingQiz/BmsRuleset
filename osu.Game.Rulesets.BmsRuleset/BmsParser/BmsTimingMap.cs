using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed class BmsTimingMap
{

    public int TickResolution { get; }

    public IReadOnlyList<BmsMeasureInfo> Measures { get; }

    public IReadOnlyList<BmsBpmEvent> BpmEvents { get; }

    public IReadOnlyList<BmsStopEvent> StopEvents { get; }

    public IReadOnlyList<BmsScrollEvent> ScrollEvents { get; }

    public IReadOnlyList<BmsSpeedEvent> SpeedEvents { get; }

    /// <summary>
    ///     BPM used to express scroll coordinates as millisecond-like values.
    ///     At the chart's initial valid BPM, native tick-scroll matches the old time-based projection scale.
    /// </summary>
    public double ScrollReferenceBpm { get; }

    // ── Precomputed timing point array (build once, query with walking cursor) ──

    private readonly struct TimingPoint(
        double time,
        double nextTime,
        double scrollPos,
        double bpm,
        double scrollFactor,
        double speedFactor,
        int scrollDir,
        bool isStop)
    {
        public readonly double Time = time;                 // segment start (ms)
        public readonly double NextTime = nextTime;         // segment end (ms), double.PositiveInfinity for last
        public readonly double ScrollPos = scrollPos;       // precomputed scroll position at Time
        public readonly double Bpm = bpm;                   // active |BPM| for this segment (always positive)
        public readonly double ScrollFactor = scrollFactor; // active SCROLL factor
        public readonly double SpeedFactor = speedFactor;   // active SPEED factor
        public readonly int ScrollDir = scrollDir;          // 1 = normal, -1 = reverse (negative BPM)
        public readonly bool IsStop = isStop;               // true → scroll position frozen during this segment
    }

    private readonly TimingPoint[] points;

    // ── Tick→time infrastructure (only used during parsing, not gameplay) ──

    private readonly double[] cumulativeStopDurations;
    private int cursor;

    // ── Construction ──────────────────────────────────────────────────────────

    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                        IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                        IEnumerable<BmsScrollEvent> scrollEvents, IEnumerable<BmsSpeedEvent> speedEvents,
                        double baseBpm = 0)
    {
        TickResolution = tickResolution;
        Measures = measures.OrderBy(m => m.Index).ToArray();
        BpmEvents = bpmEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        StopEvents = stopEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        ScrollEvents = scrollEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        SpeedEvents = speedEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        ScrollReferenceBpm = baseBpm > 0 ? baseBpm : initialBpm();
        points = buildTimingPoints();
        cumulativeStopDurations = buildCumulativeStops();
    }

    // Backward-compatible overload for tests and fallback paths that don't have scroll/speed events.
    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures,
                        IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents,
                        double baseBpm = 0)
        : this(tickResolution, measures, bpmEvents, stopEvents, [], [], baseBpm)
    {
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
        if (ScrollEvents.Count == 0)
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

            if (scrollIdx >= ScrollEvents.Count)
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
            return pt.ScrollPos;

        // Negative BPM → reverse scroll direction.
        var effectiveBpm = pt.Bpm * pt.ScrollFactor * pt.ScrollDir;
        var tickAdvance = millisecondsToTicks(time - pt.Time, effectiveBpm);
        return GetScrollPositionAtTick(tickAdvance) + pt.ScrollPos;
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
        var bpm = ScrollReferenceBpm;

        foreach (var evt in BpmEvents)
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
    public double ProjectTickToTime(long tick)
    {
        var bpmEvent = BpmEvents[findLastBpmIndex(tick)];

        var firstStop = findFirstStopIndex(bpmEvent.Tick);
        var pastStop = findFirstStopIndex(tick);

        var stopOffset = 0d;

        if (firstStop < pastStop)
        {
            stopOffset = cumulativeStopDurations[pastStop - 1];

            if (firstStop > 0)
                stopOffset -= cumulativeStopDurations[firstStop - 1];
        }

        return bpmEvent.Time + ticksToMilliseconds(tick - bpmEvent.Tick, Math.Abs(bpmEvent.Bpm)) + stopOffset;
    }

    // ── Build: precompute all timing points ───────────────────────────────────

    private TimingPoint[] buildTimingPoints()
    {
        // Collect all change ticks: BPM, STOP, SCROLL, and SPEED.
        var eventTicks = BpmEvents.Select(e => e.Tick)
            .Concat(StopEvents.Select(e => e.Tick))
            .Concat(ScrollEvents.Select(e => e.Tick))
            .Concat(SpeedEvents.Select(e => e.Tick))
            .Distinct().OrderBy(t => t).ToArray();

        var result = new List<TimingPoint>();

        var currentTick = 0L;
        var scrollTick = 0.0;
        var currentTime = 0d;
        var firstBpm = BpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Bpm != 0).Bpm;
        var currentBpm = Math.Abs(firstBpm);
        var currentDir = firstBpm < 0 ? -1 : 1;
        var currentScroll = 1.0;
        var currentSpeed = 1.0;

        if (currentBpm <= 0)
            currentBpm = ScrollReferenceBpm;

        var bpmIndex = 0;
        var stopIndex = 0;
        var scrollIndex = 0;
        var speedIndex = 0;

        foreach (var tick in eventTicks)
        {
            if (tick > currentTick)
            {
                var duration = ticksToMilliseconds(tick - currentTick, currentBpm);

                if (duration > 0)
                {
                    var scrollPos = GetScrollPositionAtTick(scrollTick);
                    result.Add(new TimingPoint(currentTime, currentTime + duration,
                        scrollPos, currentBpm, currentScroll, currentSpeed, currentDir, false));
                }

                currentTime += duration;
                scrollTick += (tick - currentTick) * currentScroll * currentDir;
                currentTick = tick;
            }

            // BPM changes at this tick
            while (bpmIndex < BpmEvents.Count && BpmEvents[bpmIndex].Tick == tick)
            {
                var bpm = BpmEvents[bpmIndex++].Bpm;
                if (bpm != 0)
                {
                    currentBpm = Math.Abs(bpm);
                    currentDir = bpm < 0 ? -1 : 1;
                }
            }

            // STOP at this tick
            while (stopIndex < StopEvents.Count && StopEvents[stopIndex].Tick == tick)
            {
                var stop = StopEvents[stopIndex++];

                if (stop.Duration > 0)
                {
                    var scrollPos = GetScrollPositionAtTick(scrollTick);
                    result.Add(new TimingPoint(currentTime, currentTime + stop.Duration,
                        scrollPos, currentBpm, currentScroll, currentSpeed, currentDir, true));
                    currentTime += stop.Duration;
                }
            }

            // SCROLL change at this tick
            while (scrollIndex < ScrollEvents.Count && ScrollEvents[scrollIndex].Tick == tick)
                currentScroll = ScrollEvents[scrollIndex++].Factor;

            // SPEED change at this tick
            while (speedIndex < SpeedEvents.Count && SpeedEvents[speedIndex].Tick == tick)
                currentSpeed = SpeedEvents[speedIndex++].Factor;
        }

        // Final infinite segment
        result.Add(new TimingPoint(currentTime, double.PositiveInfinity,
            GetScrollPositionAtTick(scrollTick), currentBpm, currentScroll, currentSpeed, currentDir, false));

        return result.ToArray();
    }

    // ── Query: find timing point for a given time ─────────────────────────────

    /// <summary>
    ///     Returns the index of the timing point whose interval contains <paramref name="time"/>.
    ///     Uses a walking cursor for O(1) amortised queries during forward playback;
    ///     falls back to binary search on seek (time jumps backwards).
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

        // Walk forward (normal playback: time advances monotonically).
        while (cursor + 1 < points.Length && time >= points[cursor + 1].Time)
            cursor++;

        // Cursor now points at the right segment (walking succeeded).
        if (cursor > 0 && time < points[cursor].Time)
            cursor--; // overshoot correction (shouldn't happen normally)

        // Verify: if cursor is correct, return.
        var cp = points[cursor];
        if (time >= cp.Time && time < cp.NextTime)
            return cursor;

        // Seek: time jumped backwards — binary search.
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

    private double[] buildCumulativeStops()
    {
        var prefix = new double[StopEvents.Count];
        double cumulative = 0;

        for (var i = 0; i < StopEvents.Count; i++)
        {
            cumulative += StopEvents[i].Duration;
            prefix[i] = cumulative;
        }

        return prefix;
    }

    private int findLastBpmIndex(long tick)
    {
        var lo = 0;
        var hi = BpmEvents.Count - 1;
        var result = 0;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;

            if (BpmEvents[mid].Tick <= tick)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    private int findFirstStopIndex(long tick)
    {
        var lo = 0;
        var hi = StopEvents.Count - 1;
        var result = StopEvents.Count;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;

            if (StopEvents[mid].Tick >= tick)
            {
                result = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return result;
    }

    private double initialBpm()
    {
        var initial = BpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Sequence == 0 && e.Bpm != 0).Bpm;
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
