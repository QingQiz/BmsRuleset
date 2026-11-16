using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

/// <summary>Interprets historical BMS hit events when the original input replay is unavailable.</summary>
internal static class BmsLegacyGaugeHistory
{
    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    internal static IReadOnlyList<GameplayHealthHistoryEvent> Reconstruct(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<HitEvent> events, BmsGameplayProfile profile)
    {
        if (events.Count == 0)
            return [];
        var explicitType = score.Mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType;
        var types = score.Mods.Any(mod => mod is BmsModAutoGauge)
            ? explicitType is BmsGaugeType.Class or BmsGaugeType.ExClass or BmsGaugeType.ExHardClass
                ? BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN : BmsModAutoGauge.AUTO_GAUGE_CHAIN
            : [explicitType ?? BmsGaugeType.Normal];
        var family = beatmap is BmsBeatmap bms ? BmsGaugeProfileFamilyProvider.FromLayout(bms.LayoutVariant) : BmsGaugeProfileFamily.SevenKeys;
        var noteCount = Math.Max(1, beatmap.HitObjects.Count(note => note is not BmsLandmine));
        var calculators = types.Select(type => new BmsGaugeCalculator(profile.CreateGaugeProfile(type, family),
            (beatmap as BmsBeatmap)?.Total, noteCount, family)).ToArray();
        var states = calculators.Select(calculator => new HealthState(calculator.Profile.InitialHealth, false)).ToArray();
        var history = new List<GameplayHealthHistoryEvent> { snapshot(0) };

        foreach (var result in events)
        {
            for (var i = 0; i < states.Length; i++)
            {
                if (states[i].HasFailed)
                    continue;
                var value = states[i].Value;
                if (result.HitObject is BmsLandmine mine)
                {
                    if (result.Result == HitResult.Meh)
                        value = mine.LandmineDamagePercent >= max_landmine_damage_percent ? 0 : Math.Max(0, value - mine.LandmineDamagePercent / 100);
                }
                else
                    value = calculators[i].ApplyDelta(value, calculators[i].GetDeltaFor(BmsGameplayProfile.ResultIdFor(result.Result), value));
                states[i] = new HealthState(value, value <= 0);
            }
            history.Add(snapshot(result.HitObject.GetEndTime()));
        }
        return history;

        GameplayHealthHistoryEvent snapshot(double time)
        {
            var selected = explicitType is { } type ? Array.IndexOf(types, type) :
                BmsGaugeHealth.FindClearedLayer([.. calculators.Select(calculator => calculator.Profile)], [.. states]);
            return new GameplayHealthHistoryEvent(time, BmsGaugeHealth.Combine([.. states]),
                [.. types.Select((type, index) => new HealthHistoryLayer(BmsGaugeProfileFactory.NameFor(type),
                    BmsGaugeProfileFactory.HistoryColourFor(type), states[index].Value / calculators[index].Profile.MaxHealth,
                    states[index].HasFailed, calculators[index].Profile.IsCleared(states[index])))], selected >= 0 ? selected : types.Length - 1);
        }
    }
}
