using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public interface IApplicableToJudgementWindow : IApplicableMod
{
    BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table);
}
