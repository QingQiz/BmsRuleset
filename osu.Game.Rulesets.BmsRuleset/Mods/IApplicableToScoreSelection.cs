using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

/// <summary>
/// Opts a mod into best-score filtering independently of its mod-menu category.
/// Mods without this interface do not restrict best-score selection.
/// </summary>
public interface IApplicableToScoreSelection
{
    ScoreSelectionDifficulty Difficulty { get; }

    /// <summary>
    /// Identifies a rule for deduplication across both mod lists. Implementations sharing
    /// an identity must evaluate the same rule from the supplied lists, not instance settings.
    /// </summary>
    Type ScoreSelectionRuleType => GetType();

    /// <summary>
    /// Evaluates a score against the selection, regardless of which side supplied this rule.
    /// The default compares types so cosmetic settings do not separate historical scores.
    /// </summary>
    bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods)
    {
        var scoreHasMod = scoreMods.Any(mod => mod.GetType() == GetType());
        var selectionHasMod = selectedMods.Any(mod => mod.GetType() == GetType());
        return Difficulty == ScoreSelectionDifficulty.Increase
            ? scoreHasMod == selectionHasMod
            : !scoreHasMod || selectionHasMod;
    }

    public enum ScoreSelectionDifficulty
    {
        /// <summary>
        /// The mod must match in both the score and the current selection.
        /// </summary>
        Increase,

        /// <summary>
        /// A score using the mod requires a matching selected mod, but selecting it also accepts scores without it.
        /// </summary>
        Reduction,
    }
}
