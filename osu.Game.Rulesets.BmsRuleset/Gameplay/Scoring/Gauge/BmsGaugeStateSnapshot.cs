namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

public sealed record BmsGaugeStateSnapshot(
    BmsGaugeType GaugeType,
    double Health,
    bool Failed);
