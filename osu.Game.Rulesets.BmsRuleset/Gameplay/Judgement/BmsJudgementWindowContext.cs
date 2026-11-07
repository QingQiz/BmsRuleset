using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public readonly record struct BmsJudgementWindowContext(
    BmsLayoutVariant Layout,
    double Rate,
    bool Tail);
