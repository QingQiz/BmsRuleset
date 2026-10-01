using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Coordinates one column's input and checkpoints. Registries own state, indexes own membership,
/// and the schedule owns phase execution; none of them requires a graphical host.
/// </summary>
internal sealed class BmsColumnJudgementRuntime
{
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];
    private readonly Dictionary<BmsHitObject, int> noteSlots = new(ReferenceEqualityComparer.Instance);
    private readonly List<(int Slot, BmsJudgementCandidate Candidate)> noteCandidates = [];
    private readonly IBmsJudgementOutput noteOutput;
    private readonly BmsHoldRegistry holds;
    private readonly BmsLandmineRegistry landmines;
    private readonly BmsLongNote[] longNotes;

    internal BmsJudgementRules Rules { get; }

    internal int Index { get; }

    internal bool IsScratch => BmsLayout.IsScratchColumn(Index, LayoutVariant);

    internal BmsLayoutVariant LayoutVariant { get; }

    internal BmsColumnNoteIndex Notes { get; }

    internal BmsCoreJudgementSchedule Schedule { get; }

    internal BmsNoteRegistration[] NoteRegistrations { get; }

    internal BmsNoteJudgementWindows[] NoteWindows { get; }

    internal IReadOnlyList<BmsLongNote> ActiveLongNotes => Notes.ActiveLongNotes;

    internal double NextPassiveUpdateTime => Notes.NextPassiveUpdateTime;

    internal void ApplyEmptyPoor(double expectedTime, in BmsFrameContext frame) => noteOutput.ApplyEmptyPoor(expectedTime, Index, frame);

    internal bool HasPmsMistake(BmsHitObject note) => BmsJudgementProfileProvider.IsPms(LayoutVariant) && pressHistory.Get(note).Mistake;

    internal bool IsKeySoundFinished(BmsHitObject note, double time)
        => IsNoteActive(note) ? GetNoteResult(note).HasResult : time > note.StartTime;

    internal void RecordResult(BmsHitObject note, double time) => pressHistory.Record(note, time, true);

    internal void RewindPressHistory(double time) => pressHistory.Rewind(time);

    internal void RefreshPassiveDeadline(double now) => Notes.RefreshDeadline(now);

    internal bool RequiresActiveLongNoteUpdate(BmsHitObject note) => Notes.RequiresActiveLongNoteUpdate(note);

    internal void RetireInactiveLongNoteAt(int index) => Notes.RetireInactiveLongNoteAt(index);

    internal void CompleteLongNotesAtPause(in BmsFrameContext frame)
    {
        foreach (var note in GetHoldingNotes().ToArray())
            holds.Pause(noteSlots[note], frame);
    }

    internal void RewindLongNotes(double time)
    {
        Schedule.Rewind();
        // Inactive objects retain history so pool activation cannot resurrect a future head.
        foreach (var note in longNotes)
        {
            holds.Rewind(noteSlots[note], time);
        }
    }

    internal int PressedDirections { get; set; }

    internal BmsColumnJudgementRuntime(BmsHitObject[] notes, BmsLayoutVariant layout, int column, bool batchUpdates, IBmsJudgementOutput output, BmsJudgementRules rules)
    {
        Rules = rules;
        Index = column;
        LayoutVariant = layout;
        columnNotes = notes.Where(n => n.Column == column && n is BmsNote or BmsLongNote).OrderBy(n => n.StartTime).ToArray();
        landmines = new BmsLandmineRegistry(notes, column, rules.Landmine, output);
        Schedule = new BmsCoreJudgementSchedule(this);
        noteOutput = output;
        var binding = new BmsNoteOperationBinding(this, output);
        NoteRegistrations = new BmsNoteRegistration[columnNotes.Length];
        NoteWindows = new BmsNoteJudgementWindows[columnNotes.Length];
        longNotes = columnNotes.Any(n => n is BmsLongNote) ? new BmsLongNote[columnNotes.Length] : [];
        for (var i = 0; i < columnNotes.Length; i++)
        {
            noteSlots.Add(columnNotes[i], i);
            if (columnNotes[i] is BmsLongNote note) longNotes[i] = note;
        }

        holds = new BmsHoldRegistry(longNotes, Rules, new BmsJudgementExecutionContext(this, output));
        for (var i = 0; i < columnNotes.Length; i++)
        {
            var note = columnNotes[i];
            if (note is BmsLongNote)
                NoteRegistrations[i] = new BmsNoteRegistration(note, i, holds.PressOperation, holds.PassiveOperation);
            else
            {
                NoteRegistrations[i] = new BmsNoteRegistration(note, i, binding.Press, binding.Passive);
                NoteWindows[i] = new BmsNoteJudgementWindows(note);
            }
        }

        Notes = new BmsColumnNoteIndex(noteSlots, NoteRegistrations, NoteWindows, holds, output, batchUpdates);
        output.SetResultCheckpoints(RecordResult, Notes.Refresh);
    }

    internal BmsLongNoteState GetLongNoteState(BmsLongNote note) => holds.GetState(noteSlots[note]);

    internal IEnumerable<BmsLongNote> GetPendingLongNoteTails()
    {
        for (var i = 0; i < Notes.PendingTails.Count; i++)
            yield return longNotes[Notes.PendingTails[i]];
    }

    internal IEnumerable<BmsLongNote> GetActiveBodies()
    {
        for (var i = 0; i < Notes.ActiveBodies.Count; i++)
            yield return longNotes[Notes.ActiveBodies[i]];
    }

    internal IEnumerable<BmsLongNote> GetHoldingNotes()
    {
        for (var i = 0; i < Notes.PendingTails.Count; i++)
        {
            var slot = Notes.PendingTails[i];
            var longNote = longNotes[slot];
            if (GetLongNoteState(longNote).IsHolding)
                yield return longNote;
        }
    }

    internal void RefreshExecutionState(BmsLongNote note)
    {
        Notes.Refresh(note);
        Notes.InvalidateDeadline();
    }

    internal BmsMainResultState GetNoteResult(BmsHitObject note) => noteOutput.GetMainResult(note);

    internal BmsHitObject? FirstPendingNote => Notes.FirstPendingNote;

    internal bool IsNoteActive(BmsHitObject note) => Notes.IsNoteActive(note);

    internal bool IsNoteCandidate(BmsHitObject note) => Notes.IsNoteCandidate(note);

    internal IEnumerable<BmsHitObject> GetNoteCandidates() => Notes.GetNoteCandidates();

    internal void ActivateNote(BmsHitObject note) => Notes.ActivateNote(note);

    internal void BeginNoteRemoval(BmsHitObject note) => Notes.BeginNoteRemoval(note);

    internal void SetNoteActivationOrder(IEnumerable<BmsHitObject> order) => Notes.SetActivationOrder(order);

    internal void RefreshNoteTiming(BmsHitObject note)
    {
        if (!noteSlots.ContainsKey(note) || !Notes.RefreshTiming(note)) return;

        // Configuration changes keep the existing relative order of equal-time chart objects.
        columnNotes.OrderBy(n => n.StartTime).ToArray().CopyTo(columnNotes, 0);
    }

    internal void RefreshNoteConfiguration(BmsHitObject note, bool preserveHistory = true)
    {
        if (!noteSlots.TryGetValue(note, out var slot)) return;

        if (note is BmsLongNote) holds.Configure(slot, preserveHistory);
        else NoteWindows[slot] = new BmsNoteJudgementWindows(note);
        RefreshNoteTiming(note);
        Notes.Refresh(note);
        Notes.InvalidateDeadline();
    }

    private bool hasPendingExecutionHead(BmsHitObject note)
        => note is BmsLongNote ln ? !GetLongNoteState(ln).HeadJudged : !noteOutput.GetMainResult(note).HasResult;

    internal void RestoreExecutionState(in BmsFrameContext frame)
    {
        for (var i = 0; i < Notes.ActiveSlots.Count; i++)
        {
            var slot = Notes.ActiveSlots[i];
            if (NoteRegistrations[slot].Object is BmsLongNote note)
                noteOutput.PublishState(note, holds.GetState(noteSlots[note]), frame);
        }

        Notes.Rebuild();
    }


    internal bool TryPressRegisteredNote(BmsHitObject note, HitResult result, in BmsPressContext press)
    {
        var registration = NoteRegistrations[noteSlots[note]];
        return Notes.IsNoteActive(note) && registration.Press(registration.Slot, result, press);
    }

    internal void CheckRegisteredNotePassive(BmsHitObject note, in BmsFrameContext frame)
    {
        var registration = NoteRegistrations[noteSlots[note]];
        registration.Passive(registration.Slot, frame);
    }

    internal PressOutcome HandleNotePress(in BmsPressContext press, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        PressedDirections |= IsScratch && press.ReverseScratch ? 2 : 1;
        if (press.Frame.IsRewinding)
            return PressOutcome.Empty;

        for (var i = 0; i < Notes.PendingTails.Count; i++)
        {
            var slot = Notes.PendingTails[i];
            if (holds.Repress(slot, press))
            {
                var longNote = longNotes[slot];
                if (!GetLongNoteState(longNote).IsHolding)
                    noteOutput.PlayTailSample(longNote);
                return PressOutcome.Hit;
            }
        }

        if (FirstPendingNote is { } first && first.StartTime == press.InputTime && IsNoteCandidate(first)
            && BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, first.EffectiveJudgementRate, false).ResultForOffset(0) == HitResult.Perfect
            && TryPressRegisteredNote(first, HitResult.Perfect, press))
        {
            pressHistory.Record(first, press.InputTime, true);
            noteOutput.PlaySample(first);
            return PressOutcome.Hit;
        }

        noteCandidates.Clear();
        pressJudgementCandidates.Clear();
        for (var i = 0; i < Notes.ActiveSlots.Count; i++)
        {
            var slot = Notes.ActiveSlots[i];
            var note = NoteRegistrations[slot].Object;
            if (noteOutput.GetMainResult(note).HasResult)
                continue;

            var candidate = new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column, note.EffectiveJudgementRate,
                note is BmsLongNote, !hasPendingExecutionHead(note), pressHistory.Get(note).Mistake);
            noteCandidates.Add((slot, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        var low = 0;
        var high = columnNotes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (columnNotes[middle].StartTime < press.InputTime - 175)
                low = middle + 1;
            else
                high = middle;
        }

        for (var i = low; i < columnNotes.Length && columnNotes[i].StartTime <= press.InputTime + 500; i++)
        {
            var note = columnNotes[i];
            var state = pressHistory.Get(note);
            if (state.Judged)
                pressJudgementCandidates.Add(new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column,
                    note.EffectiveJudgementRate, note is BmsLongNote, true, state.Mistake));
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, press.InputTime, algorithm);
        if (!selection.IsEmptyPoor && selection.Candidate is { } selected)
        {
            foreach (var candidate in noteCandidates)
            {
                if (!candidate.Candidate.Equals(selected))
                    continue;

                var note = NoteRegistrations[candidate.Slot].Object;
                if (selection.Result == HitResult.Ok && BmsJudgementProfileProvider.IsPms(LayoutVariant))
                {
                    pressHistory.Record(note, press.InputTime, false, true);
                    noteOutput.ApplyNonConsumingBad(note, press.Frame);
                    noteOutput.PlaySample(note);
                    return PressOutcome.Hit;
                }

                if (TryPressRegisteredNote(note, selection.Result, press))
                {
                    pressHistory.Record(note, press.InputTime, true);
                    noteOutput.PlaySample(note);
                    return PressOutcome.Hit;
                }

                break;
            }
        }

        noteOutput.PlayKeySound();
        if (selection is { IsEmptyPoor: true, Candidate: { } emptyPoor })
        {
            if (press.IsResumeRewinding)
                return PressOutcome.Empty;

            foreach (var candidate in noteCandidates)
            {
                if (candidate.Candidate.Equals(emptyPoor))
                    pressHistory.Record(NoteRegistrations[candidate.Slot].Object, press.InputTime, false, true);
            }

            return PressOutcome.ForEmptyPoor(emptyPoor.StartTime, emptyPoor.Column);
        }

        return PressOutcome.Empty;
    }

    internal void HandleExecutionRelease(double inputTime, in BmsFrameContext frame, bool reverseScratch = false)
    {
        var direction = IsScratch && reverseScratch ? 2 : 1;
        if ((PressedDirections & direction) == 0)
            return;

        PressedDirections &= ~direction;
        if (frame.IsRewinding)
            return;

        BmsLongNote? held = null;
        for (var i = 0; i < Notes.PendingTails.Count; i++)
        {
            var slot = Notes.PendingTails[i];
            var longNote = longNotes[slot];
            if (!GetLongNoteState(longNote).IsHolding)
                continue;

            var note = longNote;
            if (held == null || note.EndTime < held.EndTime)
                held = note;
        }

        if (held != null && holds.Release(noteSlots[held], new BmsReleaseContext(inputTime, frame, reverseScratch)))
            noteOutput.PlayTailSample(held);
    }

    internal void UpdateRegisteredLongNote(BmsLongNote note, in BmsFrameContext frame, double elapsed, bool holding)
        => holds.Finish(noteSlots[note], frame, elapsed, holding);

    internal void ObserveSimulationStep(BmsHitObject note, in BmsFrameContext frame) => noteOutput.ObserveSimulationStep(note, frame);

    internal void UpdateRegisteredBodies(in BmsFrameContext frame, double elapsed, bool holding)
    {
        // A completed hold leaves the active schedule before the next batch's body observations.
        for (var i = Notes.ActiveLongNotes.Count - 1; i >= 0; i--)
            RetireInactiveLongNoteAt(i);
        for (var i = 0; i < Notes.ActiveBodies.Count;)
        {
            var slot = Notes.ActiveBodies[i];
            holds.UpdateBody(slot, frame, elapsed, holding);
            // Ending a body removes only that member while preserving the next object's turn.
            if (i < Notes.ActiveBodies.Count && Notes.ActiveBodies[i] == slot)
                i++;
        }
    }


    internal int GetNoteSlot(BmsHitObject note) => noteSlots[note];

    internal BmsLandmineState GetLandmineState(BmsLandmine note) => landmines.GetLandmineState(note);

    internal void ActivateLandmine(BmsLandmine note) => landmines.ActivateLandmine(note);

    internal void RemoveLandmine(BmsLandmine note) => landmines.RemoveLandmine(note);

    internal bool IsLandmineActive(BmsLandmine note) => landmines.IsLandmineActive(note);

    internal void SetLandmineActivationOrder(IReadOnlyList<BmsLandmine> order) => landmines.SetLandmineActivationOrder(order);

    internal void UpdateLandmines(in BmsFrameContext frame, bool holding) => landmines.UpdateLandmines(frame, holding);

    internal void RewindLandmines(double time) => landmines.RewindLandmines(time);
}
