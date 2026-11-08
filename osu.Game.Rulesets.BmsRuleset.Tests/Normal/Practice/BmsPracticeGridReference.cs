#nullable enable

using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

// The former inverse projection verifies the precomputed query against existing musical snapping.
internal static class BmsPracticeGridReference
{
    /// <summary>Snaps to the nearest musical grid position, choosing the earlier position on a tie.</summary>
    public static double Snap(BmsTimingMap? timingMap, double time, int beatDivisor = 16)
    {
        if (timingMap == null || timingMap.TickResolution <= 0 || timingMap.BpmEvents.Count == 0
            || timingMap.Measures.Count == 0 || !double.IsFinite(time))
            return time;

        var first = timingMap.Measures[0];
        var last = timingMap.Measures[^1];
        var endTick = last.StartTick + last.LengthTicks;
        var tick = projectTimeToTick(timingMap, time, first.StartTick, endTick);
        var low = 0;
        var high = timingMap.Measures.Count - 1;
        var measureIndex = 0;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (timingMap.Measures[middle].StartTick <= tick)
            {
                measureIndex = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }

        var measure = timingMap.Measures[measureIndex];
        var measureEnd = measure.StartTick + measure.LengthTicks;
        double lower = measure.StartTick;
        double upper = measureEnd;
        if (beatDivisor > 0)
        {
            var subdivisionTicks = timingMap.TickResolution / (double)beatDivisor;
            lower += Math.Floor((tick - measure.StartTick) / subdivisionTicks) * subdivisionTicks;
            upper = Math.Min(measureEnd, lower + subdivisionTicks);
        }

        // Millisecond distance would favour the fast side of a BPM change or advance across a STOP.
        var snappedTick = tick - lower <= upper - tick + 1e-9 ? lower : upper;
        return ProjectFractionalTick(timingMap, snappedTick);
    }

    private static double projectTimeToTick(BmsTimingMap map, double time, long startTick, long endTick)
    {
        if (time <= map.ProjectTickToTime(startTick))
            return startTick;
        if (time >= map.ProjectTickToTime(endTick))
            return endTick;

        var low = startTick;
        var high = endTick;
        var wholeTick = startTick;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (map.ProjectTickToTime(middle) <= time)
            {
                wholeTick = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }

        var elapsed = time - map.ProjectTickToTime(wholeTick) - stopDurationAtTick(map, wholeTick);
        var fraction = Math.Clamp(elapsed / millisecondsPerTick(map, wholeTick), 0, 1);
        return wholeTick + fraction;
    }

    internal static double ProjectFractionalTick(BmsTimingMap map, double tick)
    {
        var wholeTick = (long)Math.Floor(tick);
        var time = map.ProjectTickToTime(wholeTick);
        var fraction = tick - wholeTick;
        if (fraction == 0)
            return time;

        // Native maps normally have a resolution divisible by the divisor. Fractional fallback maps still
        // need exact beat spacing: interpolating neighbouring projected times would smear a STOP across a tick.
        return time + fraction * millisecondsPerTick(map, wholeTick) + stopDurationAtTick(map, wholeTick);
    }

    private static double millisecondsPerTick(BmsTimingMap map, long tick)
    {
        var bpmIndex = 0;
        var low = 0;
        var high = map.BpmEvents.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (map.BpmEvents[middle].Tick <= tick)
            {
                bpmIndex = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }

        var bpm = Math.Abs(map.BpmEvents[bpmIndex].Bpm);
        return (60000 / bpm) / (map.TickResolution / 4d);
    }

    private static double stopDurationAtTick(BmsTimingMap map, long tick)
    {
        var low = 0;
        var high = map.StopEvents.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (map.StopEvents[middle].Tick < tick)
                low = middle + 1;
            else
                high = middle;
        }

        var duration = 0d;
        for (var i = low; i < map.StopEvents.Count && map.StopEvents[i].Tick == tick; i++)
            duration += map.StopEvents[i].Duration;
        return duration;
    }

}
