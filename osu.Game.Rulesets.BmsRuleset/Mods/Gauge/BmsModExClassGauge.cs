using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModExClassGauge : BmsModGauge
{
    public override string Name => "EX Class Gauge";

    public override string Acronym => "C2";

    public override LocalisableString Description => GaugeDescription("EX Class");

    public override ModType Type => ModType.System;

    public override BmsGaugeType GaugeType => BmsGaugeType.ExClass;
}
