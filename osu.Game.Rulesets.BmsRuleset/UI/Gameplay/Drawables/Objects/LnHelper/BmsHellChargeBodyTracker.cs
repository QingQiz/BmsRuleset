using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

internal sealed class BmsHellChargeBodyTracker
{
    public const double DEFAULT_TICK_SCALE = 0.5;

    private const double tick_interval = 200;

    private double accumulator;
    private BmsHellChargeHistory? history;
    private bool historyEnabled = true;

    public bool HistoryEnabled
    {
        get => historyEnabled;
        set
        {
            if (historyEnabled == value)
                return;

            historyEnabled = value;
            if (!value)
                history = null;
        }
    }

    public void Reset()
    {
        accumulator = 0;
        if (HistoryEnabled)
            history?.Clear();
        else
            history = null;
    }

    public void MarkReleased(double? time = null)
    {
        // Changing direction does not reset the signed HCN accumulator.
    }

    public void End(double time)
    {
        if (accumulator == 0)
            return;

        accumulator = 0;
        if (HistoryEnabled)
            (history ??= new BmsHellChargeHistory()).Add(time, accumulator);
    }

    public void Rewind(double time)
    {
        if (HistoryEnabled)
            accumulator = history?.Rewind(time) ?? 0;
    }

    public void Update(double elapsed, bool holding, Action<bool, double> applyTick, double? time = null)
    {
        if (elapsed <= 0)
            return;

        accumulator += holding ? elapsed : -elapsed;

        // beatoraja applies at most one tick per update, retaining any backlog for later frames.
        if (holding && accumulator > tick_interval)
        {
            applyTick(true, DEFAULT_TICK_SCALE);
            accumulator -= tick_interval;
        }

        else if (!holding && accumulator < -tick_interval)
        {
            applyTick(false, DEFAULT_TICK_SCALE);
            accumulator += tick_interval;
        }

        if (HistoryEnabled && time is { } end)
        {
            // Frame boundaries matter when a long update leaves a backlog. Restore the last
            // applied frame rather than interpolating ticks that never occurred.
            (history ??= new BmsHellChargeHistory()).Add(end, accumulator);
        }
    }
}
