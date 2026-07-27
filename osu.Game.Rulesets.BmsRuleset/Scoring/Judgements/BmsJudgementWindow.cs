using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public readonly record struct BmsJudgementWindow(HitResult Result, double SlowDTime, double FastDTime)
{
    public bool ContainsOffset(double timeOffset)
    {
        var dtime = -timeOffset;
        return dtime >= SlowDTime && dtime <= FastDTime;
    }

    public double SlowOffset => -SlowDTime;

    public double FastOffset => FastDTime;
}
