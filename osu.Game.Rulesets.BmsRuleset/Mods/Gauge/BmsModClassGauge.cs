using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModClassGauge : BmsModGauge
{
    public override string Name => "Class Gauge";

    public override string Acronym => "C1";

    public override LocalisableString Description => GaugeDescription("Class");

    public override ModType Type => ModType.System;

    public override BmsGaugeType GaugeType => BmsGaugeType.Class;
}
