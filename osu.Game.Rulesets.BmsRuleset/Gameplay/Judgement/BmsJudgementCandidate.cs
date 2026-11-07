using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

public readonly record struct BmsJudgementCandidate(
    double StartTime,
    double EndTime,
    int Column,
    double JudgementRate,
    bool IsLongNote,
    bool IsJudged = false,
    bool HasMistake = false)
{
    internal static BmsJudgementCandidate FromNote(BmsHitObject note, bool judged = false, bool mistake = false)
        => new(note.StartTime, note is BmsLongNote hold ? hold.EndTime : note.StartTime, note.Column, note.EffectiveJudgementRate,
            note is BmsLongNote, judged, mistake);
}

public readonly record struct BmsJudgementSelection(
    BmsJudgementCandidate? Candidate,
    HitResult Result,
    bool IsEmptyPoor);
