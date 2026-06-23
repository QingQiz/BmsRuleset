using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public readonly record struct BmsJudgementWindow(HitResult Result, double LateDTime, double EarlyDTime)
{
    public bool ContainsOffset(double timeOffset)
    {
        var dtime = -timeOffset;
        return dtime >= LateDTime && dtime <= EarlyDTime;
    }

    public double LateOffset => -LateDTime;

    public double EarlyOffset => EarlyDTime;
}
