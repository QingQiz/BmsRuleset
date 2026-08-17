using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModExHardClassGauge : BmsModGauge
{
    public override string Name => "EX Hard Class Gauge";

    public override string Acronym => "C3";

    public override LocalisableString Description => GaugeDescription("EX Hard Class");

    public override ModType Type => ModType.System;

    public override BmsGaugeType GaugeType => BmsGaugeType.ExHardClass;
}
