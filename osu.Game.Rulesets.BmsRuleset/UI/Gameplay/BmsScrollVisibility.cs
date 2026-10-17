using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsScrollVisibility
{
    private readonly double[] times;
    private readonly double[] minimum;
    private readonly double[] maximum;
    private readonly int leafCount = 1;

    internal BmsScrollVisibility(BmsBeatmap beatmap, BmsTimingMap timingMap)
    {
        times = timelineTimes(beatmap, timingMap).Where(double.IsFinite).Distinct().Order().ToArray();
        while (leafCount < times.Length)
            leafCount *= 2;

        minimum = new double[leafCount * 2];
        maximum = new double[leafCount * 2];
        Array.Fill(minimum, double.PositiveInfinity);
        Array.Fill(maximum, double.NegativeInfinity);
        for (var i = 0; i < times.Length; i++)
            minimum[leafCount + i] = maximum[leafCount + i] = timingMap.GetScrollPositionAtTime(times[i]);
        for (var i = leafCount - 1; i > 0; i--)
        {
            minimum[i] = Math.Min(minimum[i * 2], minimum[i * 2 + 1]);
            maximum[i] = Math.Max(maximum[i * 2], maximum[i * 2 + 1]);
        }
    }

    internal double GetEndTime(double time, double scrollPosition, double coordinateScale)
    {
        if (coordinateScale == 0 || !double.IsFinite(coordinateScale))
            return double.PositiveInfinity;

        var first = Array.BinarySearch(times, time);
        if (first < 0)
            first = ~first;

        // beatoraja finishes the first timeline above the top edge, then stops. Keeping
        // that row also preserves a long note whose head is outside but body crosses the lane.
        var index = findFirstOutside(1, 0, leafCount, first, scrollPosition + 1 / coordinateScale, coordinateScale > 0);
        return index < 0 ? double.PositiveInfinity : times[index];
    }

    private int findFirstOutside(int node, int left, int right, int first, double limit, bool forward)
    {
        if (right <= first || (forward ? maximum[node] <= limit : minimum[node] >= limit))
            return -1;
        if (right - left == 1)
            return left < times.Length ? left : -1;

        var middle = (left + right) / 2;
        var found = findFirstOutside(node * 2, left, middle, first, limit, forward);
        return found >= 0 ? found : findFirstOutside(node * 2 + 1, middle, right, first, limit, forward);
    }

    private static IEnumerable<double> timelineTimes(BmsBeatmap beatmap, BmsTimingMap map)
    {
        foreach (var note in beatmap.HitObjects)
        {
            yield return note.StartTime;

            if (note is BmsLongNote longNote)
                yield return longNote.EndTime;
        }

        foreach (var note in beatmap.InvisibleNotes)
            yield return note.StartTime;
        foreach (var measure in map.Measures)
            yield return map.ProjectTickToTime(measure.StartTick);

        var bpm = map.ScrollReferenceBpm;
        foreach (var change in map.BpmEvents.GroupBy(e => e.Tick).Select(g => g.Last()))
        {
            if (change.Bpm == bpm)
                continue;

            bpm = change.Bpm;
            yield return change.Time;
        }

        foreach (var stop in map.StopEvents)
        {
            if (stop.Duration > 0)
                yield return map.ProjectTickToTime(stop.Tick);
        }

        var scroll = 1d;
        foreach (var change in map.ScrollEvents.GroupBy(e => e.Tick).Select(g => g.Last()))
        {
            if (change.Factor == scroll)
                continue;

            scroll = change.Factor;
            yield return map.ProjectTickToTime(change.Tick);
        }

        foreach (var speed in map.SpeedEvents)
            yield return map.ProjectTickToTime(speed.Tick);
    }
}
