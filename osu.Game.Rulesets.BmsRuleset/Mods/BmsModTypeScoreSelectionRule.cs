using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public sealed class BmsModTypeScoreSelectionRule<TMod> : IScoreSelectionRule
    where TMod : Mod
{
    public static readonly BmsModTypeScoreSelectionRule<TMod> EXACT = new(true);
    public static readonly BmsModTypeScoreSelectionRule<TMod> REDUCTION = new(false);

    private readonly bool requireExactMatch;

    private BmsModTypeScoreSelectionRule(bool requireExactMatch)
    {
        this.requireExactMatch = requireExactMatch;
    }

    public bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods)
    {
        // Compare exact types so an unrelated mod with the same acronym cannot satisfy the rule.
        var scoreHasMod = scoreMods.Any(mod => mod.GetType() == typeof(TMod));
        var selectionHasMod = selectedMods.Any(mod => mod.GetType() == typeof(TMod));
        return requireExactMatch ? scoreHasMod == selectionHasMod : !scoreHasMod || selectionHasMod;
    }
}
