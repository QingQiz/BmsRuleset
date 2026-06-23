using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public sealed class BmsJudgementWindowTable
{
    private readonly IReadOnlyList<BmsJudgementWindow> hitWindows;
    private readonly BmsJudgementWindow? missWindow;

    public BmsJudgementWindowTable(IEnumerable<BmsJudgementWindow> rows)
    {
        var allRows = rows.ToArray();
        hitWindows = allRows.Where(row => row.Result != HitResult.Miss).ToArray();
        var missRows = allRows.Where(row => row.Result == HitResult.Miss).ToArray();
        missWindow = missRows.Length == 0 ? null : missRows[0];
    }

    public double GoodEarlyDTime => hitWindows.First(row => row.Result == HitResult.Good).EarlyDTime;

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
        if (missWindow == null || !missWindow.Value.ContainsOffset(timeOffset))
            return false;

        return ResultForOffset(timeOffset) == HitResult.None;
    }

    public bool IsPastPassivePoorOffset(double timeOffset)
    {
        var badWindow = hitWindows.First(row => row.Result == HitResult.Ok);
        return timeOffset > badWindow.LateOffset;
    }

    public double FrameworkWindowFor(HitResult result)
    {
        var row = rowFor(result);

        if (row == null)
            return 0;

        return Math.Min(Math.Abs(row.Value.LateOffset), Math.Abs(row.Value.EarlyOffset));
    }

    public double LateWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.LateOffset ?? 0;
    }

    public double EarlyWindowFor(HitResult result)
    {
        var row = rowFor(result);
        return row?.EarlyOffset ?? 0;
    }

    private BmsJudgementWindow? rowFor(HitResult result)
    {
        if (result == HitResult.Miss)
            return missWindow;

        foreach (var row in hitWindows)
        {
            if (row.Result == result)
                return row;
        }

        return null;
    }
}
