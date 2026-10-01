using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

/// <summary>
/// Owns one column's input history, candidate indexes and synchronous judgement dispatch.
/// </summary>
internal sealed partial class BmsColumnJudgementContext
{
    private readonly BmsPlayfield playfield = null!;
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;
    private readonly bool hasLongNotes;
    private readonly bool canBatchUpdates;
    private readonly List<(DrawableBmsHitObject Drawable, BmsJudgementCandidate Candidate)> pressCandidates = [];
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];
    private readonly SortedSet<(double Time, long Id, DrawableBmsHitObject Note)> pendingHeads = [];
    private readonly Dictionary<DrawableBmsHitObject, long> headIds = [];
    private readonly List<DrawableBmsHitObject> activeLongNotes = [];
    private readonly List<DrawableBmsHitObject> orderedActiveLongNotes = [];
    private long nextHeadId;
    private int pressedDirections;
    private bool passiveDeadlineDirty = true;
    private bool activeLongNoteOrderDirty;

    internal BmsColumnJudgementContext(BmsPlayfield playfield, BmsColumn column)
    {
        this.playfield = playfield;
        HellChargeTick = playfield.ApplyHellChargeTick;
        Column = column;
        Container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
        canBatchUpdates = column.CanBatchUpdates;
        columnNotes = playfield.Beatmap.HitObjects.Where(h => h.Column == Index && h is not BmsLandmine and not BmsInvisibleNote)
                              .OrderBy(h => h.StartTime).ToArray();
        hasLongNotes = playfield.Beatmap.HitObjects.Any(h => h.Column == Index && h is BmsLongNote);
    }

    internal BmsColumn Column { get; } = null!;
    internal BmsColumnHitObjectContainer Container { get; } = null!;
    internal int Index => Column?.Index ?? noteColumnIndex;
    private bool IsScratch => Column?.IsScratch ?? BmsLayout.IsScratchColumn(Index, LayoutVariant);
    private BmsLayoutVariant LayoutVariant => Column?.LayoutVariant ?? noteLayout;
    internal IReadOnlyList<DrawableBmsHitObject> ActiveLongNotes => activeLongNotes;
    internal double NextPassiveUpdateTime { get; private set; }

    internal bool HasPmsMistake(BmsHitObject note) => BmsJudgementProfileProvider.IsPms(LayoutVariant) && pressHistory.Get(note).Mistake;

    internal void RecordResult(BmsHitObject note, double time) => pressHistory.Record(note, time, true);

    internal void RewindPressHistory(double time) => pressHistory.Rewind(time);

    internal bool TryPressNote(DrawableBmsHitObject note, in BmsNoteJudgementWindows rule, HitResult result)
    {
        if (noteOutput != null)
            return TryPressRegisteredNote(note.HitObject, result, new BmsPressContext(note.Time.Current, noteFrame(note)));

        var judgement = BmsNoteJudgementRule.Shared.JudgePress(result, note.Judged);
        if (judgement == HitResult.None)
            return false;

        note.ApplyBmsResult(judgement);
        return true;
    }

    internal void CheckPassiveNote(DrawableBmsHitObject note, in BmsNoteJudgementWindows rule)
    {
        if ((note.Clock as IGameplayClock)?.IsRewinding == true || note.HitObject == null)
            return;

        if (noteOutput != null)
        {
            CheckRegisteredNotePassive(note.HitObject, noteFrame(note));
            return;
        }

        var judgement = BmsNoteJudgementRule.Shared.JudgePassive(note.HitObject, rule, note.Time.Current, note.Judged);
        if (judgement != HitResult.None)
            note.ApplyBmsResult(judgement);
    }

    internal void RefreshPassiveDeadline(double now)
    {
        if (noteOutput != null)
        {
            refreshNoteDeadline(now);
            return;
        }

        // Removal only makes this bound conservative; activation and rewind can move it earlier.
        if (!canBatchUpdates || (!passiveDeadlineDirty && now <= NextPassiveUpdateTime))
            return;

        passiveDeadlineDirty = false;
        NextPassiveUpdateTime = double.PositiveInfinity;
        foreach (var drawable in Container.AliveEntries.Values)
        {
            if (drawable is DrawableBmsHitObject note && note.HasPendingHead)
                NextPassiveUpdateTime = Math.Min(NextPassiveUpdateTime, note.NextPassiveJudgementTime);
        }
    }

    internal void RetireInactiveLongNoteAt(int index)
    {
        if (activeLongNotes[index].RequiresActiveLongNoteUpdate)
            return;

        activeLongNotes.RemoveAt(index);
        activeLongNoteOrderDirty = true;
    }

    internal DrawableBmsHitObject? FirstPendingHead
    {
        get
        {
            if (drawableNoteOutput != null)
                return FirstPendingNote is { } note ? drawableNoteOutput.GetRepresentation(note) : null;

            if (!canBatchUpdates || pendingHeads.Count == 0)
                return null;

            var first = pendingHeads.Min.Note;
            // Normal LN heads remain uncommitted until release. An overlapping hold may still
            // win the legacy/Lowest selector, so keep its original slow-path selection semantics.
            foreach (var held in activeLongNotes)
            {
                if (!held.Judged && held.HitObject.StartTime <= first.HitObject.StartTime)
                    return null;
            }

            return first;
        }
    }

    internal void Register(DrawableBmsHitObject note)
    {
        if (noteOutput != null && noteSlots.ContainsKey(note.HitObject))
        {
            drawableNoteOutput!.Bind(note);
            ActivateNote(note.HitObject);
            bindScheduledRepresentation(note);
            refreshActiveNoteOrder();
            return;
        }

        // Holds also need an index in columns containing mines or invisible notes.
        if (note.HitObject is not BmsLongNote && (!canBatchUpdates || note.HitObject is not BmsNote))
            return;

        var id = ++nextHeadId;
        headIds.Add(note, id);
        if (canBatchUpdates && note.HasPendingHead)
            pendingHeads.Add((note.HitObject.StartTime, id, note));
        trackActiveLongNote(note);
        passiveDeadlineDirty = true;
        note.OnNewResult += removeJudgedHead;
        note.OnRevertResult += restoreHead;
    }

    internal void Unregister(DrawableBmsHitObject note)
    {
        if (noteOutput != null && noteSlots.ContainsKey(note.HitObject))
        {
            BeginNoteRemoval(note.HitObject);
            if (TryGetScheduledObject(note, out var scheduled)) Schedule!.BeginRemoval(scheduled);
            refreshActiveNoteOrder();
            return;
        }

        if (headIds.Remove(note, out var id))
        {
            pendingHeads.Remove((note.HitObject.StartTime, id, note));
            note.OnNewResult -= removeJudgedHead;
            note.OnRevertResult -= restoreHead;
            activeLongNoteOrderDirty |= activeLongNotes.Remove(note);
            orderedActiveLongNotes.Remove(note);
            // Selection buffers may outlive the input that filled them, but not a pool binding.
            for (var i = pressCandidates.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(pressCandidates[i].Drawable, note))
                    pressCandidates.RemoveAt(i);
            }
        }
    }

    private void removeJudgedHead(DrawableHitObject drawable, JudgementResult result)
    {
        var note = (DrawableBmsHitObject)drawable;
        // A captured multicast callback may run after an earlier observer removed or rebound the representation.
        if (!ReferenceEquals(note.HitObject, result.HitObject) || !headIds.TryGetValue(note, out var id))
            return;
        pendingHeads.Remove((result.HitObject.StartTime, id, note));
        trackActiveLongNote(note);
    }

    private void onHeadJudged(DrawableBmsHitObject note)
    {
        // OnKilled runs after index removal and can still judge an expired head.
        if (!headIds.TryGetValue(note, out var id))
            return;

        pendingHeads.Remove((note.HitObject.StartTime, id, note));
        trackActiveLongNote(note);
    }

    private void trackActiveLongNote(DrawableBmsHitObject note)
    {
        if (note.RequiresActiveLongNoteUpdate && !activeLongNotes.Contains(note))
        {
            activeLongNotes.Add(note);
            activeLongNoteOrderDirty = true;
        }
    }

    private void restoreHead(DrawableHitObject drawable, JudgementResult result)
    {
        var note = (DrawableBmsHitObject)drawable;
        if (canBatchUpdates && note.HasPendingHead)
            pendingHeads.Add((note.HitObject.StartTime, headIds[note], note));
        trackActiveLongNote(note);
        passiveDeadlineDirty = true;
    }

    internal void UpdateHellChargeBodies(bool holding)
    {
        if (noteOutput != null)
        {
            if (Schedule != null) pruneInactiveLongNotes();
            updateDrawableBodies(holding);
            return;
        }
        pruneInactiveLongNotes();
        foreach (var note in canBatchUpdates ? activeLongNotes : getOrderedActiveLongNotes())
            note.LongNoteJudgement!.UpdateHellChargeBody(this, note.Time.Current, note.Time.Elapsed,
                holding || ((ILongNoteHolder)note).IsAutomaticallyHeld);
    }

    internal void CompleteLongNotesAtPause(double currentTime)
    {
        if (noteOutput != null)
        {
            foreach (var note in GetHoldingNotes().ToArray())
                CompleteLongNoteAtPause(drawableNoteOutput!.GetRepresentation(note), currentTime);
            return;
        }
        pruneInactiveLongNotes();

        // Resume rewind keeps completed results authoritative. Finalising active holds here
        // means the rewind does not need to reconstruct an intermediate LN controller state.
        foreach (var note in getOrderedActiveLongNotes())
        {
            CompleteLongNoteAtPause(note, currentTime);
        }
    }

    internal DrawableBmsHitObject? TryRepress(double time, bool reverseScratch)
    {
        if (noteOutput != null)
        {
            foreach (var slot in pendingLongNoteTailSlots)
            {
                var note = LongNoteRegistrations[slot].Object;
                var drawable = drawableNoteOutput!.GetRepresentation(note);
                if (longNoteBinding!.Repress(slot, new BmsPressContext(time, noteFrame(drawable), reverseScratch)))
                    return drawable;
            }
            return null;
        }
        pruneInactiveLongNotes();
        foreach (var note in getOrderedActiveLongNotes())
        {
            if (TryRepressLongNote(note, time, reverseScratch))
                return note;
        }

        return null;
    }

    private void pruneInactiveLongNotes()
    {
        for (var i = activeLongNotes.Count - 1; i >= 0; i--)
        {
            if (activeLongNotes[i].RequiresActiveLongNoteUpdate)
                continue;

            activeLongNotes.RemoveAt(i);
            activeLongNoteOrderDirty = true;
        }
    }

    private List<DrawableBmsHitObject> getOrderedActiveLongNotes()
    {
        if (!activeLongNoteOrderDirty)
            return orderedActiveLongNotes;

        orderedActiveLongNotes.Clear();
        if (activeLongNotes.Count <= 1)
            orderedActiveLongNotes.AddRange(activeLongNotes);
        else
        {
            // Overlapping holds must keep the same input winner as the former alive scan.
            // Rebuild on activation/retirement/rewind, rather than on every key or frame.
            foreach (var drawable in Container.AliveEntries.Values)
            {
                if (drawable is DrawableBmsHitObject { RequiresActiveLongNoteUpdate: true } note)
                    orderedActiveLongNotes.Add(note);
            }
        }
        activeLongNoteOrderDirty = false;
        return orderedActiveLongNotes;
    }

    internal void RebuildRewoundIndexes()
    {
        if (noteOutput != null)
        {
            RestoreExecutionState(new BmsFrameContext(Column.Time.Current, (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate));
            return;
        }

        // Normal LN heads are not committed results until their tails. Rewind can undo a head
        // without OnRevertResult, so rebuild the input/hold indexes from the restored controllers.
        pendingHeads.Clear();
        activeLongNotes.Clear();
        activeLongNoteOrderDirty = true;
        foreach (var (note, id) in headIds)
        {
            if (canBatchUpdates && note.HasPendingHead)
                pendingHeads.Add((note.HitObject.StartTime, id, note));
            trackActiveLongNote(note);
        }

        passiveDeadlineDirty = true;
    }

    internal int PressedDirections
    {
        get => pressedDirections;
        set => pressedDirections = value;
    }

    internal bool LastPressWasReverseScratch { get; private set; }

    internal PressOutcome HandlePress(double time, bool reverseScratch = false)
    {
        if (noteOutput != null)
            return HandleNotePress(new BmsPressContext(time,
                new BmsFrameContext(Column.Time.Current, (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate,
                    (Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0), reverseScratch, playfield.IsResumeRewinding),
                playfield.JudgementAlgorithm);

        LastPressWasReverseScratch = IsScratch && reverseScratch;
        pressedDirections |= LastPressWasReverseScratch ? 2 : 1;

        // Replay input also changes while moving backwards; retain key state without judging it.
        if ((Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0)
            return PressOutcome.Empty;

        if (hasLongNotes && TryRepress(time, LastPressWasReverseScratch) is { } repressed)
        {
            if (repressed is ILongNoteHolder { IsHoldingLongNote: false } && repressed.HitObject is BmsLongNote held)
                Column.PlaySample(held.TailSampleKey, held.TailSampleVolume);
            return PressOutcome.Hit;
        }

        // The earliest pending exact-time head wins every judgement algorithm. The ordered
        // live index avoids sorting thousands of future candidates for every replay press.
        if (FirstPendingHead is { } first
            && first.HitObject.StartTime == time && first.HasPendingHead
            && BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, first.HitObject.EffectiveJudgementRate, false).ResultForOffset(0) == HitResult.Perfect
            && first.TryHit(HitResult.Perfect))
        {
            pressHistory.Record(first.HitObject, time, true);
            Column.PlaySample(first.HitObject.SampleKey, first.HitObject.SampleVolume);
            return PressOutcome.Hit;
        }

        pressCandidates.Clear();
        pressJudgementCandidates.Clear();

        foreach (var alive in Container.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d
                || d.Judged
                || d.HitObject is BmsLandmine or BmsInvisibleNote)
            {
                continue;
            }

            var candidate = new BmsJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.GetEndTime(),
                d.HitObject.Column,
                d.HitObject.EffectiveJudgementRate,
                d.HitObject is BmsLongNote,
                !d.HasPendingHead,
                pressHistory.Get(d.HitObject).Mistake);
            pressCandidates.Add((d, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        // Consumed notes may already be pooled. Search the chart's fixed empty-POOR interval
        // rather than retaining drawables or scanning the complete judgement history.
        var low = 0;
        var high = columnNotes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (columnNotes[middle].StartTime < time - 175)
                low = middle + 1;
            else
                high = middle;
        }

        for (var i = low; i < columnNotes.Length && columnNotes[i].StartTime <= time + 500; i++)
        {
            var note = columnNotes[i];
            var state = pressHistory.Get(note);
            if (state.Judged)
                pressJudgementCandidates.Add(new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column,
                    note.EffectiveJudgementRate, note is BmsLongNote, true, state.Mistake));
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, time, playfield.JudgementAlgorithm);

        if (!selection.IsEmptyPoor && selection.Candidate is { } selectedCandidate)
        {
            DrawableBmsHitObject? target = null;
            foreach (var candidate in pressCandidates)
            {
                if (candidate.Candidate.Equals(selectedCandidate))
                {
                    target = candidate.Drawable;
                    break;
                }
            }

            if (target == null)
                return PressOutcome.Empty;

            if (selection.Result == HitResult.Ok && BmsJudgementProfileProvider.IsPms(LayoutVariant))
            {
                pressHistory.Record(target.HitObject, time, false, true);
                playfield.RegisterNonConsumingBad(target.HitObject.StartTime, Index);
                Column.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);
                return PressOutcome.Hit;
            }

            if (target.TryHit(selection.Result))
            {
                pressHistory.Record(target.HitObject, time, true);
                Column.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);
                return PressOutcome.Hit;
            }
        }

        Column.PlayKeySound();

        if (selection is { IsEmptyPoor: true, Candidate: { } emptyPoorCandidate })
        {
            // Lead-in key presses are preparation input, including for PMS mistake tracking.
            if (playfield.IsResumeRewinding)
                return PressOutcome.Empty;

            foreach (var candidate in pressCandidates)
            {
                if (candidate.Candidate.Equals(emptyPoorCandidate))
                    pressHistory.Record(candidate.Drawable.HitObject, time, false, true);
            }

            return PressOutcome.ForEmptyPoor(emptyPoorCandidate.StartTime, emptyPoorCandidate.Column);
        }

        return PressOutcome.Empty;
    }

    internal void HandleRelease(double time, bool reverseScratch = false)
    {
        if (noteOutput != null)
        {
            HandleExecutionRelease(time, new BmsFrameContext(Column.Time.Current,
                (Column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Column.Clock.Rate,
                (Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0), reverseScratch);
            return;
        }
        var direction = IsScratch && reverseScratch ? 2 : 1;
        if ((pressedDirections & direction) == 0)
            return;

        pressedDirections &= ~direction;

        if ((Column.Clock as IGameplayClock)?.IsRewinding == true || Column.Time.Elapsed < 0)
            return;

        if (!hasLongNotes)
            return;

        // Release: find the earliest held LN in this column and let it judge the key-up.
        // We must include LNs released before the tail window (a fast release is a drop,
        // scored as POOR) — filtering by the release window here would leave the note
        // frozen at the judgement line until its tail time passed.
        DrawableBmsHitObject? heldNote = null;

        foreach (var alive in Container.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d) continue;
            if (d.LongNoteJudgement?.IsHolding != true)
                continue;

            if (heldNote == null || d.HitObject.GetEndTime() < heldNote.HitObject.GetEndTime())
                heldNote = d;
        }

        if (heldNote != null)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, heldNote.HitObject.EffectiveJudgementRate, tail: true);
            var releaseOffset = time - heldNote.HitObject.GetEndTime();

            if (TryReleaseLongNote(heldNote, releaseOffset, tailTable, reverseScratch) && heldNote.HitObject is BmsLongNote ln)
                Column.PlaySample(ln.TailSampleKey, ln.TailSampleVolume);
        }
    }

}
