using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public sealed record BmsGaugeHistoryEvent(
    double Time,
    BmsGaugeType ActiveGaugeType,
    IReadOnlyList<BmsGaugeStateSnapshot> States);

public sealed record BmsGaugeStateSnapshot(
    BmsGaugeType GaugeType,
    double Health,
    bool Failed);
