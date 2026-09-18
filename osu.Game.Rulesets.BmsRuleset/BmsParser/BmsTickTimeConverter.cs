using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

// Import needs note times without building gameplay scroll segments. Both paths share this
// projection so coincident BPM/STOP events and lead-in offsets keep identical semantics.
internal readonly struct BmsTickTimeConverter(
    int tickResolution,
    IReadOnlyList<BmsBpmEvent> bpmEvents,
    IReadOnlyList<BmsStopEvent> stopEvents)
{
    private readonly double[] cumulativeStopDurations = buildCumulativeStops(stopEvents);

    // Events must already be ordered by tick, then source sequence, with STOP offsets applied to BPM times.

    public double ProjectTickToTime(long tick)
    {
        var bpmEvent = bpmEvents[findLastBpmIndex(tick)];

        var firstStop = findFirstStopIndex(bpmEvent.Tick);
        var pastStop = findFirstStopIndex(tick);

        var stopOffset = 0d;

        if (firstStop < pastStop)
        {
            stopOffset = cumulativeStopDurations[pastStop - 1];

            if (firstStop > 0)
                stopOffset -= cumulativeStopDurations[firstStop - 1];
        }

        return bpmEvent.Time + (tick - bpmEvent.Tick) * (60000 / Math.Abs(bpmEvent.Bpm)) / (tickResolution / 4d) + stopOffset;
    }

    private static double[] buildCumulativeStops(IReadOnlyList<BmsStopEvent> stopEvents)
    {
        var prefix = new double[stopEvents.Count];
        double cumulative = 0;

        for (var i = 0; i < stopEvents.Count; i++)
        {
            cumulative += stopEvents[i].Duration;
            prefix[i] = cumulative;
        }

        return prefix;
    }

    private int findLastBpmIndex(long tick)
    {
        var lo = 0;
        var hi = bpmEvents.Count - 1;
        var result = 0;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;

            if (bpmEvents[mid].Tick <= tick)
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
        var hi = stopEvents.Count - 1;
        var result = stopEvents.Count;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;

            if (stopEvents[mid].Tick >= tick)
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
}
