using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

internal sealed class BmsScoreMultiplierCalculator : ScoreMultiplierCalculator
{
    public BmsScoreMultiplierCalculator(ScoreMultiplierContext context)
        : base(context)
    {
        Single<BmsModDoubleTime>(2);
        Single<BmsModHalfTime>(0.5);
    }
}
