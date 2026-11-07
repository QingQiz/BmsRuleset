using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public interface IApplicableToJudgement : IApplicableMod
{
    bool ReplacesBaseWindows => false;

    Func<BmsLayoutVariant, int, double?, double>? JudgementRateCalculator => null;

    BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table, in BmsJudgementWindowContext context) => table;

    BmsJudgementBehaviour ApplyToJudgement(BmsJudgementBehaviour behaviour) => behaviour;
}
