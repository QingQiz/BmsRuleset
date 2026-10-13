using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public abstract partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{

    public override bool IsPresent => !VisualsSuppressed && base.IsPresent;

    internal bool VisualsSuppressed { get; private set; }

    internal long BindingGeneration { get; private set; }

    private BmsVisualTimeObserver? visualTimeObserver;
    private BmsHitObject? observedVisualNote;
    private long visualTimeGeneration;

    internal bool HasVisualTimeObserver => visualTimeObserver != null;

    protected private bool IsCurrentVisualTimeObserver(BmsVisualTimeObserver observer)
        => visualTimeObserver == observer && visualTimeGeneration == BindingGeneration && ReferenceEquals(HitObject, observedVisualNote);

    internal void SetVisualTimeObserver(BmsVisualTimeObserver observer)
    {
        visualTimeObserver = observer;
        observedVisualNote = HitObject;
        visualTimeGeneration = BindingGeneration;
    }

    internal void ClearVisualTimeObserver(long generation)
    {
        if (visualTimeGeneration != generation)
            return;

        visualTimeObserver = null;
        observedVisualNote = null;
    }

    internal void ObserveVisualTime(float y, in BmsColumnVisualFrame frame)
    {
        var observer = visualTimeObserver;
        if (observer != null && visualTimeGeneration == BindingGeneration && ReferenceEquals(HitObject, observedVisualNote))
            observer(y, frame);
    }

    internal virtual void UpdateVisualFrame(float y, in BmsColumnVisualFrame frame)
    {
        if (HasVisualTimeObserver)
            ObserveVisualTime(y, frame);
        UpdateVisualLayout(y, frame);
    }

    internal void UpdateVisualLayout(float y, in BmsColumnVisualFrame frame)
        => ApplyVisualLayout(frame, PrepareVisual(y, frame));

    protected private void ApplyVisualLayout(in BmsColumnVisualFrame frame, in BmsVisualLayout layout)
    {
        var suppress = SkipFurtherUpdates;
        // The padding is at least one column height. Intersecting that minimum range
        // cannot be culled, so only outlying bounds need the skin's size-dependent height.
        if (!suppress && (layout.MaximumY < -frame.Height - frame.Height || layout.MinimumY > frame.Height))
        {
            var padding = Math.Max(frame.Height, VisualHeight);
            suppress = layout.MaximumY < -frame.Height - padding || layout.MinimumY > padding;
        }

        if (VisualsSuppressed != suppress)
        {
            VisualsSuppressed = suppress;
            Invalidate(Invalidation.Presence);
        }

        if (!suppress) ApplyVisual(frame, layout);
    }

    protected private virtual BmsVisualLayout PrepareVisual(float y, in BmsColumnVisualFrame frame) => new(y, y, y);

    protected private virtual void ApplyVisual(in BmsColumnVisualFrame frame, in BmsVisualLayout layout)
    {
        ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
        Y = layout.OriginY;
    }

    protected virtual float VisualHeight => 0;

    protected virtual bool SkipFurtherUpdates => false;

    internal virtual void RestoreRewoundVisuals()
    {
    }

    protected BmsLayoutVariant LayoutVariant => ParentColumn?.LayoutVariant ?? HitObject?.Beatmap.LayoutVariant ?? BmsLayoutVariant.Bme7K;

    protected double ScrollSpeedMultiplier => ParentColumn?.ScrollSpeedMultiplier ?? 1;

    // A separate visual layer lets appearance fades compose with hit/miss transforms
    // and long-note release opacity without changing judgement updates.
    private Drawable visualLayer;

    protected Container Visuals => (Container)visualLayer;

    protected void UseDirectVisual(Drawable visual)
    {
        // A single visual can compose the visibility fade independently of hit/miss transforms.
        // Removing the empty layer avoids another traversal and invalidation per scrolling note.
        RemoveInternal(visualLayer, true);
        AddInternal(visualLayer = visual);
        if (visual is BmsNoteVisual noteVisual)
        {
            singlePieceVisual = noteVisual;
            noteVisual.ComponentChanging += restoreSinglePieceWrapper;
            noteVisual.ComponentChanged += refreshSinglePieceVisual;
            noteVisual.ComponentInvalidated += restoreSinglePieceWrapper;
        }
    }

    private float appliedSoftConstantAlpha = float.NaN;

    internal void UpdateSoftConstantVisibility(float alpha)
    {
        // Alpha invalidation propagates through the whole visual tree; skip it when a
        // non-fading soft-constant state leaves this note unchanged.
        if (alpha == appliedSoftConstantAlpha)
            return;

        appliedSoftConstantAlpha = alpha;
        visualLayer.Alpha = alpha;
        refreshSinglePieceVisual();
    }

    private float appliedNoteHeightScale = float.NaN;

    [Resolved(CanBeNull = true)]
    protected BmsColumn? ParentColumn { get; private set; }

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
        AddInternal(visualLayer = new VisualContainer { RelativeSizeAxes = Axes.X });
    }

    internal void ApplyBmsResult(HitResult result, bool suppressPenalty)
    {
        ((BmsJudgementResult)Result).SuppressPenalty = suppressPenalty;
        ApplyResult(result);
    }

    public override void PlaySamples()
    {
    }

    public override void OnKilled()
    {
        base.OnKilled();
        // The framework has stopped samples, while this removing representation can still submit its final result.
        ParentColumn?.JudgementBridge.CompleteScheduledRemoval(this,
            new BmsFrameContext(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate,
                (Clock as IGameplayClock)?.IsRewinding == true));
    }

    protected override void Update()
    {
        base.Update();

        ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
        refreshSinglePieceVisual();
    }

    protected override void UpdateAfterChildren()
    {
        // The column schedule owns passive results; the base hook only dispatches an unused per-object check.
    }

    protected override void LoadAsyncComplete()
    {
        base.LoadAsyncComplete();
        // All BMS voices use the shared PCM mixer. Activating the framework's empty per-note
        // sound binds thousands of unused audio adjustments whenever a dense pool is populated.
        Samples.LifetimeStart = double.PositiveInfinity;
        Samples.LifetimeEnd = double.PositiveInfinity;
    }

    // BMS objects appear immediately; a zero-duration fade only adds transform tracking and cleanup.
    protected override void UpdateInitialTransforms() => Alpha = 1;

    internal void ApplyNoteHeightScale(float scale)
    {
        if (scale == appliedNoteHeightScale)
            return;

        appliedNoteHeightScale = scale;
        ApplyVisualHeightScale(scale);
        refreshSinglePieceVisual();
    }

    protected virtual void ApplyVisualHeightScale(float scale)
    {
    }

    protected virtual void ResetVisualState()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();
        BindingGeneration++;
        if (VisualsSuppressed)
        {
            VisualsSuppressed = false;
            Invalidate(Invalidation.Presence);
        }

        Alpha = 1;
        appliedSoftConstantAlpha = float.NaN;
        visualLayer.Alpha = 1;
        ResetVisualState();
    }

    protected override void OnFree()
    {
        // ApplyDefaults also frees the representation; its logical registration stays in place.
        ParentColumn?.JudgementBridge.UnbindNoteRepresentation(this, BindingGeneration);
        base.OnFree();
    }

    protected override JudgementResult CreateResult(osu.Game.Rulesets.Judgements.Judgement judgement) => new BmsJudgementResult(HitObject, judgement);

    protected override void LoadSamples()
    {
    }

    private sealed partial class VisualContainer : Container
    {
        // The framework's reflection fallback misses the inherited protected draw-node factory.
        protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;
    }
}

public abstract partial class DrawableBmsHitObject<TCol> : DrawableBmsHitObject
    where TCol : struct, IColumnProvider
{
    protected int Column { get; } = default(TCol).Value;
}
