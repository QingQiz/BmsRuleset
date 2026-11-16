using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Bms;

/// <summary>Layered BMS gauges in the ruleset's 0..1 health units.</summary>
public static class BmsGaugeHealth
{
    public static int FindClearedLayer(ImmutableArray<BmsGaugeProfile> profiles, ImmutableArray<HealthState> layers)
    {
        for (var i = 0; i < layers.Length; i++)
        {
            if (profiles[i].IsCleared(layers[i]))
                return i;
        }
        return -1;
    }

    public static HealthConfiguration Create(
        ImmutableDictionary<int, double> mines,
        ImmutableArray<BmsGaugeCalculator> calculators,
        ImmutableArray<HealthState> initial)
    {
        return new HealthConfiguration(Combine(initial), context =>
        {
            var judgement = context.Event;
            if (judgement.SuppressPenalty || judgement.Result == BmsResults.MINE_AVOIDED)
                return context.Health;

            if (judgement.Part != NoteParts.EMPTY && judgement.Part != NoteParts.BODY && judgement.Result != BmsResults.MINE_HIT
                && !context.Plan.OpportunitiesByPart.ContainsKey((judgement.NoteId, judgement.Part)))
                return context.Health;

            var type = judgement.Result switch
            {
                var id when id == BmsResults.SUSTAIN_GAIN => BmsResults.GREAT,
                var id when id == BmsResults.SUSTAIN_LOSS => BmsResults.BAD,
                _ => judgement.Result,
            };
            var layers = context.Health.Layers.Select((layer, i) =>
            {
                if (layer.HasFailed) return layer;

                var calculator = calculators[i];
                var delta = judgement.Result == BmsResults.MINE_HIT
                    ? -mines[judgement.NoteId]
                    : calculator.GetDeltaFor(type, layer.Value) * (judgement.Part == NoteParts.BODY ? .5 : 1);
                var value = calculator.ApplyDelta(layer.Value, delta);
                return new HealthState(value, calculator.Profile.IsFailed(value));
            }).ToImmutableArray();
            return Combine(layers);
        }, context => context.Health.HasFailed
            ? SessionOutcome.Failed
            : !context.Completed
                ? SessionOutcome.InProgress
                : context.Health.Layers.Where((layer, i) => calculators[i].Profile.IsCleared(layer)).Any()
                    ? SessionOutcome.Cleared
                    : SessionOutcome.Failed);
    }

    public static HealthState Combine(ImmutableArray<HealthState> layers)
    {
        var active = layers.FirstOrDefault(layer => !layer.HasFailed);
        return new HealthState(active.Value, layers.All(layer => layer.HasFailed)) { Layers = layers };
    }
}
