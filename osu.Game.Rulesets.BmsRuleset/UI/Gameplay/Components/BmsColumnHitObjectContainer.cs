using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{
    internal const double MAX_DEFERRED_UPDATE_TIME = 16;
    private readonly BmsGameplayScrollController scrollController;
    private readonly Func<float> getHitTargetPosition;
    private readonly Func<bool> isResumeRewinding;
    private readonly Func<double> getResumeRewindEndTime;
    private readonly bool canBatchUpdates;
    private bool frameOpen;
    private bool hasUpdated;
    private bool updatePending;
    private double lastFullUpdateTime;
    private double latestLifetimeStart = double.PositiveInfinity;
    private bool coreOrderDirty = true;
    private readonly List<BmsScheduledObject> coreLayoutOrder = [];
    private readonly List<BmsScheduledObject> coreSubtreeOrder = [];
    private readonly List<(int Position, BmsScheduledObject Object)> corePositions = [];
    private readonly List<BmsScheduledObject> coreDeferredOrder = [];
    private readonly Dictionary<DrawableBmsHitObject, Bindable<double>> coreStartTimes = [];

    internal BmsColumnJudgementContext Judgements { get; set; } = null!;

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
        bool canBatchUpdates = false)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        this.isResumeRewinding = isResumeRewinding;
        this.getResumeRewindEndTime = getResumeRewindEndTime;
        this.canBatchUpdates = canBatchUpdates;
        RelativeSizeAxes = Axes.Both;
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);
        if (drawable is not DrawableBmsHitObject note)
            return;

        Judgements.Register(note);
        InvalidateCoreOrder();
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        InvalidateCoreOrder();
        if (drawable is DrawableBmsHitObject old && coreStartTimes.Remove(old, out var startTime))
            startTime.UnbindAll();
        if (drawable is DrawableBmsHitObject note)
            Judgements.Unregister(note);

        base.RemoveDrawable(entry, drawable);
        if (drawable is DrawableBmsHitObject removed)
            Judgements.UnbindRemovedNote(removed);
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

    internal bool CanDeferUpdate => canBatchUpdates && frameOpen && hasUpdated && Time.Elapsed >= 0
                                    && (Clock as IGameplayClock)?.IsRewinding != true && !isResumeRewinding()
                                    && Time.Current >= lastFullUpdateTime
                                    && (Time.Current - lastFullUpdateTime < MAX_DEFERRED_UPDATE_TIME || lastFullUpdateTime >= latestLifetimeStart)
                                    && Time.Current <= Judgements.NextPassiveUpdateTime;

    public override bool UpdateSubTree()
    {
        var now = Time.Current;
        // Replay input and active holds still advance at every timestamp. Between unjudged-head
        // POOR deadlines, the remaining skin trees only need the final frame's update.
        // Lifetimes include this deferral budget so even the earliest E-POOR input finds its note.
        if (CanDeferUpdate)
        {
            updatePending = true;
            UpdateDeferredLongNotes();
            return true;
        }

        var updated = base.UpdateSubTree();
        hasUpdated = true;
        updatePending = false;
        lastFullUpdateTime = now;
        Judgements.RefreshPassiveDeadline(now);

        return updated;
    }

    internal void UpdateDeferredLongNotes()
    {
        var activeLongNotes = Judgements.ActiveLongNotes;
        if (activeLongNotes.Count == 0)
            return;

        coreDeferredOrder.Clear();
        for (var i = 0; i < activeLongNotes.Count; i++)
        {
            var note = activeLongNotes[i];
            if (Judgements.RequiresActiveLongNoteUpdate(note) && Judgements.Schedule.TryGetCurrent(note, out var item))
                coreDeferredOrder.Add(item);
        }
        Judgements.Schedule.RunDeferred(coreDeferredOrder, coreFrame(), Time.Elapsed);

        var hitTarget = getHitTargetPosition();
        var scale = scrollController.ScrollSpeedMultiplier / Math.Max(1.0, scrollController.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);
        var currentScrollPos = scrollController.CurrentScrollPosition;
        for (var i = activeLongNotes.Count - 1; i >= 0; i--)
        {
            var note = activeLongNotes[i];
            if (Judgements.RequiresActiveLongNoteUpdate(note))
            {
                var ln = note;
                var startPosition = scrollController.GetVisualScrollPosition(ln.StartTime, ln.ScrollPositionAtStartTime);
                var endPosition = double.IsNaN(ln.VisualScrollPositionAtEndTime) ? ln.ScrollPositionAtEndTime : ln.VisualScrollPositionAtEndTime;
                Judgements.GetRepresentation(note).UpdateDeferredVisuals(-(hitTarget + (float)((startPosition - currentScrollPos) * scale)),
                    -(hitTarget + (float)((endPosition - currentScrollPos) * scale)));
            }

            Judgements.RetireInactiveLongNoteAt(i);
        }
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

        refreshCoreOrder();
        Judgements.Schedule.RunLayout(coreLayoutOrder, coreFrame(), Time.Elapsed, isResumeRewinding());

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
            note.UpdateSoftConstantVisibility(scrollController.GetSoftConstantAlpha(hitObject.StartTime));
            var startPosition = scrollController.GetVisualScrollPosition(hitObject.StartTime, hitObject.ScrollPositionAtStartTime);
            var offset = (float)((startPosition - currentScrollPos) * scale);
            var y = -(hitTarget + offset);

            if (hitObject is BmsLongNote longNote)
            {
                var endPosition = double.IsNaN(longNote.VisualScrollPositionAtEndTime)
                    ? longNote.ScrollPositionAtEndTime
                    : longNote.VisualScrollPositionAtEndTime;
                var endOffset = (float)((endPosition - currentScrollPos) * scale);
                var endY = -(hitTarget + endOffset);
                note.UpdateVisualPosition(y, DrawHeight, endY);
                note.UpdateVisualGeometry(y, endY);
            }
            else
                note.UpdateVisualPosition(y, DrawHeight);

            // Resume rewind retains the completed presentation while the clock moves backwards.
            if (!resumeRewinding && (Clock as IGameplayClock)?.IsRewinding != true && Time.Elapsed >= 0 && note.RequiresColumnFrameUpdate)
                note.UpdateColumnFrame();
        }

        Judgements.Schedule.RunSubtree(coreSubtreeOrder, coreFrame());
    }

    internal void InvalidateCoreOrder()
    {
        coreOrderDirty = true;
        Judgements?.Schedule.InvalidateOrder();
    }

    private BmsFrameContext coreFrame()
        => new(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate, (Clock as IGameplayClock)?.IsRewinding == true);

    private void refreshCoreOrder()
    {
        if (!coreOrderDirty) return;
        coreLayoutOrder.Clear();
        coreSubtreeOrder.Clear();
        corePositions.Clear();
        foreach (var drawable in AliveEntries.Values)
        {
            if (drawable is not DrawableBmsHitObject note || !Judgements.TryGetScheduledObject(note, out var item)) continue;
            if (!coreStartTimes.ContainsKey(note))
            {
                var startTime = note.StartTimeBindable.GetBoundCopy();
                startTime.BindValueChanged(_ => InvalidateCoreOrder());
                coreStartTimes.Add(note, startTime);
            }
            coreLayoutOrder.Add(item);
            corePositions.Add((IndexOf(note), item));
        }
        // Alive-map order differs from the native child order, including equal-time reverse child IDs.
        corePositions.Sort((left, right) => left.Position.CompareTo(right.Position));
        foreach (var position in corePositions) coreSubtreeOrder.Add(position.Object);
        coreOrderDirty = false;
    }

    protected override void Dispose(bool isDisposing)
    {
        foreach (var startTime in coreStartTimes.Values) startTime.UnbindAll();
        coreStartTimes.Clear();
        base.Dispose(isDisposing);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (!isResumeRewinding())
            return;

        var rewindEndTime = getResumeRewindEndTime();

        foreach (var note in AliveEntries.Values)
        {
            if (note.HitObject is not BmsLongNote longNote)
                continue;

            // EndTime covers HCN head-POOR, whose endpoints are scored synthetically
            // without applying a result to the parent drawable.
            if (longNote.EndTime <= rewindEndTime || (note.Judged && !Judgements.GetLongNoteState(longNote).IsHolding))
                note.Alpha = 0;
        }
    }

    protected override void Update()
    {
        base.Update();
        if (Time.Elapsed >= 0 || isResumeRewinding())
            return;

        if (!Judgements.Schedule.BeginRewindStep())
            return;

        Judgements.RewindLongNotes(Time.Current);

        Judgements.RewindLandmines(Time.Current);

        foreach (var note in AliveEntries.Values)
        {
            if (note is DrawableBmsHitObject bms)
                bms.RestoreRewoundState();
        }

        Judgements.RebuildRewoundIndexes();
    }
}
