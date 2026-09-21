using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Judgements;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{
    internal const double MAX_DEFERRED_UPDATE_TIME = 16;
    private readonly BmsGameplayScrollController scrollController;
    private readonly Func<float> getHitTargetPosition;
    private readonly Func<bool> isResumeRewinding;
    private readonly Func<double> getResumeRewindEndTime;
    private readonly bool canBatchTapUpdates;
    private bool frameOpen;
    private bool hasUpdated;
    private bool updatePending;
    private double lastFullUpdateTime;
    private double nextPassiveUpdateTime;
    private double latestLifetimeStart = double.PositiveInfinity;
    private bool passiveDeadlineDirty = true;
    private readonly SortedSet<(double Time, long Id, DrawableBmsHitObject Note)> pendingTaps = [];
    private readonly Dictionary<DrawableBmsHitObject, long> tapIds = [];
    private long nextTapId;

    internal DrawableBmsHitObject? FirstPendingTap => canBatchTapUpdates && pendingTaps.Count > 0 ? pendingTaps.Min.Note : null;

    public override void Add(HitObjectLifetimeEntry entry)
    {
        base.Add(entry);
        latestLifetimeStart = double.PositiveInfinity;
    }

    internal BmsColumnHitObjectContainer(
        BmsGameplayScrollController scrollController,
        Func<float> getHitTargetPosition,
        Func<bool> isResumeRewinding,
        Func<double> getResumeRewindEndTime,
        bool canBatchTapUpdates = false)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        this.isResumeRewinding = isResumeRewinding;
        this.getResumeRewindEndTime = getResumeRewindEndTime;
        this.canBatchTapUpdates = canBatchTapUpdates;
        RelativeSizeAxes = Axes.Both;
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);
        if (!canBatchTapUpdates || drawable is not DrawableBmsHitObject note || entry.HitObject is not BmsNote)
            return;

        var id = ++nextTapId;
        tapIds.Add(note, id);
        if (!note.Judged)
            pendingTaps.Add((note.HitObject.StartTime, id, note));
        passiveDeadlineDirty = true;
        note.OnNewResult += removeJudgedTap;
        note.OnRevertResult += restoreTap;
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        if (drawable is DrawableBmsHitObject note && tapIds.Remove(note, out var id))
        {
            pendingTaps.Remove((note.HitObject.StartTime, id, note));
            note.OnNewResult -= removeJudgedTap;
            note.OnRevertResult -= restoreTap;
        }

        base.RemoveDrawable(entry, drawable);
    }

    private void removeJudgedTap(DrawableHitObject drawable, JudgementResult result)
    {
        var note = (DrawableBmsHitObject)drawable;
        pendingTaps.Remove((note.HitObject.StartTime, tapIds[note], note));
    }

    private void restoreTap(DrawableHitObject drawable, JudgementResult result)
    {
        var note = (DrawableBmsHitObject)drawable;
        pendingTaps.Add((note.HitObject.StartTime, tapIds[note], note));
        passiveDeadlineDirty = true;
    }

    internal void BeginGameplayFrame()
    {
        frameOpen = true;
        updatePending = false;
    }

    internal void EndGameplayFrame(bool completePendingUpdate = true)
    {
        frameOpen = false;
        if (completePendingUpdate && updatePending)
            UpdateSubTree();
    }

    internal bool CanDeferUpdate => canBatchTapUpdates && frameOpen && hasUpdated && Time.Elapsed >= 0
                                    && (Clock as IGameplayClock)?.IsRewinding != true && !isResumeRewinding()
                                    && Time.Current >= lastFullUpdateTime
                                    && (Time.Current - lastFullUpdateTime < MAX_DEFERRED_UPDATE_TIME || lastFullUpdateTime >= latestLifetimeStart)
                                    && Time.Current <= nextPassiveUpdateTime;

    public override bool UpdateSubTree()
    {
        var now = Time.Current;
        // Replay input is still dispatched for every timestamp. Plain taps have no held state,
        // so between passive-POOR deadlines their skin trees only need the final frame's update.
        // Lifetimes include this deferral budget so even the earliest E-POOR input finds its note.
        if (CanDeferUpdate)
        {
            updatePending = true;
            return true;
        }

        var updated = base.UpdateSubTree();
        hasUpdated = true;
        updatePending = false;
        lastFullUpdateTime = now;
        // Removing/judging a note can only make this bound conservative. Rescan when the bound
        // expires or activation/rewind introduces an earlier candidate, not on every render update.
        if (canBatchTapUpdates && (passiveDeadlineDirty || now > nextPassiveUpdateTime))
        {
            passiveDeadlineDirty = false;
            nextPassiveUpdateTime = double.PositiveInfinity;
            foreach (var drawable in AliveEntries.Values)
            {
                if (drawable is DrawableBmsHitObject note && !note.Judged)
                    nextPassiveUpdateTime = Math.Min(nextPassiveUpdateTime, note.NextPassiveJudgementTime);
            }
        }

        return updated;
    }

    /// <summary>
    ///     Re-compute lifetimes for every entry in this container.
    ///     Triggered on load completion and whenever the user adjusts the scroll speed.
    /// </summary>
    public void RefreshAllEntries(double? currentTime = null)
    {
        latestLifetimeStart = double.NegativeInfinity;
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.RefreshLifetime(currentTime);
            // Once every candidate has been activated, no future input needs another lifetime
            // pass. Full skin traversal on each catch-up step can otherwise prevent recovery.
            latestLifetimeStart = Math.Max(latestLifetimeStart, entry.LifetimeStart);
        }
    }

    internal void ApplyVisualOffsetToAllEntries()
    {
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.ApplyVisualOffset();
        }
    }

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        var currentScrollPos = scrollController.CurrentScrollPosition;
        var hitTarget = getHitTargetPosition();
        var scale = scrollController.ScrollSpeedMultiplier / Math.Max(1.0, scrollController.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);
        var resumeRewinding = isResumeRewinding();

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var hitObject = note.HitObject!;
            var startPosition = scrollController.GetVisualScrollPosition(hitObject.StartTime, hitObject.ScrollPositionAtStartTime);
            var offset = (float)((startPosition - currentScrollPos) * scale);
            var y = -(hitTarget + offset);

            if (note is ILongNoteHolder ln && hitObject is BmsLongNote longNote)
            {
                var endPosition = double.IsNaN(longNote.VisualScrollPositionAtEndTime)
                    ? longNote.ScrollPositionAtEndTime
                    : longNote.VisualScrollPositionAtEndTime;
                var endOffset = (float)((endPosition - currentScrollPos) * scale);
                var endY = -(hitTarget + endOffset);
                note.UpdateVisualPosition(y, DrawHeight, endY);
                ln.UpdateBodyGeometry(y, endY);
            }
            else
                note.UpdateVisualPosition(y, DrawHeight);

            // Keeping judgement controllers frozen avoids replaying misses or resetting an active long note.
            if (!resumeRewinding && (Clock as IGameplayClock)?.IsRewinding != true && Time.Elapsed >= 0 && note.RequiresColumnFrameUpdate)
                note.UpdateColumnFrame();
        }
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (!isResumeRewinding())
            return;

        var rewindEndTime = getResumeRewindEndTime();

        foreach (var note in AliveEntries.Values)
        {
            if (note is not ILongNoteHolder longNoteHolder || note.HitObject is not BmsLongNote longNote)
                continue;

            // EndTime covers HCN head-POOR, whose endpoints are scored synthetically
            // without applying a result to the parent drawable.
            if (longNote.EndTime <= rewindEndTime || (note.Judged && !longNoteHolder.IsHoldingLongNote))
                note.Alpha = 0;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (Time.Elapsed >= 0 || isResumeRewinding())
            return;

        // Restore entries before pool activation, including controllers whose drawables were freed.
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry { LongNoteJudgementController: { } controller })
                controller.Rewind(Time.Current);
        }

        foreach (var note in AliveEntries.Values)
        {
            if (note is DrawableBmsHitObject bms)
                bms.RestoreRewoundState();
        }
    }
}
