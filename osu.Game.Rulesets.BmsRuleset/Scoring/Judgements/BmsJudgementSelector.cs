using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public static class BmsJudgementSelector
{
    private static readonly CandidateComparer candidate_comparer = new();

    public static BmsJudgementSelection SelectPress(
        BmsLayoutVariant layout,
        int column,
        IEnumerable<BmsJudgementCandidate> candidates,
        double inputTime)
    {
        var initialCapacity = candidates.TryGetNonEnumeratedCount(out var candidateCount)
            ? Math.Max(1, candidateCount)
            : 16;
        var sortedCandidates = ArrayPool<BmsJudgementCandidate>.Shared.Rent(initialCapacity);
        var count = 0;

        try
        {
            foreach (var candidate in candidates)
            {
                if (count == sortedCandidates.Length)
                {
                    var expanded = ArrayPool<BmsJudgementCandidate>.Shared.Rent(sortedCandidates.Length * 2);
                    Array.Copy(sortedCandidates, expanded, count);
                    ArrayPool<BmsJudgementCandidate>.Shared.Return(sortedCandidates);
                    sortedCandidates = expanded;
                }

                sortedCandidates[count++] = candidate;
            }

            Array.Sort(sortedCandidates, 0, count, candidate_comparer);
            return selectSorted(layout, column, sortedCandidates, count, inputTime);
        }
        finally
        {
            ArrayPool<BmsJudgementCandidate>.Shared.Return(sortedCandidates);
        }
    }

    private static BmsJudgementSelection selectSorted(
        BmsLayoutVariant layout,
        int column,
        BmsJudgementCandidate[] candidates,
        int count,
        double inputTime)
    {
        BmsJudgementCandidate? selected = null;
        HitResult selectedResult = HitResult.None;
        BmsJudgementCandidate? emptyPoorCandidate = null;

        for (var i = 0; i < count; i++)
        {
            var candidate = candidates[i];
            var table = BmsJudgementProfileProvider.GetTable(layout, candidate.Column, candidate.JudgementRate, tail: false);
            var offset = inputTime - candidate.StartTime;
            var result = table.ResultForOffset(offset);

            if (result != HitResult.None)
            {
                if (selected == null || shouldReplaceSelected(selected.Value, candidate, inputTime, selectedResult, result, table.GoodFastDTime))
                {
                    selected = candidate;
                    selectedResult = result;
                }

                continue;
            }

            if (candidate.Column == column && table.IsEmptyPoorOffset(offset))
                emptyPoorCandidate ??= candidate;
        }

        if (selected != null)
            return new BmsJudgementSelection(selected, selectedResult, false);

        return emptyPoorCandidate != null
            ? new BmsJudgementSelection(emptyPoorCandidate, HitResult.Miss, true)
            : new BmsJudgementSelection(null, HitResult.None, false);
    }

    private static bool shouldReplaceSelected(
        BmsJudgementCandidate current,
        BmsJudgementCandidate next,
        double inputTime,
        HitResult currentResult,
        HitResult nextResult,
        double goodFastDTime)
    {
        var nextOffset = inputTime - next.StartTime;
        var currentDTime = current.StartTime - inputTime;

        if (currentDTime < -goodFastDTime && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        if (currentResult is HitResult.Ok or HitResult.Meh && nextResult is HitResult.Perfect or HitResult.Great or HitResult.Good)
            return true;

        return currentResult is HitResult.Ok or HitResult.Meh
               && nextResult is HitResult.Ok or HitResult.Meh
               && System.Math.Abs(nextOffset) < System.Math.Abs(inputTime - current.StartTime);
    }

    private sealed class CandidateComparer : IComparer<BmsJudgementCandidate>
    {
        public int Compare(BmsJudgementCandidate x, BmsJudgementCandidate y)
        {
            var startTimeComparison = x.StartTime.CompareTo(y.StartTime);
            return startTimeComparison != 0 ? startTimeComparison : x.Column.CompareTo(y.Column);
        }
    }
}
