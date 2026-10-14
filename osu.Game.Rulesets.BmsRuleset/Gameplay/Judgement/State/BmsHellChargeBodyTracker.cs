using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

internal sealed class BmsHellChargeBodyTracker
{
    public const double DEFAULT_TICK_SCALE = 0.5;
    private const double tick_interval = 200;

    private double time = double.NaN;
    private double accumulator;
    private int ticks;
    private BmsHellChargeHistory.Segment segment;
    private BmsHellChargeHistory? history;

    public bool HistoryEnabled
    {
        get;
        set
        {
            field = value;
            if (!value) history = null;
        }
    } = true;

    public void Reset(double startTime = double.NaN)
    {
        time = startTime;
        accumulator = 0;
        ticks = 0;
        segment = new(startTime, 0, 0);
        history?.Clear();
    }

    public void End(double endTime)
    {
        if (time == endTime && segment.Direction == 0) return;

        time = endTime;
        accumulator = 0;
        ticks = 0;
        setSegment(new(endTime, 0, 0));
    }

    public void Rewind(double rewindTime)
    {
        if (!HistoryEnabled) return;

        segment = history?.Rewind(rewindTime) ?? new(rewindTime, 0, 0);
        time = rewindTime;
        ticks = countTicks(time);
        accumulator = valueAt(time);
    }

    internal double NextTickTime(bool holding)
    {
        if (double.IsNaN(time)) return double.PositiveInfinity;

        var direction = holding ? 1 : -1;
        return Math.BitIncrement(segment.Direction == direction
            ? threshold(ticks)
            : time + tick_interval - direction * accumulator);
    }

    public void Update(double elapsed, bool holding, Action<bool, double> applyTick, double? endTime = null)
    {
        if (elapsed <= 0) return;

        var end = endTime ?? (double.IsNaN(time) ? elapsed : time + elapsed);
        if (double.IsNaN(time)) Reset(end - elapsed);
        prepare(holding);
        var count = countTicks(end);
        while (ticks < count)
        {
            ticks++;
            applyTick(holding, DEFAULT_TICK_SCALE);
        }

        time = end;
        accumulator = valueAt(end);
    }

    internal void Update(double endTime, bool holding, BmsLongNote note, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (endTime < time || double.IsNaN(time)) return;

        prepare(holding);
        var count = countTicks(endTime);
        while (ticks < count)
        {
            var tickTime = Math.BitIncrement(threshold(ticks));
            ticks++;
            execution.ApplyBodyTick(note, holding, DEFAULT_TICK_SCALE, frame with { SimulationTime = tickTime });
        }

        time = endTime;
        accumulator = valueAt(endTime);
    }

    private void prepare(bool holding)
    {
        var direction = holding ? 1 : -1;
        if (segment.Direction == direction) return;

        ticks = 0;
        setSegment(new(time, accumulator, direction));
    }

    private void setSegment(BmsHellChargeHistory.Segment next)
    {
        segment = next;
        // Only input direction changes and the body ending are needed to reconstruct a seek.
        if (HistoryEnabled) (history ??= new()).Add(next);
    }

    private double threshold(int index) => segment.Time + tick_interval * (index + 1) - segment.Direction * segment.Accumulator;

    private int countTicks(double end)
    {
        if (segment.Direction == 0) return 0;

        var count = Math.Max(0, (int)Math.Ceiling((end - threshold(0)) / tick_interval));
        if (end > threshold(count)) count++;
        else if (count > 0 && end <= threshold(count - 1)) count--;
        return count;
    }

    private double valueAt(double end) => segment.Accumulator + segment.Direction * (end - segment.Time - ticks * tick_interval);
}
