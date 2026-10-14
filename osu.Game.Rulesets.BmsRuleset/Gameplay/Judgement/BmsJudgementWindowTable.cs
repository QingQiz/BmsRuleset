using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public sealed class BmsJudgementWindowTable
{
    private readonly double? passivePoorOffset;
    private readonly BmsJudgementWindow[] hitWindows;

    public IReadOnlyList<BmsJudgementWindow> HitWindows => hitWindows;

    public BmsJudgementWindow? EPoorWindow { get; }

    public BmsJudgementWindowTable(IEnumerable<BmsJudgementWindow> rows)
    {
        var allRows = rows as BmsJudgementWindow[] ?? rows.ToArray();
        var hitCount = 0;
        BmsJudgementWindow? epoor = null;

        foreach (var row in allRows)
        {
            if (row.Result == HitResult.Miss)
                epoor ??= row;
            else
                hitCount++;
        }

        var hits = new BmsJudgementWindow[hitCount];
        var hitIndex = 0;

        foreach (var row in allRows)
        {
            if (row.Result != HitResult.Miss)
            {
                hits[hitIndex++] = row;

                if (row.Result == HitResult.Ok)
                    passivePoorOffset ??= row.SlowOffset;
            }
        }

        hitWindows = hits;
        EPoorWindow = epoor;
    }

    internal static double TimeAfter(double origin, double offset)
    {
        var lower = origin + offset;
        if (lower - origin > offset) return lower;

        var upper = Math.BitIncrement(origin + Math.BitIncrement(offset));
        while (upper - origin <= offset) upper = Math.BitIncrement(upper);
        // Cancellation near zero can span many representable timestamps. Bisect rather than
        // walking every float, retaining the rule's subtraction and strict comparison.
        while (true)
        {
            var middle = lower + (upper - lower) / 2;
            if (middle == lower || middle == upper) return upper;

            if (middle - origin > offset) upper = middle;
            else lower = middle;
        }
    }

    public HitResult ResultForOffset(double timeOffset)
    {
        foreach (var row in hitWindows)
        {
            if (row.ContainsOffset(timeOffset))
                return row.Result;
        }

        return HitResult.None;
    }

    public bool IsEmptyPoorOffset(double timeOffset)
    {
        if (EPoorWindow == null || !EPoorWindow.Value.ContainsOffset(timeOffset))
            return false;

        return ResultForOffset(timeOffset) == HitResult.None;
    }

    public bool IsPastPassivePoorOffset(double timeOffset) =>
        timeOffset > (passivePoorOffset ?? throw new InvalidOperationException("Sequence contains no matching element"));

    public double FrameworkWindowFor(HitResult result)
    {
        var row = rowFor(result);

        if (row == null)
            return 0;

        return Math.Min(Math.Abs(row.Value.SlowOffset), Math.Abs(row.Value.FastOffset));
    }

    public double SlowWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.SlowOffset ?? 0;
    }

    public double FastWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.FastOffset ?? 0;
    }

    private BmsJudgementWindow? rowFor(HitResult result)
    {
        if (result == HitResult.Miss)
            return EPoorWindow;

        foreach (var row in hitWindows)
        {
            if (row.Result == result)
                return row;
        }

        return null;
    }
}
