using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

internal delegate void BmsLongNoteStateObserver(NoteSnapshot state, in BmsFrameContext frame);

internal readonly record struct BmsLongNoteState(
    bool HeadJudged,
    bool LongNoteStarted,
    bool TailJudged,
    HitResult HeadResult,
    HitResult TailResult,
    bool EarlyReleaseOutsideWindow = false)
{
    internal static BmsLongNoteState From(BmsLongNote note, NoteSnapshot snapshot, double time)
    {
        var head = snapshot.Parts.Single(p => p.Part == NoteParts.HEAD);
        var tail = snapshot.Parts.Single(p => p.Part == NoteParts.TAIL);
        var body = snapshot.Parts.Single(p => p.Part == NoteParts.BODY);
        var started = body.Reason != HoldReasons.HOST_END && (note.EffectiveMode == BmsLongNoteMode.HellChargeNote
            ? head.Resolved && !body.Resolved
            : head.Resolved && head.Result != BmsResults.POOR && (!tail.Resolved || (tail.Result == BmsResults.POOR && time <= note.EndTime + 50)));
        return new BmsLongNoteState(head.Resolved, started, tail.Resolved,
            BmsEngineChart.HitResultFor(head.Result), BmsEngineChart.HitResultFor(tail.Result), tail.Reason == HoldReasons.DROP);
    }

    internal bool IsHolding => LongNoteStarted && !TailJudged;
}
