using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

internal static class BmsTestHealth
{
    internal static GameplayHealthHistoryEvent History(double time, IReadOnlyList<BmsGaugeStateSnapshot> states)
    {
        var model = new BmsGaugeController(new BindableDouble(), BmsGameplayProfile.INSTANCE);
        model.SetGaugeTypes(states.Select(state => state.GaugeType), initialStates: states);
        return model.CaptureHistory(time, false);
    }

    internal static HealthState ToState(IReadOnlyList<BmsGaugeStateSnapshot> states)
        => BmsGaugeHealth.Combine([.. states.Select(state => new HealthState(state.Health, state.Failed))]);

    internal static IReadOnlyList<BmsGaugeStateSnapshot> CourseGauges(this BmsCourseSession session)
        => session.CurrentHealth is { } health ? session.GetGaugeStates(health) : [];

    internal static BmsGaugeController Gauge(this BmsHealthProcessor health) => (BmsGaugeController)health.Model;

    internal static bool EvaluateTestClear(this BmsHealthProcessor health)
    {
        // Boundary tests supply a final gauge seed; create the same configuration that owns runtime outcome evaluation.
        health.CreateEngineHealth(new BmsEngineChart(new BmsBeatmap()));
        return health.HasPassedAtEnd();
    }
}
