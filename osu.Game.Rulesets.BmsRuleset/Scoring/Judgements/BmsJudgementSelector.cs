using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public static class BmsJudgementSelector
{
    public static BmsJudgementSelection SelectPress(
        BmsLayoutVariant layout,
        int column,
        IEnumerable<BmsJudgementCandidate> candidates,
        double inputTime)
    {
        BmsJudgementCandidate? selected = null;
        HitResult selectedResult = HitResult.None;
        var hasEmptyPoor = false;

        foreach (var candidate in candidates.OrderBy(c => c.StartTime).ThenBy(c => c.Column))
        {
            var table = BmsJudgementProfileProvider.GetTable(layout, candidate.Column, candidate.JudgementRate, tail: false);
            var offset = inputTime - candidate.StartTime;
            var result = table.ResultForOffset(offset);

            if (result != HitResult.None)
            {
                if (selected == null || shouldReplaceSelected(selected.Value, candidate, inputTime, layout, selectedResult, result))
                {
                    selected = candidate;
                    selectedResult = result;
                }

                continue;
            }

            if (candidate.Column == column && table.IsEmptyPoorOffset(offset))
                hasEmptyPoor = true;
        }

        if (selected != null)
            return new BmsJudgementSelection(selected, selectedResult, false);

        return hasEmptyPoor
            ? new BmsJudgementSelection(null, HitResult.Miss, true)
            : new BmsJudgementSelection(null, HitResult.None, false);
    }

    private static bool shouldReplaceSelected(
        BmsJudgementCandidate current,
        BmsJudgementCandidate next,
        double inputTime,
        BmsLayoutVariant layout,
        HitResult currentResult,
        HitResult nextResult)
    {
        var table = BmsJudgementProfileProvider.GetTable(layout, next.Column, next.JudgementRate, tail: false);
        var nextOffset = inputTime - next.StartTime;
        var currentDTime = current.StartTime - inputTime;
        var goodEarlyDTime = table.GoodEarlyDTime;

        if (currentDTime < -goodEarlyDTime && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        if (currentResult is HitResult.Ok or HitResult.Meh && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        return currentResult is HitResult.Ok or HitResult.Meh
               && nextResult is HitResult.Ok or HitResult.Meh
               && System.Math.Abs(nextOffset) < System.Math.Abs(inputTime - current.StartTime);
    }
}
