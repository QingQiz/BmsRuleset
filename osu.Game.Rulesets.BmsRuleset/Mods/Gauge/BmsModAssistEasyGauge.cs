using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods.Gauge;

public class BmsModAssistEasyGauge : BmsModGauge
{
    public override string Name => "Assist Easy Gauge";

    public override string Acronym => "E2";

    public override LocalisableString Description => GaugeDescription("Assist Easy");

    public override ModType Type => ModType.DifficultyReduction;

    public override BmsGaugeType GaugeType => BmsGaugeType.AssistEasy;
}
