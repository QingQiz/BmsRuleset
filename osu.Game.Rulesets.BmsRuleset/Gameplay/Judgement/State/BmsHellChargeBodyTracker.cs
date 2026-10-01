using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

internal sealed class BmsHellChargeBodyTracker
{
    public const double DEFAULT_TICK_SCALE = 0.5;

    private const double tick_interval = 200;

    private double accumulator;
    private BmsHellChargeHistory? history;

    public bool HistoryEnabled
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            if (!value)
                history = null;
        }
    } = true;

    public void Reset()
    {
        accumulator = 0;
        if (HistoryEnabled)
            history?.Clear();
        else
            history = null;
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

        var tick = accumulate(elapsed, holding);
        if (tick)
            applyTick(holding, DEFAULT_TICK_SCALE);
        commit(holding, tick, time);
    }

    internal void Update(double elapsed, bool holding, BmsLongNote note, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (elapsed <= 0)
            return;

        var tick = accumulate(elapsed, holding);
        if (tick)
            execution.ApplyBodyTick(note, holding, DEFAULT_TICK_SCALE, frame);
        commit(holding, tick, frame.SimulationTime);
    }

    private bool accumulate(double elapsed, bool holding)
    {
        accumulator += holding ? elapsed : -elapsed;
        // One update releases at most one tick, leaving a large frame's backlog for later updates.
        return (holding && accumulator > tick_interval) || (!holding && accumulator < -tick_interval);
    }

    private void commit(bool holding, bool tick, double? time)
    {
        // Feedback observes the pre-commit accumulator; history records only completed frames.
        if (tick)
            accumulator += holding ? -tick_interval : tick_interval;
        if (HistoryEnabled && time is { } end)
            (history ??= new BmsHellChargeHistory()).Add(end, accumulator);
    }
}
