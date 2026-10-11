using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;

public class BmsModHazardGauge : BmsModGauge
{
    public override string Name => "Hazard Gauge";

    public override string Acronym => "H3";

    public override LocalisableString Description => GaugeDescription("Hazard");

    public override ModType Type => ModType.DifficultyIncrease;

    public override BmsGaugeType GaugeType => BmsGaugeType.Hazard;
}
