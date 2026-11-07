using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

public sealed record BmsGaugeProfile(
    BmsGaugeAlgorithm Algorithm,
    double InitialHealth,
    double MaxHealth,
    double ClearThreshold,
    double PerfectGain,
    double GreatGain,
    double GoodGain,
    double BadDelta,
    double PoorDelta,
    double EmptyPoorDelta,
    BmsGaugeDisplayProfile Display,
    IReadOnlyList<BmsGaugeGutsRule> GutsRules
)
{
    public Func<int, double>? DefaultTotal { get; init; }

    public Func<double, int, double>? DamageScale { get; init; }

    public double FailureThreshold { get; init; }

    public bool IsFailed(double health) => health <= 0 || health < FailureThreshold;

    public double MinHealth => Algorithm == BmsGaugeAlgorithm.Total ? 0.02 : 0;
}
