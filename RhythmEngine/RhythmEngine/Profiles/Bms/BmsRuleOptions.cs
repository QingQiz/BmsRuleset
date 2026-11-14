using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Bms;

/// <summary>Normalized per-object policy after chart and mod configuration.</summary>
public sealed record BmsRuleOptions(
    bool RetainResolvedHeads = true,
    bool ImmediateBadHead = false,
    bool ShareLateBad = false,
    bool NonConsumingBad = false,
    long? HeadDeadline = null,
    long? TailDeadline = null,
    long ReleaseGrace = 0,
    TimerOrder MineOrder = TimerOrder.AfterInputState,
    TimerOrder PassiveOrder = TimerOrder.BeforeInput);

public static partial class BmsProfiles
{
    public static ImmutableArray<InputCandidate> SelectCandidates(InputArbitrationContext context, Chart chart, BmsSelection selection)
    {
        var candidates = context.Candidates;
        var active = candidates.Where(c => c.Priority == 0 && !c.Shared).OrderBy(c => chart.Notes[c.NoteIndex].EndTime).FirstOrDefault();
        if (candidates.Any(c => c.Priority == 0 && !c.Shared))
            return [active, .. candidates.Where(c => c.Shared && chart.Notes[c.NoteIndex].Rule is HoldRule)];

        var selected = selectCandidate(candidates, selection, chart);
        return selected is { } target ? [.. candidates.Where(c => c.Shared), target] : [.. candidates.Where(c => c.Shared)];
    }

    public static ScoreConfiguration CreateScore(bool emptyPoorBreaksCombo = false) => new(new ScoreState(), (state, context) =>
    {
        var result = context.Event;
        if (result.SuppressPenalty)
            return state with { AccuracyMaximum = state.AccuracyMaximum + (context.Opportunity?.MaximumAccuracy ?? 0) };

        if (context.Opportunity != null || result.Part == NoteParts.EMPTY || result.Result == BmsResults.MINE_HIT)
            state = state with { Counters = state.Counters.SetItem(result.Result.Value, state.Counters.GetValueOrDefault(result.Result.Value) + 1) };
        if (result.Result == BmsResults.MINE_HIT)
            return state with { Combo = 0 };
        if (result.Part == NoteParts.EMPTY)
            return state with { Combo = emptyPoorBreaksCombo || result.Result == BmsResults.BAD ? 0 : state.Combo };

        return ProfileSupport.Accumulate(state, context, values);
    }, plan => plan.MaximumAccuracy);
}
