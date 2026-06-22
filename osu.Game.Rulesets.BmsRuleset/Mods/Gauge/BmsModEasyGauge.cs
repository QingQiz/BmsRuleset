using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModEasyGauge : BmsModGauge
{
    public override string Name => "Easy Gauge";

    public override string Acronym => "E1";

    public override LocalisableString Description => GaugeDescription("Easy");

    public override ModType Type => ModType.DifficultyReduction;

    public override BmsGaugeType GaugeType => BmsGaugeType.Easy;
}
