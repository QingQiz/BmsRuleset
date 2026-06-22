using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModExHardGauge : BmsModGauge
{
    public override string Name => "EX Hard Gauge";

    public override string Acronym => "H2";

    public override LocalisableString Description => GaugeDescription("EX Hard");

    public override ModType Type => ModType.DifficultyIncrease;

    public override BmsGaugeType GaugeType => BmsGaugeType.ExHard;
}
