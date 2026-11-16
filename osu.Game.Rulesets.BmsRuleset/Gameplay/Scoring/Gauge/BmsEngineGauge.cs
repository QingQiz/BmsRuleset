using System.Collections.Immutable;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

internal static class BmsEngineGauge
{
    internal static HealthConfiguration CreateDefault(BmsHitObject[] objects, ScorePlan plan, BmsGameplayProfile gameplayProfile)
    {
        var beatmap = objects.FirstOrDefault()?.Beatmap;
        var family = beatmap == null ? BmsGaugeProfileFamily.SevenKeys : BmsGaugeProfileFamilyProvider.FromLayout(beatmap.LayoutVariant);
        var profile = gameplayProfile.CreateGaugeProfile(BmsGaugeType.Normal, family);
        var count = plan.Opportunities.Length;
        var calculator = new BmsGaugeCalculator(profile, beatmap?.Total, count, family,
            (beatmap as BmsBeatmap)?.PracticeFullScoringOpportunities);
        return Create(objects, [calculator], [new HealthState(profile.InitialHealth, false)]);
    }

    internal static HealthConfiguration Create(BmsHitObject[] objects, ImmutableArray<BmsGaugeCalculator> calculators,
                                               ImmutableArray<HealthState> initial)
    {
        var mines = objects.Select((n, id) => (n, id)).Where(p => p.n is BmsLandmine)
            .ToImmutableDictionary(p => p.id, p => ((BmsLandmine)p.n).LandmineDamagePercent / 100.0);
        return BmsGaugeHealth.Create(mines, calculators, initial);
    }
}
