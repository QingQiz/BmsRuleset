using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal sealed partial class BmsGameplayProfile
{
    private static readonly HoldFeedbackPolicy hold_feedback = new(HoldFeedback.All, HoldFeedback.None, HoldFeedback.AnimateBody | HoldFeedback.HoldLight);
    private static readonly HoldFeedbackPolicy sustain_feedback = hold_feedback with { Recovered = HoldFeedback.All };

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
        return complete(new BmsLongNotePresentation(state.HeadJudged, false, keepBody,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh && !keepBody, state.TailResult != HitResult.Meh,
            note.EndTime), note, state, context);
    }

    private static BmsLongNotePresentation chargeNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        var keepBody = state.TailJudged && state.EarlyReleaseOutsideWindow && context.Time < note.EndTime;
        return complete(new BmsLongNotePresentation(state.HeadJudged, true, keepBody,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh && !keepBody, state.TailResult != HitResult.Meh,
            state.TailJudged ? note.EndTime : chargeLifetimeEnd(note, context)), note, state, context);
    }

    private static BmsLongNotePresentation hellChargeNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        // A successful early tail keeps the remaining HCN body visually held.
        var successfulTail = state.TailJudged && state.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        var lifetimeEnd = chargeLifetimeEnd(note, context);
        return complete(new BmsLongNotePresentation(state.HeadJudged, true,
            state.LongNoteStarted && state.HeadResult == HitResult.Meh && context.Time < lifetimeEnd,
            state.TailJudged && !state.LongNoteStarted, false, lifetimeEnd), note, state, context, successfulTail);
    }

    private static BmsLongNotePresentation complete(BmsLongNotePresentation presentation, BmsLongNote note,
                                                    in BmsLongNoteState state, in BmsLongNotePresentationContext context, bool keepSuccessfulTailHeld = false)
    {
        var sustain = note.Kind == KeyNoteKind.SustainHold;
        var policy = sustain ? sustain_feedback : hold_feedback;
        var held = state.LongNoteStarted && (sustain ? context.Held || keepSuccessfulTailHeld : state.IsHolding && context.Held);
        var feedback = policy.Resolve(held, state.BodyBroken);
        var showReleasedStyle = !presentation.Retired && context.Time < note.EndTime
                                                      && (state.LongNoteStarted || (state.TailJudged && state.HeadResult != HitResult.Meh));

        return (presentation with
        {
            PrepareHeadPin = state.HeadJudged && !state.TailJudged,
            HeadReached = context.Time >= note.StartTime,
            KeepVisibleAfterHit = !presentation.Retired && (context.Time < note.EndTime || (presentation.KeepAfterHeadResult && !state.TailJudged)),
        }).WithHoldFeedback(feedback, showReleasedStyle, (!state.TailJudged || (sustain && context.Held))
                                                         && context.Time >= note.StartTime && context.Time <= note.EndTime);
    }

    private static double chargeLifetimeEnd(BmsLongNote note, in BmsLongNotePresentationContext context)
        => note.EndTime + context.Timing.TailDeadline / 1000.0 + 100;
}
