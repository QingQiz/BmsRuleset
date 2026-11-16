using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal sealed partial class BmsGameplayProfile
{
    internal override BmsLongNotePresentation LongNotePresentation(BmsLongNote note, NoteSnapshot snapshot, in BmsLongNotePresentationContext context)
    {
        var state = BmsLongNoteState.From(note, snapshot, context.Time, this);
        return note.Kind switch
        {
            KeyNoteKind.ReleaseHold => chargeNote(note, state, context),
            KeyNoteKind.SustainHold => hellChargeNote(note, state, context),
            _ => longNote(note, state, context),
        };
    }

    private static BmsLongNotePresentation longNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        var keepBody = state.TailJudged && state.EarlyReleaseOutsideWindow && context.Time < note.EndTime;
        return complete(new BmsLongNotePresentation(state.HeadJudged, state.IsHolding && context.Held, false, keepBody,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh && !keepBody, state.TailResult != HitResult.Meh,
            note.EndTime, holdLightActive(note, state, context.Time)), note, state, context);
    }

    private static BmsLongNotePresentation chargeNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        var keepBody = state.TailJudged && state.EarlyReleaseOutsideWindow && context.Time < note.EndTime;
        return complete(new BmsLongNotePresentation(state.HeadJudged, state.IsHolding && context.Held, true, keepBody,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh && !keepBody, state.TailResult != HitResult.Meh,
            state.TailJudged ? note.EndTime : chargeLifetimeEnd(note, context), holdLightActive(note, state, context.Time)), note, state, context);
    }

    private static BmsLongNotePresentation hellChargeNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        // A successful early tail keeps the remaining HCN body visually held.
        var successfulTail = state.TailJudged && state.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        var lifetimeEnd = chargeLifetimeEnd(note, context);
        return complete(new BmsLongNotePresentation(state.HeadJudged, state.LongNoteStarted && (context.Held || successfulTail), true,
            state.LongNoteStarted && state.HeadResult == HitResult.Meh && context.Time < lifetimeEnd,
            state.TailJudged && !state.LongNoteStarted, false, lifetimeEnd, holdLightActive(note, state, context.Time)), note, state, context);
    }

    private static BmsLongNotePresentation complete(BmsLongNotePresentation presentation, BmsLongNote note,
                                                    in BmsLongNoteState state, in BmsLongNotePresentationContext context)
        => presentation with
        {
            AnimateBody = state.LongNoteStarted,
            // An out-of-window break keeps the released appearance until the remaining body passes.
            ReleasedStyle = !presentation.Retired && (state.LongNoteStarted || (state.TailJudged && state.HeadResult != HitResult.Meh))
                                                  && context.Time < note.EndTime && !presentation.HoldingBody,
            PrepareHeadPin = state.HeadJudged && !state.TailJudged,
            RestoreHeadPin = state.LongNoteStarted,
            HeadReached = context.Time >= note.StartTime,
            KeepVisibleAfterHit = !presentation.Retired && (context.Time < note.EndTime || (presentation.KeepAfterHeadResult && !state.TailJudged)),
        };

    private static bool holdLightActive(BmsLongNote note, in BmsLongNoteState state, double time)
        => state.LongNoteStarted && !state.TailJudged && time >= note.StartTime && time <= note.EndTime;

    private static double chargeLifetimeEnd(BmsLongNote note, in BmsLongNotePresentationContext context)
        => note.EndTime + context.Timing.TailDeadline / 1000.0 + 100;
}
