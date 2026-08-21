using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

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
);
