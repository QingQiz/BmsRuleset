namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;

public sealed record BmsJudgementProfile(
    BmsJudgementWindowTable Normal,
    BmsJudgementWindowTable Scratch,
    BmsJudgementWindowTable LongNoteTail,
    BmsJudgementWindowTable LongScratchTail);
