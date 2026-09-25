using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModHalfTime : ModHalfTime, IApplicableToScoreSelection
{
    public Type ScoreSelectionRuleType => typeof(BmsRateScoreSelection);

    public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;

    public bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods) =>
        BmsRateScoreSelection.IsScoreEligible(scoreMods, selectedMods);
}
