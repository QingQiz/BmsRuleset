using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModHardGauge : BmsModGauge
{
    public override string Name => "Hard Gauge";

    public override string Acronym => "H1";

    public override LocalisableString Description => GaugeDescription("Hard");

    public override ModType Type => ModType.DifficultyIncrease;

    public override BmsGaugeType GaugeType => BmsGaugeType.Hard;
}
