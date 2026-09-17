using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModHalfTime : ModHalfTime, IApplicableToScoreSelection
{
    public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;

    public bool MatchesScoreSelection(Mod other) =>
        other is BmsModHalfTime halfTime && SpeedChange.Value == halfTime.SpeedChange.Value;
}
