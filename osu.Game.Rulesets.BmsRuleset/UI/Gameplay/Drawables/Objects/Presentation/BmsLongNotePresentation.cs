using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

internal delegate BmsLongNotePresentation BmsLongNotePresentationMapping(BmsLongNote note, in BmsLongNoteState state, bool held, double time);

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
    private static readonly BmsLongNotePresentationMapping long_note_mapping = longNote;
    private static readonly BmsLongNotePresentationMapping charge_note_mapping = chargeNote;
    private static readonly BmsLongNotePresentationMapping hell_charge_note_mapping = hellChargeNote;

    internal BmsHitLightStyle HitLightStyle => BmsHitLightStyle.Hold;

    internal static bool HideDuringResume(BmsLongNote note, in BmsLongNoteState state, bool hasMainResult, double resumeTime)
        // HCN head-POOR can complete both endpoints synthetically without a main result.
        => note.EndTime <= resumeTime || (hasMainResult && !state.IsHolding);

    internal static BmsLongNotePresentationMapping SelectMapping(BmsLongNoteMode mode)
        => mode switch
        {
            BmsLongNoteMode.ChargeNote => charge_note_mapping,
            BmsLongNoteMode.HellChargeNote => hell_charge_note_mapping,
            _ => long_note_mapping,
        };

    private static BmsLongNotePresentation longNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
        => new(state.HeadJudged, state.IsHolding && held, false, false,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh, state.TailResult != HitResult.Meh, note.EndTime, holdLightActive(note, state, time));

    private static BmsLongNotePresentation chargeNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
        => new(state.HeadJudged, state.IsHolding && held, true, false,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh, state.TailResult != HitResult.Meh, chargeLifetimeEnd(note), holdLightActive(note, state, time));

    private static BmsLongNotePresentation hellChargeNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
    {
        // A successful early tail keeps the remaining HCN body visually held.
        var successfulTail = state.TailJudged && state.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        return new BmsLongNotePresentation(state.HeadJudged, state.LongNoteStarted && (held || successfulTail), true,
            state.LongNoteStarted && state.HeadResult == HitResult.Meh && time < chargeLifetimeEnd(note),
            state.TailJudged && !state.LongNoteStarted, false, chargeLifetimeEnd(note), holdLightActive(note, state, time));
    }

    private static bool holdLightActive(BmsLongNote note, in BmsLongNoteState state, double time)
        => state.LongNoteStarted && !state.TailJudged && time >= note.StartTime && time <= note.EndTime;

    private static double chargeLifetimeEnd(BmsLongNote note)
        => note.EndTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column,
            note.EffectiveJudgementRate, tail: false).SlowWindowFor(HitResult.Ok) + 100;
}
