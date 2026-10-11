using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public interface IApplicableToJudgementWindow : IApplicableMod
{
    BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table);
}
