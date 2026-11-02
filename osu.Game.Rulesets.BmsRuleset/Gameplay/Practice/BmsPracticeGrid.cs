using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public readonly record struct BmsPracticeGridLine(double Time, int Measure, int Subdivision, bool IsMeasureLine);

public static class BmsPracticeGrid
{
    public static BmsPracticeGridLine[] Create(BmsTimingMap? timingMap, int beatDivisor = 16)
    {
        validateDivisor(beatDivisor);
        if (timingMap == null || timingMap.TickResolution <= 0 || timingMap.BpmEvents.Count == 0)
            return [];

        List<BmsPracticeGridLine> lines = [];
        // BMS resolution is the number of ticks in a normal 4/4 measure, not a quarter note.
        // Keeping the musical interval fixed preserves the note value in short and irregular measures.
        var subdivisionTicks = beatDivisor == 0 ? double.PositiveInfinity : timingMap.TickResolution / (double)beatDivisor;
        foreach (var measure in timingMap.Measures)
        {
            lines.Add(new BmsPracticeGridLine(timingMap.ProjectTickToTime(measure.StartTick), measure.Index, 0, true));
            for (var subdivision = 1; subdivision * subdivisionTicks < measure.LengthTicks; subdivision++)
            {
                var tick = measure.StartTick + subdivision * subdivisionTicks;
                lines.Add(new BmsPracticeGridLine(projectFractionalTick(timingMap, tick), measure.Index, subdivision, false));
            }
        }

        if (timingMap.Measures.Count > 0)
        {
            var last = timingMap.Measures[^1];
            var end = last.StartTick + last.LengthTicks;
            if (last.LengthTicks > 0)
                lines.Add(new BmsPracticeGridLine(timingMap.ProjectTickToTime(end), last.Index + 1, 0, true));
        }
        return [.. lines];
    }

    /// <summary>Snaps to the nearest musical grid position, choosing the earlier position on a tie.</summary>
    public static double Snap(BmsTimingMap? timingMap, double time, int beatDivisor = 16)
    {
        validateDivisor(beatDivisor);
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
        return projectFractionalTick(timingMap, snappedTick);
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

    private static double projectFractionalTick(BmsTimingMap map, double tick)
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

    private static void validateDivisor(int beatDivisor)
    {
        if (beatDivisor is not (0 or 4 or 8 or 12 or 16 or 24 or 32 or 48 or 64))
        {
            throw new ArgumentOutOfRangeException(nameof(beatDivisor));
        }
    }
}
