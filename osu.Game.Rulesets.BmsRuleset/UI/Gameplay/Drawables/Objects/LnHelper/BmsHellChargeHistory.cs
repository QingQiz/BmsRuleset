using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

internal sealed class BmsHellChargeHistory
{
    private readonly List<Frame> frames = [];
    private readonly List<FrameRun> runs = [];

    private readonly record struct Frame(double Time, double Accumulator);

    private readonly record struct FrameRun(Frame Start, double TimeStep, double AccumulatorStep, int Count)
    {
        public Frame At(int index) => new(Start.Time + TimeStep * index, Start.Accumulator + AccumulatorStep * index);
        public Frame Last => At(Count - 1);
    }

    public void Clear()
    {
        frames.Clear();
        runs.Clear();
    }

    public void Add(double time, double accumulator)
    {
        var frame = new Frame(time, accumulator);
        if (runs.Count > 0 && (frames.Count == 0 || runs[^1].Last.Time > frames[^1].Time))
        {
            var run = runs[^1];
            if (run.At(run.Count) == frame)
            {
                runs[^1] = run with { Count = run.Count + 1 };
                return;
            }
        }

        frames.Add(frame);
        if (frames.Count < 3 || runs.Count > 0 && frames[^3].Time <= runs[^1].Last.Time)
            return;

        var first = frames[^3];
        var second = frames[^2];
        var candidate = new FrameRun(first, second.Time - first.Time, second.Accumulator - first.Accumulator, 3);
        // Only replace snapshots that can be reconstructed exactly, including floating-point
        // rounding. Jitter, direction changes and tick/backlog boundaries stay lossless.
        if (candidate.TimeStep > 0 && candidate.At(1) == second && candidate.Last == frame)
        {
            runs.Add(candidate);
            frames.RemoveRange(frames.Count - 3, 3);
        }
    }

    public double Rewind(double time)
    {
        var low = 0;
        var high = frames.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (frames[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }
        frames.RemoveRange(low, frames.Count - low);

        low = 0;
        high = runs.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (runs[middle].Start.Time <= time)
                low = middle + 1;
            else
                high = middle;
        }
        runs.RemoveRange(low, runs.Count - low);

        if (runs.Count > 0)
        {
            var run = runs[^1];
            low = 0;
            high = run.Count;
            // A seek between frames restores the last applied frame, never an interpolated
            // accumulator. Binary search also avoids rounding a near-boundary seek upward.
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (run.At(middle).Time <= time)
                    low = middle + 1;
                else
                    high = middle;
            }
            runs[^1] = run with { Count = low };
        }

        if (frames.Count > 0 && (runs.Count == 0 || frames[^1].Time >= runs[^1].Last.Time))
            return frames[^1].Accumulator;

        return runs.Count > 0 ? runs[^1].Last.Accumulator : 0;
    }
}
