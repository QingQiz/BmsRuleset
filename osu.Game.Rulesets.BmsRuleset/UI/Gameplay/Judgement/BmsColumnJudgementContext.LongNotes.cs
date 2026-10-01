using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed partial class BmsColumnJudgementContext : IBmsLongNoteJudgementSink
{
    // State survives lifetime entries and pool bindings; only current representations may be retained.
    private readonly Dictionary<BmsLongNote, BmsLongNoteJudgementController> longNoteControllers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BmsLongNote, DrawableBmsHitObject> longNoteRepresentations = new(ReferenceEqualityComparer.Instance);

    public Action<bool, double> HellChargeTick { get; } = null!;

    internal BmsLongNoteJudgementController BindLongNote(DrawableBmsHitObject drawable)
    {
        var note = (BmsLongNote)drawable.HitObject;
        if (!longNoteControllers.TryGetValue(note, out var controller))
        {
            controller = new BmsLongNoteJudgementController(note, Column.PreserveLongNoteHistory);
            longNoteControllers.Add(note, controller);
        }
        else
            controller.RefreshConfiguration(Column.PreserveLongNoteHistory);

        if (longNoteRepresentations.TryGetValue(note, out var existing) && !ReferenceEquals(existing, drawable))
            throw new InvalidOperationException("A long note already has an active representation.");

        longNoteRepresentations[note] = drawable;
        return controller;
    }

    internal void UnbindLongNote(DrawableBmsHitObject drawable)
    {
        var note = (BmsLongNote)drawable.HitObject;
        if (longNoteRepresentations.TryGetValue(note, out var existing) && ReferenceEquals(existing, drawable))
            longNoteRepresentations.Remove(note);
    }

    internal void RewindLongNotes(double time)
    {
        Schedule?.Rewind();
        // This boundary precedes pool activation, so inactive heads and body histories recover too.
        foreach (var controller in longNoteControllers.Values)
            controller.Rewind(time);
    }

    internal bool TryPressLongNote(DrawableBmsHitObject note, HitResult result)
        => noteOutput != null ? TryPressRegisteredNote(note.HitObject, result,
               new BmsPressContext(note.Time.Current, noteFrame(note), Column.LastPressWasReverseScratch))
           : note.HitObject != null && !note.Judged
                                  && note.LongNoteJudgement!.TryHit(this, note.Time.Current, result, gameplayRate(note), Column.LastPressWasReverseScratch);

    internal bool TryRepressLongNote(DrawableBmsHitObject note, double currentTime, bool reverseScratch)
        => noteOutput != null ? longNoteBinding!.Repress(noteSlots[note.HitObject], new BmsPressContext(currentTime, noteFrame(note), reverseScratch))
            : note.LongNoteJudgement!.TryRepress(this, currentTime, reverseScratch, gameplayRate(note));

    internal bool TryReleaseLongNote(DrawableBmsHitObject note, double releaseOffset, BmsJudgementWindowTable tailTable, bool reverseScratch)
        => noteOutput != null ? longNoteBinding!.Release(noteSlots[note.HitObject], ((BmsLongNote)note.HitObject).EndTime + releaseOffset, noteFrame(note), reverseScratch)
            : note.HitObject != null && note.LongNoteJudgement!.TryRelease(this, note.Time.Current, releaseOffset, tailTable, gameplayRate(note), reverseScratch);

    internal bool CompleteLongNoteAtPause(DrawableBmsHitObject note, double currentTime)
        => noteOutput != null ? longNoteBinding!.CompleteAtPause(noteSlots[note.HitObject], new BmsFrameContext(currentTime, gameplayRate(note)))
            : note.HitObject != null && note.LongNoteJudgement!.CompleteAtPause(this, currentTime, gameplayRate(note));

    internal void CheckPassiveLongNote(DrawableBmsHitObject note)
    {
        if (note.HitObject == null || note.Judged || (note.Clock as IGameplayClock)?.IsRewinding == true)
            return;

        if (noteOutput != null)
            CheckRegisteredNotePassive(note.HitObject, noteFrame(note));
        else
            note.LongNoteJudgement!.CheckPassiveResult(this, note.Time.Current, gameplayRate(note));
    }

    internal void UpdateLongNotePostResult(DrawableBmsHitObject note, bool holding)
    {
        if (noteOutput != null)
            UpdateRegisteredLongNote((BmsLongNote)note.HitObject, noteFrame(note), note.Time.Elapsed, holding);
        else
            note.LongNoteJudgement!.UpdatePostResult(this, note.Time.Current, note.Time.Elapsed, holding, gameplayRate(note));
    }

    private static double gameplayRate(DrawableBmsHitObject note)
        => (note.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? note.Clock.Rate;

    private DrawableBmsHitObject representation(BmsLongNote note)
        => longNoteRepresentations.TryGetValue(note, out var drawable)
            ? drawable
            : throw new InvalidOperationException("Long-note judgement requires an active representation.");

    void IBmsLongNoteJudgementSink.OnUserHeadJudged(BmsLongNote note)
    {
        var drawable = representation(note);
        ((IBmsLongNoteDrawable)drawable).PrepareHeadPin();
        onHeadJudged(drawable);
    }

    void IBmsLongNoteJudgementSink.OnHellChargeHeadPoor(BmsLongNote note, double eventTime, double lifetimeEnd)
    {
        var drawable = representation(note);
        ((IBmsLongNoteDrawable)drawable).KeepBodyAlive(lifetimeEnd);
        onHeadJudged(drawable);
    }

    void IBmsLongNoteJudgementSink.ApplyJudgementResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints)
    {
        var drawable = representation(note);
        ((BmsLongNoteJudgementResult)drawable.Result).SetEndpointResults(endpoints);
        drawable.ApplyBmsResult(result);
    }

    void IBmsLongNoteJudgementSink.ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint)
        => playfield.ApplySyntheticLongNoteEndpoint(endpoint);

    void IBmsLongNoteJudgementSink.ClearVisualIfTailWasNotPoor(BmsLongNote note, HitResult result)
    {
        if (result != HitResult.Meh)
            ((IBmsLongNoteDrawable)representation(note)).ClearBody();
    }

    void IBmsLongNoteJudgementSink.Retire(BmsLongNote note)
        => ((IBmsLongNoteDrawable)representation(note)).Retire();
}
