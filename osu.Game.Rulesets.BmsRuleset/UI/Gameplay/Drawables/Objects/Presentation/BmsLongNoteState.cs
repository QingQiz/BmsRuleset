using osu.Game.Rulesets.Scoring;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using RhythmEngine.Core;

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
        var head = snapshot.Parts.Single(p => p.Part == "head");
        var tail = snapshot.Parts.Single(p => p.Part == "tail");
        var body = snapshot.Parts.Single(p => p.Part == "body");
        var started = body.Reason != "End" && (note.EffectiveMode == BmsLongNoteMode.HellChargeNote
            ? head.Resolved && !body.Resolved
            : head.Resolved && head.Result != "poor" && (!tail.Resolved || (tail.Result == "poor" && time <= note.EndTime + 50)));
        return new BmsLongNoteState(head.Resolved, started, tail.Resolved,
            BmsEngineChart.HitResultFor(head.Result), BmsEngineChart.HitResultFor(tail.Result), tail.Reason == "Drop");
    }

    internal bool IsHolding => LongNoteStarted && !TailJudged;
}
