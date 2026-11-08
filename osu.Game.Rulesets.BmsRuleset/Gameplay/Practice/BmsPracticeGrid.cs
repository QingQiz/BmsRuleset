using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public readonly record struct BmsPracticeGridLine(double Time, int Measure, int Subdivision, bool IsMeasureLine);

public sealed class BmsPracticeGrid
{
    public BmsPracticeGridLine[] Lines { get; }

    internal double[] SnapTimes { get; }

    private readonly double[] snapBoundaries;

    private BmsPracticeGrid(BmsPracticeGridLine[] lines, double[] snapBoundaries)
    {
        Lines = lines;
        SnapTimes = lines.Select(line => line.Time).Distinct().Order().ToArray();
        this.snapBoundaries = snapBoundaries;
    }

    public static BmsPracticeGrid Create(BmsTimingMap? timingMap, int beatDivisor = 16)
    {
        validateDivisor(beatDivisor);
        if (timingMap == null || timingMap.TickResolution <= 0 || timingMap.BpmEvents.Count == 0)
            return new BmsPracticeGrid([], []);

        List<BmsPracticeGridLine> lines = [];
        List<double> boundaries = [];
        var previousTick = 0d;
        // BMS resolution is the number of ticks in a normal 4/4 measure, not a quarter note.
        // Keeping the musical interval fixed preserves the note value in short and irregular measures.
        var subdivisionTicks = beatDivisor == 0 ? double.PositiveInfinity : timingMap.TickResolution / (double)beatDivisor;
        foreach (var measure in timingMap.Measures)
        {
            addLine(measure.StartTick, measure.Index, 0, true);
            for (var subdivision = 1; subdivision * subdivisionTicks < measure.LengthTicks; subdivision++)
            {
                var tick = measure.StartTick + subdivision * subdivisionTicks;
                addLine(tick, measure.Index, subdivision, false);
            }
        }

        if (timingMap.Measures.Count > 0)
        {
            var last = timingMap.Measures[^1];
            var end = last.StartTick + last.LengthTicks;
            if (last.LengthTicks > 0)
                addLine(end, last.Index + 1, 0, true);
        }

        return new BmsPracticeGrid([.. lines], [.. boundaries]);

        void addLine(double tick, int measure, int subdivision, bool isMeasureLine)
        {
            if (lines.Count > 0)
            {
                // Project musical midpoints, not time midpoints: BPM changes alter their real-time distance.
                // Keep the original tick tolerance and let a STOP at the midpoint finish before switching sides.
                boundaries.Add(snapBoundary(timingMap, previousTick, tick));
            }

            lines.Add(new BmsPracticeGridLine(projectFractionalTick(timingMap, tick), measure, subdivision, isMeasureLine));
            previousTick = tick;
        }
    }

    private static double snapBoundary(BmsTimingMap map, double lower, double upper)
    {
        var midpoint = lower + (upper - lower + 1e-9) / 2;
        var wholeTick = (long)Math.Floor(midpoint);
        var start = map.ProjectTickToTime(wholeTick);
        var stop = stopDurationAtTick(map, wholeTick);
        var tickDuration = millisecondsPerTick(map, wholeTick);
        var boundary = start + stop + (midpoint - wholeTick) * tickDuration;

        // At very high BPM, a floating-point rounding step can straddle the old inverse
        // calculation's switch. Resolve that once here, including a STOP's final instant.
        while (!choosesEarlier(boundary))
            boundary = Math.BitDecrement(boundary);
        while (choosesEarlier(Math.BitIncrement(boundary)))
            boundary = Math.BitIncrement(boundary);
        return boundary;

        bool choosesEarlier(double time)
        {
            var tick = wholeTick + Math.Clamp((time - start - stop) / tickDuration, 0, 1);
            return tick - lower <= upper - tick + 1e-9;
        }
    }

    /// <summary>Snaps to the nearest musical grid position, choosing the earlier position on a tie.</summary>
    public double Snap(double time)
    {
        if (Lines.Length == 0 || !double.IsFinite(time))
            return time;

        var low = 0;
        var high = snapBoundaries.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (time > snapBoundaries[middle])
                low = middle + 1;
            else
                high = middle;
        }

        return Lines[low].Time;
    }

    private static double projectFractionalTick(BmsTimingMap map, double tick)
    {
        var wholeTick = (long)Math.Floor(tick);
        var time = map.ProjectTickToTime(wholeTick);
        var fraction = tick - wholeTick;
        if (fraction == 0)
            return time;

        // Fractional positions follow the whole tick's STOP; interpolating neighbouring
        // projected times would spread that STOP across the tick.
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
