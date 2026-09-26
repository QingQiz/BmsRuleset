namespace osu.Game.Rulesets.BmsRuleset.Mods;

/// <summary>
/// Opts a mod into best-score filtering independently of its mod-menu category.
/// Mods without this interface do not restrict best-score selection.
/// </summary>
public interface IApplicableToScoreSelection
{
    /// <summary>
    /// Returns a shared rule so equivalent restrictions are evaluated only once.
    /// </summary>
    IScoreSelectionRule ScoreSelectionRule { get; }
}
