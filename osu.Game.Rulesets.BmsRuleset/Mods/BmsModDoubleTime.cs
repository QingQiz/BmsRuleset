using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModDoubleTime : ModDoubleTime, IApplicableToScoreSelection
{
    public IScoreSelectionRule ScoreSelectionRule => BmsRateScoreSelection.INSTANCE;
}
