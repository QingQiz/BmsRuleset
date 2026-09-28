using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public class BmsJudgementResult(HitObject hitObject, Judgement judgement) : JudgementResult(hitObject, judgement)
{
    // PMS still consumes a missed note after its earlier BAD/empty POOR, but must not
    // penalise it twice. Keep the framework result for completion and accuracy accounting.
    public bool SuppressPenalty { get; set; }
}
