using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

internal readonly record struct BmsLongNotePresentationContext(double Time, bool Held);

internal delegate BmsLongNotePresentation BmsLongNotePresentationMapping(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context);

internal readonly record struct BmsLongNotePresentation(
    bool HeadJudged,
    bool HoldingBody,
    bool KeepAfterHeadResult,
    bool KeepAlive,
    bool Retired,
    bool ClearBodyOnRetire,
    double LifetimeEnd,
    bool HoldLightActive)
{
    internal bool AnimateBody { get; init; }

    internal bool ReleasedStyle { get; init; }

    internal bool PrepareHeadPin { get; init; }

    internal bool RestoreHeadPin { get; init; }

    internal bool HeadReached { get; init; }

    internal bool KeepVisibleAfterHit { get; init; }

    private static readonly BmsLongNotePresentationMapping long_note_mapping = longNote;
    private static readonly BmsLongNotePresentationMapping charge_note_mapping = chargeNote;
    private static readonly BmsLongNotePresentationMapping hell_charge_note_mapping = hellChargeNote;

    internal BmsHitLightStyle HitLightStyle => BmsHitLightStyle.Hold;

    internal static BmsLongNotePresentationMapping SelectMapping(BmsLongNoteMode mode)
        => mode switch
        {
            BmsLongNoteMode.ChargeNote => charge_note_mapping,
            BmsLongNoteMode.HellChargeNote => hell_charge_note_mapping,
            _ => long_note_mapping,
        };

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
            state.TailJudged ? note.EndTime : chargeLifetimeEnd(note), holdLightActive(note, state, context.Time)), note, state, context);
    }

    private static BmsLongNotePresentation hellChargeNote(BmsLongNote note, in BmsLongNoteState state, in BmsLongNotePresentationContext context)
    {
        // A successful early tail keeps the remaining HCN body visually held.
        var successfulTail = state.TailJudged && state.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        var lifetimeEnd = chargeLifetimeEnd(note);
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

    private static double chargeLifetimeEnd(BmsLongNote note)
        => note.EndTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column,
            note.EffectiveJudgementRate, tail: false).SlowWindowFor(HitResult.Ok) + 100;
}
