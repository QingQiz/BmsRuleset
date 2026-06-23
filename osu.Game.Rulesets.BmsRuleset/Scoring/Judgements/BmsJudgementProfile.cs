namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public sealed record BmsJudgementProfile(
    BmsJudgementWindowTable Normal,
    BmsJudgementWindowTable Scratch,
    BmsJudgementWindowTable LongNoteTail,
    BmsJudgementWindowTable LongScratchTail);
