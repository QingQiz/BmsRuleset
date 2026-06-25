using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

/// <summary>
/// A mod that mutates the populated <see cref="ScoreInfo"/> after the base score
/// population has run, so attribution logic (e.g. which gauge a run resolved to)
/// lives with the mod that introduced the behaviour rather than the score processor.
/// </summary>
public interface IApplicableToScorePopulation
{
    void ApplyToScore(ScoreInfo score);
}
