using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public sealed record BmsJudgementBehaviour
{
    public double SearchWindow { get; init; } = 500;

    public BmsJudgementAlgorithm? Algorithm { get; init; }

    public bool NonConsumingBad { get; init; }

    public double LongNoteReleaseMargin { get; init; }

    public bool LongNoteBadIsImmediate { get; init; }
}
