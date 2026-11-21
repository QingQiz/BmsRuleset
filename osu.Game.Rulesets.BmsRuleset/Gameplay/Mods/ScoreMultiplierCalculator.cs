using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

internal sealed class BmsScoreMultiplierCalculator : ScoreMultiplierCalculator
{
    public BmsScoreMultiplierCalculator(ScoreMultiplierContext context)
        : base(context)
    {
        Combination<BmsModManiaProfile, BmsModDoubleTime>((mania, _) => mania.UseLazer.Value ? 1 : 2);
        Combination<BmsModManiaProfile, BmsModHalfTime>((mania, halfTime) => mania.UseLazer.Value ? maniaSlowRateMultiplier(halfTime) : 0.5);
        Single<BmsModDoubleTime>(2);
        Single<BmsModHalfTime>(0.5);
    }

    private static double maniaSlowRateMultiplier(BmsModHalfTime halfTime)
    {
        var speed = (int)(halfTime.SpeedChange.Value * 10) / 10.0;
        return 0.6 + (speed - 1);
    }
}
