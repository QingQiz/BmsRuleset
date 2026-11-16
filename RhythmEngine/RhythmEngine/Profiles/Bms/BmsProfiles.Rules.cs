using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Bms;

public static partial class BmsProfiles
{
    public static AccuracyGrades Grades { get; } = new(8.0 / 9, 7.0 / 9, 6.0 / 9, 5.0 / 9);

    public static ScoreGrade GradeFor(double accuracy, IReadOnlyDictionary<ResultId, int> results)
        => accuracy >= 1.0 - 1e-9 && new[] { BmsResults.GREAT, BmsResults.GOOD, BmsResults.BAD, BmsResults.POOR }.All(result => results.GetValueOrDefault(result) == 0)
            ? ScoreGrade.Perfect : Grades.GradeFor(accuracy, allowPerfect: false);

    public static Func<SessionCompletionContext, bool> CreateCompletion(KeyChart source, IEnumerable<long> passiveDeadlines)
    {
        // Results can finish before the remaining empty-input windows retire; the host historically ends after the last slow judgement window.
        var deadline = Math.Max(source.EndTime, source.Notes.Select(note => note.EndTime).DefaultIfEmpty(0).Max() + passiveDeadlines.DefaultIfEmpty(0).Max());
        return context => context.Time >= deadline && context.Score.JudgedOpportunities == context.Plan.Opportunities.Length;
    }

    public static ImmutableArray<InputCandidate> SelectCandidates(InputArbitrationContext context, Chart chart, BmsSelection selection)
    {
        var candidates = context.Candidates;
        var active = candidates.Where(c => c.Priority == 0 && !c.Shared).OrderBy(c => chart.Notes[c.NoteIndex].EndTime).FirstOrDefault();
        if (candidates.Any(c => c.Priority == 0 && !c.Shared))
            return [active, .. candidates.Where(c => c.Shared && chart.Notes[c.NoteIndex].Rule is HoldRule)];

        var selected = selectCandidate(candidates, selection, chart);
        return selected is { } target ? [.. candidates.Where(c => c.Shared), target] : [.. candidates.Where(c => c.Shared)];
    }

    public static ResultValue GetResultValue(ResultId result) => values[result];

    public static ScoreConfiguration CreateScore(bool emptyPoorBreaksCombo = false) => new(new ScoreState(), (state, context) =>
    {
        var result = context.Event;
        if (result.SuppressPenalty)
            return state with
            {
                AccuracyMaximum = state.AccuracyMaximum + (context.Opportunity is { } opportunity ? opportunity.MaximumAccuracy * opportunity.Weight : 0),
                JudgedOpportunities = state.JudgedOpportunities + (context.Opportunity == null ? 0 : 1),
            };

        if (result.Part == NoteParts.EMPTY || result.Result == BmsResults.MINE_HIT)
            state = state with { ResultCounts = state.ResultCounts.SetItem(result.Result, state.ResultCounts.GetValueOrDefault(result.Result) + 1) };
        if (result.Result == BmsResults.MINE_HIT)
            return state with { Combo = 0 };
        if (result.Part == NoteParts.EMPTY)
            return state with { Combo = emptyPoorBreaksCombo || result.Result == BmsResults.BAD ? 0 : state.Combo };

        return ProfileSupport.Accumulate(state, context, values);
    }, plan => plan.MaximumAccuracy);
}
