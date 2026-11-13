using System;
using System.Collections.Immutable;
using System.Linq;
using RhythmEngine;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

internal static class BmsEngineGauge
{
    internal static HealthConfiguration CreateDefault(BmsHitObject[] objects)
    {
        var beatmap = objects.FirstOrDefault()?.Beatmap;
        var family = beatmap == null ? BmsGaugeProfileFamily.SevenKeys : BmsGaugeProfileFamilyProvider.FromLayout(beatmap.LayoutVariant);
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal, family);
        var count = objects.Sum(n => n is BmsLandmine ? 0 : n is BmsLongNote { EffectiveMode: not BmsLongNoteMode.LongNote } ? 2 : 1);
        var calculator = new BmsGaugeCalculator(profile, beatmap?.Total, count, family);
        return Create(objects, [calculator], [new HealthState(profile.InitialHealth, false)]);
    }

    internal static HealthConfiguration Create(BmsHitObject[] objects, ImmutableArray<BmsGaugeCalculator> calculators,
                                               ImmutableArray<HealthState> initial)
    {
        var mines = objects.Select((n, id) => (n, id)).Where(p => p.n is BmsLandmine)
            .ToImmutableDictionary(p => p.id, p => ((BmsLandmine)p.n).LandmineDamagePercent / 100.0);
        var deferredHeads = objects.Select((n, id) => (n, id)).Where(p => p.n is BmsLongNote { EffectiveMode: BmsLongNoteMode.LongNote })
            .Select(p => p.id).ToImmutableHashSet();
        return new HealthConfiguration(combine(initial), context =>
        {
            var judgement = context.Event;
            if (judgement.SuppressPenalty || judgement.Result == "mine-avoided"
                || (judgement.Part == "head" && deferredHeads.Contains(judgement.NoteId)))
                return context.Health;
            var type = judgement.Result switch
            {
                "sustain-gain" => HitResult.Great,
                "sustain-loss" => HitResult.Ok,
                _ => BmsEngineChart.HitResultFor(judgement.Result),
            };
            var layers = context.Health.Layers.Select((layer, i) =>
            {
                if (layer.HasFailed) return layer;
                var calculator = calculators[i];
                var delta = judgement.Result == "mine-hit" ? -mines[judgement.NoteId]
                    : calculator.GetDeltaFor(type, layer.Value) * (judgement.Part == "body" ? .5 : 1);
                var value = calculator.ApplyDelta(layer.Value, delta);
                return new HealthState(value, calculator.Profile.IsFailed(value));
            }).ToImmutableArray();
            return combine(layers);
        }, context => context.Health.HasFailed ? SessionOutcome.Failed : !context.Completed ? SessionOutcome.InProgress
            : context.Health.Layers.Where((layer, i) => !layer.HasFailed && layer.Value >= calculators[i].Profile.ClearThreshold).Any()
                ? SessionOutcome.Cleared : SessionOutcome.Failed);
    }

    private static HealthState combine(ImmutableArray<HealthState> layers)
    {
        var active = layers.FirstOrDefault(layer => !layer.HasFailed);
        return new HealthState(active.Value, layers.All(layer => layer.HasFailed)) { Layers = layers };
    }
}
