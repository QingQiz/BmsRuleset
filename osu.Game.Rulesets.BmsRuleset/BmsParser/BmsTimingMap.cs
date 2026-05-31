using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public sealed class BmsTimingMap
{

    public int TickResolution { get; }

    public IReadOnlyList<BmsMeasureInfo> Measures { get; }

    public IReadOnlyList<BmsBpmEvent> BpmEvents { get; }

    public IReadOnlyList<BmsStopEvent> StopEvents { get; }

    /// <summary>
    ///     BPM used to express scroll coordinates as millisecond-like values.
    ///     At the chart's initial valid BPM, native tick-scroll matches the old time-based projection scale.
    /// </summary>
    public double ScrollReferenceBpm { get; }

    private readonly record struct ScrollSegment(double StartTime, double EndTime, double StartTick, double Bpm, bool IsStop);

    private readonly IReadOnlyList<ScrollSegment> scrollSegments;

    public BmsTimingMap(int tickResolution, IEnumerable<BmsMeasureInfo> measures, IEnumerable<BmsBpmEvent> bpmEvents, IEnumerable<BmsStopEvent> stopEvents)
    {
        TickResolution = tickResolution;
        Measures = measures.OrderBy(m => m.Index).ToArray();
        BpmEvents = bpmEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        StopEvents = stopEvents.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToArray();
        ScrollReferenceBpm = initialBpm();
        scrollSegments = buildScrollSegments();
    }

    /// <summary>
    ///     Returns the native BMS scroll coordinate for a tick position.
    ///     Equal tick distances produce equal visual distances; BPM changes affect how fast this
    ///     coordinate advances over real time, while STOP segments keep it frozen.
    /// </summary>
    public double GetScrollPositionAtTick(double tick) => ticksToMilliseconds(tick, ScrollReferenceBpm);

    /// <summary>
    ///     Returns the native BMS scroll coordinate reached at a projected osu! time.
    /// </summary>
    public double GetScrollPositionAtTime(double time)
    {
        if (scrollSegments.Count == 0)
            return time;

        if (time < scrollSegments[0].StartTime)
        {
            var initialBpm = BpmEvents.FirstOrDefault(e => e.Bpm > 0).Bpm;

            if (initialBpm <= 0)
                initialBpm = ScrollReferenceBpm;

            return GetScrollPositionAtTick(millisecondsToTicks(time - scrollSegments[0].StartTime, initialBpm) + scrollSegments[0].StartTick);
        }

        var low = 0;
        var high = scrollSegments.Count - 1;

        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var segment = scrollSegments[middle];

            if (time < segment.StartTime)
            {
                high = middle - 1;
                continue;
            }

            if (time >= segment.EndTime)
            {
                low = middle + 1;
                continue;
            }

            return segment.IsStop
                ? GetScrollPositionAtTick(segment.StartTick)
                : GetScrollPositionAtTick(segment.StartTick + millisecondsToTicks(time - segment.StartTime, segment.Bpm));
        }

        var last = scrollSegments[^1];
        return last.IsStop
            ? GetScrollPositionAtTick(last.StartTick)
            : GetScrollPositionAtTick(last.StartTick + millisecondsToTicks(time - last.StartTime, last.Bpm));
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

    private IReadOnlyList<ScrollSegment> buildScrollSegments()
    {
        var result = new List<ScrollSegment>();
        var eventTicks = BpmEvents.Select(e => e.Tick).Concat(StopEvents.Select(e => e.Tick)).Distinct().OrderBy(t => t).ToArray();
        var currentTick = 0L;
        var currentTime = 0d;
        var currentBpm = BpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Bpm > 0).Bpm;

        if (currentBpm <= 0)
            currentBpm = ScrollReferenceBpm;

        var bpmIndex = 0;
        var stopIndex = 0;

        foreach (var tick in eventTicks)
        {
            if (tick > currentTick)
            {
                var duration = ticksToMilliseconds(tick - currentTick, currentBpm);

                if (duration > 0)
                    result.Add(new ScrollSegment(currentTime, currentTime + duration, currentTick, currentBpm, false));

                currentTime += duration;
                currentTick = tick;
            }

            while (bpmIndex < BpmEvents.Count && BpmEvents[bpmIndex].Tick == tick)
            {
                var bpm = BpmEvents[bpmIndex++].Bpm;

                if (bpm > 0)
                    currentBpm = bpm;
            }

            while (stopIndex < StopEvents.Count && StopEvents[stopIndex].Tick == tick)
            {
                var stop = StopEvents[stopIndex++];

                if (stop.Duration > 0)
                {
                    result.Add(new ScrollSegment(currentTime, currentTime + stop.Duration, currentTick, currentBpm, true));
                    currentTime += stop.Duration;
                }
            }
        }

        result.Add(new ScrollSegment(currentTime, double.PositiveInfinity, currentTick, currentBpm, false));
        return result;
    }

    private double initialBpm()
    {
        var initial = BpmEvents.FirstOrDefault(e => e.Tick == 0 && e.Sequence == 0 && e.Bpm > 0).Bpm;
        return initial > 0 ? initial : 130;
    }

    private double ticksToMilliseconds(double ticks, double bpm) =>
        ticks * (60000 / bpm) / (TickResolution / 4d);

    private double millisecondsToTicks(double milliseconds, double bpm) =>
        milliseconds * (bpm * (TickResolution / 4d)) / 60000;
}

public readonly record struct BmsMeasureInfo(int Index, long StartTick, long LengthTicks, double LengthRatio);

public readonly record struct BmsBpmEvent(long Tick, double Bpm, double Time, int Sequence = 0);

public readonly record struct BmsStopEvent(long Tick, double Duration, double StopValue, double Bpm, int Sequence);
