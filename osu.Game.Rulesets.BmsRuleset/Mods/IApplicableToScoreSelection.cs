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
    /// Compares the settings relevant to score selection. The default compares only mod types,
    /// so cosmetic settings do not unnecessarily separate historical scores.
    /// </summary>
    bool MatchesScoreSelection(Mod other) => GetType() == other.GetType();

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
