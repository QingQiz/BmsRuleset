using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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
    private readonly Dictionary<DrawableBmsHitObject, Bindable<double>> coreStartTimes = [];

    internal BmsColumnJudgementRuntime Judgements { get; set; } = null!;
    internal BmsJudgementBridge JudgementBridge { get; set; } = null!;

    public override void Add(HitObjectLifetimeEntry entry)
    {
        base.Add(entry);
        latestLifetimeStart = double.PositiveInfinity;
    }

    internal BmsColumnHitObjectContainer(
        BmsGameplayScrollController scrollController,
        Func<float> getHitTargetPosition,
        Func<bool> isResumeRewinding,
        bool canBatchUpdates = false)
    {
        this.scrollController = scrollController;
        this.getHitTargetPosition = getHitTargetPosition;
        this.isResumeRewinding = isResumeRewinding;
        this.canBatchUpdates = canBatchUpdates;
        RelativeSizeAxes = Axes.Both;
    }

    protected override void AddDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        base.AddDrawable(entry, drawable);
        if (drawable is not DrawableBmsHitObject note)
            return;

        JudgementBridge.Register(note);
        InvalidateCoreOrder();
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        InvalidateCoreOrder();
        if (drawable is DrawableBmsHitObject old && coreStartTimes.Remove(old, out var startTime))
            startTime.UnbindAll();
        if (drawable is DrawableBmsHitObject note)
            JudgementBridge.Unregister(note);

        base.RemoveDrawable(entry, drawable);
        if (drawable is DrawableBmsHitObject removed)
            JudgementBridge.UnbindRemovedNote(removed);
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
            UpdateDeferredFrame();
            return true;
        }

        var updated = base.UpdateSubTree();
        hasUpdated = true;
        updatePending = false;
        lastFullUpdateTime = now;
        Judgements.RefreshPassiveDeadline(now);

        return updated;
    }

    internal void UpdateDeferredFrame()
    {
        observeDeferredVisuals(visualFrame());
        Judgements.Schedule.RunDeferred(coreFrame(), Time.Elapsed);
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

        var frame = visualFrame();

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var hitObject = note.HitObject!;
            note.UpdateSoftConstantVisibility(scrollController.GetSoftConstantAlpha(hitObject.StartTime));
            var startPosition = scrollController.GetVisualScrollPosition(hitObject.StartTime, hitObject.ScrollPositionAtStartTime);
            var y = frame.YAt(startPosition);
            observeVisualTime(note, y, frame);
            note.UpdateVisualLayout(y, frame);
        }

        // Geometry observes the pre-passive state; result feedback can then retire or recolour that presentation.
        Judgements.Schedule.RunLayout(coreLayoutOrder, coreFrame(), Time.Elapsed, isResumeRewinding());
        Judgements.Schedule.RunSubtree(coreSubtreeOrder, coreFrame());
    }

    internal void InvalidateCoreOrder()
    {
        coreOrderDirty = true;
        Judgements?.Schedule.InvalidateOrder();
    }

    private BmsColumnVisualFrame visualFrame()
    {
        var hitTarget = getHitTargetPosition();
        var scale = scrollController.ScrollSpeedMultiplier / Math.Max(1.0, scrollController.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);
        return new BmsColumnVisualFrame(scrollController.CurrentScrollPosition, scale, hitTarget, DrawHeight);
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
            if (drawable is not DrawableBmsHitObject note || !JudgementBridge.TryGetScheduledObject(note, out var item)) continue;
            if (!coreStartTimes.ContainsKey(note))
            {
                var startTime = note.StartTimeBindable.GetBoundCopy();
                startTime.BindValueChanged(_ =>
                {
                    Judgements.RefreshNoteTiming(note.HitObject);
                    InvalidateCoreOrder();
                });
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
        visualObservations.Clear();
        base.Dispose(isDisposing);
    }

    protected override void Update()
    {
        base.Update();
        if (Time.Elapsed >= 0 || isResumeRewinding())
            return;

        JudgementBridge.RestoreRewoundState(coreFrame());
    }
}
