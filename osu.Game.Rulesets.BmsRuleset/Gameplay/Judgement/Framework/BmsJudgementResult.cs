using System.Runtime.CompilerServices;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;

public class BmsJudgementResult(HitObject hitObject, Rulesets.Judgements.Judgement judgement) : JudgementResult(hitObject, judgement)
{
    // PMS still consumes a missed note after its earlier BAD/empty POOR, but must not
    // penalise it twice. Keep the framework result for completion and accuracy accounting.
    public bool SuppressPenalty { get; set; }

    internal double EventTime => getRawTime(this) ?? HitObject.GetEndTime();

    internal void SetTiming(double time, double rate)
    {
        setRawTime(this, time);
        setGameplayRate(this, rate);
    }

    internal void ResetForReplay()
    {
        Type = HitResult.None;
        setRawTime(this, null);
        SuppressPenalty = false;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_RawTime")]
    private static extern double? getRawTime(JudgementResult result);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_RawTime")]
    private static extern void setRawTime(JudgementResult result, double? time);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_GameplayRate")]
    private static extern void setGameplayRate(JudgementResult result, double? rate);
}
