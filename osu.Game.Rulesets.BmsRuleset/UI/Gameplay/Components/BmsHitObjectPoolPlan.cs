using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal static class BmsHitObjectPoolPlan
{
    // A long note has head/body/tail skin trees. This is a relative prewarm budget, not a byte limit.
    internal const int PREWARM_BUDGET = 65536;
    private const int max_pool_size = 8192;
    private static readonly int[] minimum_sizes = [64, 32, 32];
    private static readonly int[] weights = [1, 3, 1];

    internal readonly record struct ColumnSizes(int Notes, int LongNotes, int Mines);

    internal static int[] CreateHitExplosionSizes(IEnumerable<BmsHitObject> hitObjects, int columns)
    {
        const int prewarm_budget = 8192;
        var times = new List<double>[columns];
        for (var i = 0; i < columns; i++)
            times[i] = [];
        foreach (var note in hitObjects)
        {
            if (note is BmsNote && note.Column >= 0 && note.Column < columns)
                times[note.Column].Add(note.StartTime);
        }

        var sizes = new int[columns];
        for (var column = 0; column < columns; column++)
        {
            var starts = times[column];
            starts.Sort();
            var first = 0;
            sizes[column] = 2;
            for (var last = 0; last < starts.Count; last++)
            {
                // Preserve every pulse; this only moves predictable skin construction into loading.
                // Include one deferred visual update in the 200 ms fade's overlap estimate.
                while (starts[last] - starts[first] > 200 + BmsColumnHitObjectContainer.MAX_DEFERRED_UPDATE_TIME)
                    first++;
                sizes[column] = Math.Max(sizes[column], last - first + 1);
            }
        }

        var total = sizes.Sum();
        if (total > prewarm_budget)
        {
            for (var i = 0; i < columns; i++)
                sizes[i] = 2 + (int)((long)(sizes[i] - 2) * Math.Max(0, prewarm_budget - columns * 2) / (total - columns * 2));
        }
        return sizes;
    }

    internal static ColumnSizes[] Create(IEnumerable<BmsHitObject> hitObjects, int columns)
    {
        var intervals = new List<(double Time, int Delta)>[columns * 3];
        for (var i = 0; i < intervals.Length; i++)
            intervals[i] = [];

        // Estimate ordinary scroll residence and retain the entire LN duration. Actual visibility
        // can be much longer under STOP/reverse scroll or custom speeds; pools may grow on demand.
        var lead = BmsGameplayScrollController.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED);
        foreach (var hitObject in hitObjects)
        {
            if (hitObject.Column < 0 || hitObject.Column >= columns)
                continue;

            var kind = hitObject switch { BmsLongNote => 1, BmsLandmine => 2, _ => 0 };
            var bucket = intervals[hitObject.Column * 3 + kind];
            var end = hitObject is BmsLongNote ln ? ln.EndTime : hitObject.StartTime;
            bucket.Add((hitObject.StartTime - (kind == 2 ? 100 : lead), 1));
            bucket.Add((end + (kind == 2 ? BmsHitObjectLifetimeEntry.MINE_PAST_LIFETIME : 400), -1));
        }

        var sizes = new int[intervals.Length];
        var minimumCost = 0;
        var extraCost = 0;
        for (var i = 0; i < intervals.Length; i++)
        {
            // Allocate before releasing at equal times, covering simultaneous boundary events.
            intervals[i].Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : b.Delta.CompareTo(a.Delta));
            var alive = 0;
            var peak = minimum_sizes[i % 3];
            foreach (var point in intervals[i])
            {
                alive += point.Delta;
                peak = Math.Max(peak, alive);
            }

            sizes[i] = Math.Min(max_pool_size, peak);
            minimumCost += minimum_sizes[i % 3] * weights[i % 3];
            extraCost += (sizes[i] - minimum_sizes[i % 3]) * weights[i % 3];
        }

        var available = Math.Max(0, PREWARM_BUDGET - minimumCost);
        if (extraCost > available)
        {
            for (var i = 0; i < sizes.Length; i++)
            {
                var minimum = minimum_sizes[i % 3];
                sizes[i] = minimum + (int)((long)(sizes[i] - minimum) * available / extraCost);
            }
        }

        return Enumerable.Range(0, columns).Select(c => new ColumnSizes(sizes[c * 3], sizes[c * 3 + 1], sizes[c * 3 + 2])).ToArray();
    }
}
