using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

internal sealed class BmsHellChargeBodyTracker
{
    public const double DEFAULT_TICK_SCALE = 0.5;
    public const double REPRESS_RECOVERY_PULSE_SCALE = 0.00001;

    private const double tick_interval = 200;

    private double accumulator;
    private bool? lastHolding;
    private readonly List<HoldSegment> history = [];

    private readonly record struct HoldSegment(double Start, double End, double Accumulator, bool? Holding);

    public void Reset()
    {
        accumulator = 0;
        lastHolding = null;
        history.Clear();
    }

    public void MarkReleased(double? time = null)
    {
        if (time is { } eventTime)
            history.Add(new HoldSegment(eventTime, eventTime, accumulator, false));
        lastHolding = false;
    }

    public void Rewind(double time)
    {
        while (history.Count > 0 && history[^1].Start > time)
        {
            history.RemoveAt(history.Count - 1);
        }

        if (history.Count == 0)
        {
            accumulator = 0;
            lastHolding = null;
            return;
        }

        var segment = history[^1];
        var end = Math.Min(time, segment.End);
        accumulator = segment.Accumulator + (segment.Holding == true ? 1 : -1) * (end - segment.Start);
        // Keep exactly +/-200 pending, matching the strict tick threshold in forward playback.
        if (accumulator > tick_interval)
            accumulator -= Math.Ceiling((accumulator - tick_interval) / tick_interval) * tick_interval;
        else if (accumulator < -tick_interval)
            accumulator += Math.Ceiling((-accumulator - tick_interval) / tick_interval) * tick_interval;
        lastHolding = segment.Holding;
        history[^1] = segment with { End = end };
    }

    public void Update(double elapsed, bool holding, Action<bool, double> applyTick, double? time = null)
    {
        if (elapsed <= 0)
            return;

        if (lastHolding == false && holding)
            applyTick(true, REPRESS_RECOVERY_PULSE_SCALE);

        lastHolding = holding;
        accumulator += holding ? elapsed : -elapsed;

        while (accumulator > tick_interval)
        {
            applyTick(true, DEFAULT_TICK_SCALE);
            accumulator -= tick_interval;
        }

        while (accumulator < -tick_interval)
        {
            applyTick(false, DEFAULT_TICK_SCALE);
            accumulator += tick_interval;
        }

        if (time is { } end)
        {
            // Store changes at application time: a future release must not replace the hold
            // state in the preceding frame. Steady holding needs only one interval.
            if (history.Count > 0 && history[^1].Holding == holding && Math.Abs(history[^1].End - (end - elapsed)) < 0.001)
                history[^1] = history[^1] with { End = end };
            else
                history.Add(new HoldSegment(end, end, accumulator, holding));
        }
    }
}
