using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.Configuration;

public enum BmsJudgementAlgorithm
{
    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.JudgementAlgorithmCombo))]
    Combo,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.JudgementAlgorithmDuration))]
    Duration,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.JudgementAlgorithmLowest))]
    Lowest,

    [LocalisableDescription(typeof(BmsStrings), nameof(BmsStrings.JudgementAlgorithmScore))]
    Score,
}
