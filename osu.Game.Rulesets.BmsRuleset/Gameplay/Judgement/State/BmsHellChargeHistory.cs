using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

internal sealed class BmsHellChargeHistory
{
    private readonly List<Segment> segments = [];

    internal readonly record struct Segment(double Time, double Accumulator, int Direction);

    public void Clear() => segments.Clear();

    internal void Add(Segment segment) => segments.Add(segment);

    internal Segment Rewind(double time)
    {
        var low = 0;
        var high = segments.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (segments[middle].Time <= time) low = middle + 1;
            else high = middle;
        }

        segments.RemoveRange(low, segments.Count - low);
        return low > 0 ? segments[low - 1] : new Segment(time, 0, 0);
    }
}
