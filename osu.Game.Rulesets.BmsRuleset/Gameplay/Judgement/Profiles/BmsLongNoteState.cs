using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal readonly record struct BmsLongNoteState(
    bool HeadJudged,
    bool LongNoteStarted,
    bool TailJudged,
    HitResult HeadResult,
    HitResult TailResult,
    bool TailReleased = false,
    bool BodyBroken = false)
{
    internal static BmsLongNoteState From(BmsLongNote note, NoteSnapshot snapshot, double time, GameplayProfile profile)
    {
        var head = snapshot.Parts.Single(p => p.Part == NoteParts.HEAD);
        var tail = snapshot.Parts.Single(p => p.Part == NoteParts.TAIL);
        var body = snapshot.Parts.Single(p => p.Part == NoteParts.BODY);
        var headResult = profile.HitResultFor(head.Result);
        var tailResult = profile.HitResultFor(tail.Result);
        var ended = body.Reason == HoldReasons.HOST_END;
        var started = !ended && (note.Kind == KeyNoteKind.SustainHold
            ? head.Resolved && !body.Resolved
            : head.Resolved && headResult != HitResult.Meh && (!tail.Resolved || (tailResult == HitResult.Meh && time <= note.EndTime + 50)));
        return new BmsLongNoteState(head.Resolved, started, tail.Resolved, headResult, tailResult,
            !ended && (tail.Reason == HoldReasons.RELEASE || tail.Reason == HoldReasons.DROP), body.Reason == HoldReasons.DROP);
    }

    internal bool IsHolding => LongNoteStarted && !TailJudged;
}
