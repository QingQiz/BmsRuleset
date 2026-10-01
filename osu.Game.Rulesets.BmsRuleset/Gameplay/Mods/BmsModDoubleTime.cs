using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public class BmsModDoubleTime : ModDoubleTime, IApplicableToScoreSelection
{
    public IScoreSelectionRule ScoreSelectionRule => BmsRateScoreSelection.INSTANCE;
}
