using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public static class BmsJudgementSelector
{
    private static readonly CandidateComparer candidate_comparer = new();

    public static BmsJudgementSelection SelectPress(
        BmsLayoutVariant layout,
        int column,
        IEnumerable<BmsJudgementCandidate> candidates,
        double inputTime,
        BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
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
            var search = new PressSearch(layout, column, inputTime, algorithm);
            for (var i = 0; i < count; i++)
            {
                if (!search.Consider(sortedCandidates[i], i)) break;
            }

            return search.Selection;
        }
        finally
        {
            ArrayPool<BmsJudgementCandidate>.Shared.Return(sortedCandidates);
        }
    }

    // Gameplay already owns ordered notes. Feed them directly, retaining the selected slot
    // instead of copying, sorting and finding the chosen note a second time.
    internal struct PressSearch(BmsLayoutVariant layout, int column, double inputTime, BmsJudgementAlgorithm? algorithm)
    {
        private BmsJudgementCandidate? selected;
        private HitResult selectedResult;
        private BmsJudgementWindowTable? selectedTable;
        private BmsJudgementCandidate? emptyPoorCandidate;
        private int selectedSlot;
        private int emptyPoorSlot;

        internal readonly BmsJudgementSelection Selection => selected != null
            ? new BmsJudgementSelection(selected, selectedResult, false)
            : new BmsJudgementSelection(emptyPoorCandidate, emptyPoorCandidate != null ? HitResult.Miss : HitResult.None, emptyPoorCandidate != null);

        internal readonly int Slot => selected != null ? selectedSlot : emptyPoorCandidate != null ? emptyPoorSlot : -1;

        internal bool Consider(in BmsJudgementCandidate candidate, int slot)
        {
            if (candidate.Column != column)
                return true;

            var offset = inputTime - candidate.StartTime;
            // beatoraja stops its candidate search before the inclusive E-POOR table edge.
            if (offset <= -500)
                return false;

            if (selected is { } current && algorithm == BmsJudgementAlgorithm.Duration
                                        && candidate.StartTime >= inputTime && Math.Abs(current.StartTime - inputTime) <= -offset)
                return false;

            var table = BmsJudgementProfileProvider.GetTable(layout, candidate.Column, candidate.JudgementRate, tail: false);
            var result = candidate.IsJudged ? HitResult.None : table.ResultForOffset(offset);
            if (BmsJudgementProfileProvider.IsPms(layout)
                && (candidate.IsJudged || (candidate.HasMistake && result is not (HitResult.Perfect or HitResult.Great or HitResult.Good))))
                return true;

            if (result != HitResult.None)
            {
                if (selected == null || shouldReplaceSelected(selected.Value, candidate, inputTime, selectedTable!, table, algorithm))
                {
                    selected = candidate;
                    selectedResult = result;
                    selectedTable = table;
                    selectedSlot = slot;
                }

                // Once the current choice cannot be replaced, later notes and empty POOR
                // candidates cannot change the result, even when judgement rates differ.
                if (selectedResult == HitResult.Perfect && selected.Value.StartTime == inputTime)
                    return false;

                return (algorithm ?? BmsJudgementAlgorithm.Combo) switch
                {
                    BmsJudgementAlgorithm.Lowest => false,
                    BmsJudgementAlgorithm.Duration => selected.Value.StartTime < inputTime,
                    BmsJudgementAlgorithm.Combo => selected.Value.StartTime - inputTime < -selectedTable!.SlowWindowFor(HitResult.Good),
                    BmsJudgementAlgorithm.Score => selected.Value.StartTime - inputTime < -selectedTable!.SlowWindowFor(HitResult.Great),
                    _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
                };
            }

            if (table.EPoorWindow?.ContainsOffset(offset) == true
                && (emptyPoorCandidate == null || Math.Abs(offset) < Math.Abs(inputTime - emptyPoorCandidate.Value.StartTime)))
            {
                emptyPoorCandidate = candidate;
                emptyPoorSlot = slot;
            }

            return true;
        }
    }

    private static bool shouldReplaceSelected(
        BmsJudgementCandidate current,
        BmsJudgementCandidate next,
        double inputTime,
        BmsJudgementWindowTable currentTable,
        BmsJudgementWindowTable nextTable,
        BmsJudgementAlgorithm? algorithm)
    {
        // Match beatoraja's JudgeAlgorithm comparisons, including strict late and inclusive early
        // boundaries. Use each note's own table because BMS judgement rates can change mid-chart.
        return (algorithm ?? BmsJudgementAlgorithm.Combo) switch
        {
            BmsJudgementAlgorithm.Combo => isPastWindow(HitResult.Good),
            BmsJudgementAlgorithm.Duration => Math.Abs(current.StartTime - inputTime) > Math.Abs(next.StartTime - inputTime),
            BmsJudgementAlgorithm.Lowest => false,
            BmsJudgementAlgorithm.Score => isPastWindow(HitResult.Great),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, null),
        };

        bool isPastWindow(HitResult threshold) =>
            current.StartTime - inputTime < -currentTable.SlowWindowFor(threshold)
            && next.StartTime - inputTime <= nextTable.FastWindowFor(threshold);
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
