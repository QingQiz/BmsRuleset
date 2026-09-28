using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public readonly record struct BmsJudgementCandidate(
    double StartTime,
    double EndTime,
    int Column,
    double JudgementRate,
    bool IsLongNote,
    bool IsJudged = false,
    bool HasMistake = false);

public readonly record struct BmsJudgementSelection(
    BmsJudgementCandidate? Candidate,
    HitResult Result,
    bool IsEmptyPoor);
