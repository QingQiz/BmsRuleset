namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public sealed record BmsJudgementProfile(
    BmsJudgementWindowTable Normal,
    BmsJudgementWindowTable Scratch,
    BmsJudgementWindowTable LongNoteTail,
    BmsJudgementWindowTable LongScratchTail);
