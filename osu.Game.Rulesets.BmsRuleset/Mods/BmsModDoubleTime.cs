using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModDoubleTime : ModDoubleTime, IApplicableToScoreSelection
{
    public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Increase;

    public bool MatchesScoreSelection(Mod other) =>
        other is BmsModDoubleTime doubleTime && SpeedChange.Value == doubleTime.SpeedChange.Value;
}
