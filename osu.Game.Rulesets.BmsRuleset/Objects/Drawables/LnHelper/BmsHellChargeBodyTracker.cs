using System;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

internal sealed class BmsHellChargeBodyTracker
{
    public const double DEFAULT_TICK_SCALE = 0.5;
    public const double REPRESS_RECOVERY_PULSE_SCALE = 0.00001;

    private const double tick_interval = 200;

    private double accumulator;
    private bool? lastHolding;

    public void Reset()
    {
        accumulator = 0;
        lastHolding = null;
    }

    public void MarkReleased() => lastHolding = false;

    public void Update(double elapsed, bool holding, Action<bool, double> applyTick)
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
    }
}
