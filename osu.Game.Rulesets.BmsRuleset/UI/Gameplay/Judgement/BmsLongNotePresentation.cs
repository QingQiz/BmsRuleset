using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal delegate BmsLongNotePresentation BmsLongNotePresentationMapping(BmsLongNote note, in BmsLongNoteState state, bool held, double time);

internal readonly record struct BmsLongNotePresentation(
    bool HeadJudged, bool HoldingBody, bool KeepAfterHeadResult, bool KeepAlive, bool Retired, double LifetimeEnd)
{
    private static readonly BmsLongNotePresentationMapping longNoteMapping = longNote;
    private static readonly BmsLongNotePresentationMapping chargeNoteMapping = chargeNote;
    private static readonly BmsLongNotePresentationMapping hellChargeNoteMapping = hellChargeNote;

    internal BmsHitLightStyle HitLightStyle => BmsHitLightStyle.Hold;

    internal static bool HideDuringResume(BmsLongNote note, in BmsLongNoteState state, bool hasMainResult, double resumeTime)
        // HCN head-POOR can complete both endpoints synthetically without a main result.
        => note.EndTime <= resumeTime || hasMainResult && !state.IsHolding;

    internal static BmsLongNotePresentationMapping SelectMapping(BmsLongNoteMode mode)
        => mode switch
        {
            BmsLongNoteMode.ChargeNote => chargeNoteMapping,
            BmsLongNoteMode.HellChargeNote => hellChargeNoteMapping,
            _ => longNoteMapping,
        };

    private static BmsLongNotePresentation longNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
        => new(state.HeadJudged, state.IsHolding && held, false, false,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh, note.EndTime);

    private static BmsLongNotePresentation chargeNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
        => new(state.HeadJudged, state.IsHolding && held, true, false,
            state.TailJudged && !state.LongNoteStarted && state.HeadResult != HitResult.Meh, chargeLifetimeEnd(note));

    private static BmsLongNotePresentation hellChargeNote(BmsLongNote note, in BmsLongNoteState state, bool held, double time)
    {
        // A successful early tail keeps the remaining HCN body visually held.
        var successfulTail = state.TailJudged && state.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        return new BmsLongNotePresentation(state.HeadJudged, state.LongNoteStarted && (held || successfulTail), true,
            state.LongNoteStarted && state.HeadResult == HitResult.Meh && time < chargeLifetimeEnd(note),
            state.TailJudged && !state.LongNoteStarted, chargeLifetimeEnd(note));
    }

    private static double chargeLifetimeEnd(BmsLongNote note)
        => note.EndTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column,
            note.EffectiveJudgementRate, tail: false).SlowWindowFor(HitResult.Ok) + 100;
}
