using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public class BmsModHalfTime : ModHalfTime, IApplicableToScoreSelection
{
    public IScoreSelectionRule ScoreSelectionRule => BmsRateScoreSelection.INSTANCE;
}
