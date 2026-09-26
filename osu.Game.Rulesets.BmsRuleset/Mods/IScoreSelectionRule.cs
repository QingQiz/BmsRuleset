using System.Collections.Generic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public interface IScoreSelectionRule
{
    /// <summary>
    /// Evaluates the supplied lists independently of the mod that provided this rule.
    /// </summary>
    bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods);
}
