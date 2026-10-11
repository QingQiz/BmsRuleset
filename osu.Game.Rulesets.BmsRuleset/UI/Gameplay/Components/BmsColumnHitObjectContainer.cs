using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
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
    private readonly bool canBatchUpdates;
    private bool frameOpen;
    private bool hasUpdated;
    private bool updatePending;
    private double lastFullUpdateTime;

    private double latestLifetimeStart = double.PositiveInfinity;

    // The alive map reuses removed dictionary slots. A registration is append-only when
    // it raises the largest alive-map count observed by this container; otherwise its
    // slot may precede an existing entry and the judgement order must be rebuilt.
    private int maximumAliveEntryCount;
    private long aliveEntriesVersion;
    private bool coreOrderDirty = true;
    private readonly BmsScheduledOrder coreLayoutOrder = new();
    private readonly BmsScheduledOrder coreSubtreeOrder = new();
    private readonly Dictionary<DrawableBmsHitObject, Action<ValueChangedEvent<double>>> coreStartTimes = [];

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
        var aliveCount = AliveEntries.Count;
        var appended = aliveCount > maximumAliveEntryCount;
        if (appended)
            maximumAliveEntryCount = aliveCount;
        var version = ++aliveEntriesVersion;

        base.AddDrawable(entry, drawable);
        if (drawable is not DrawableBmsHitObject note)
            return;

        // Usage callbacks can mutate the container before registration finishes.
        JudgementBridge.Register(note, appended && version == aliveEntriesVersion);
        InvalidateCoreOrder();
    }

    protected override void RemoveDrawable(HitObjectLifetimeEntry entry, DrawableHitObject drawable)
    {
        aliveEntriesVersion++;
        InvalidateCoreOrder();
        if (drawable is DrawableBmsHitObject old && coreStartTimes.Remove(old, out var startTimeChanged))
            old.StartTimeBindable.ValueChanged -= startTimeChanged;
        if (drawable is DrawableBmsHitObject unregisteredNote)
            JudgementBridge.Unregister(unregisteredNote);

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

        updateVisuals(visualFrame());
    }

    private void updateVisuals(BmsColumnVisualFrame frame)
    {

        refreshCoreOrder();

        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var hitObject = note.HitObject!;
            var startTime = scrollController.ConstantScrollActive || scrollController.SoftConstantActive ? hitObject.StartTime : 0;
            note.UpdateSoftConstantVisibility(scrollController.SoftConstantActive ? scrollController.GetSoftConstantAlpha(startTime) : 1);
            var startPosition = scrollController.ConstantScrollActive ? startTime : hitObject.ScrollPositionAtStartTime;
            note.UpdateVisualFrame(frame.YAt(startPosition), frame);
        }

        // Geometry observes the pre-passive state; result feedback can then retire or recolour that presentation.
        var frameContext = coreFrame();
        var elapsed = Time.Elapsed;
        var rewinding = isResumeRewinding();
        Judgements.Schedule.RunLayout(coreLayoutOrder, frameContext, elapsed, rewinding);
        Judgements.Schedule.RunSubtree(coreSubtreeOrder, frameContext);
    }

    internal void InvalidateCoreOrder()
    {
        coreOrderDirty = true;
        // Until the existing lifetime-order refresh observes all current bindings,
        // keep the original draw path rather than snapshotting a stale static prefix.
        spriteBatchHasMultiplePieces = true;
        Judgements?.Schedule.InvalidateOrder();
    }

    private BmsColumnVisualFrame visualFrame()
    {
        var hitTarget = getHitTargetPosition();
        var height = DrawHeight;
        var scale = scrollController.ScrollCoordinateScale(height, hitTarget);
        return new BmsColumnVisualFrame(scrollController.CurrentScrollPosition, scale, hitTarget, height);
    }

    private BmsFrameContext coreFrame()
        => new(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate, (Clock as IGameplayClock)?.IsRewinding == true);

    private void refreshCoreOrder()
    {
        if (!coreOrderDirty) return;

        coreLayoutOrder.Clear();
        coreSubtreeOrder.Clear();
        spriteBatchHasMultiplePieces = false;
        foreach (var drawable in AliveEntries.Values)
        {
            if (drawable is not DrawableBmsHitObject note)
            {
                spriteBatchHasMultiplePieces = true;
                continue;
            }

            // These representations cannot supply a single Sprite. Reuse this
            // lifecycle traversal so mixed columns never prepare a doomed prefix.
            if (note.HitObject is BmsLongNote or BmsInvisibleNote)
                spriteBatchHasMultiplePieces = true;
            if (!JudgementBridge.TryGetScheduledObject(note, out var item)) continue;

            if (!coreStartTimes.ContainsKey(note))
                observeStartTime(note);
            // Only long notes have a post-layout operation; keep tap-heavy frames out of the layout plan.
            if (item.Object is BmsLongNote)
                coreLayoutOrder.Add(item);
        }

        // The framework already owns the exact subtree order, including equal-time child IDs.
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            if (InternalChildren[i] is DrawableBmsHitObject note && JudgementBridge.TryGetScheduledObject(note, out var item))
                coreSubtreeOrder.Add(item);
        }

        coreOrderDirty = false;
    }

    private void observeStartTime(DrawableBmsHitObject note)
    {
        // Keep the capture at the binding boundary; rebuilding order must not allocate for existing bindings.
        Action<ValueChangedEvent<double>> changed = _ =>
        {
            Judgements.RefreshNoteTiming(note.HitObject);
            InvalidateCoreOrder();
        };
        note.StartTimeBindable.ValueChanged += changed;
        coreStartTimes.Add(note, changed);
    }

    protected override void Dispose(bool isDisposing)
    {
        foreach (var (note, changed) in coreStartTimes) note.StartTimeBindable.ValueChanged -= changed;
        coreStartTimes.Clear();
        foreach (var (drawable, observation) in visualObservations)
            drawable.ClearVisualTimeObserver(observation.Generation);
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
