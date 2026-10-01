using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
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

    internal void UpdateVisualLayout(float y, in BmsColumnVisualFrame frame)
    {
        var layout = PrepareVisual(y, frame);
        // Oversized skin art needs more slack than a fixed number of screens.
        var padding = Math.Max(frame.Height, VisualHeight);
        var suppress = SkipFurtherUpdates || layout.MaximumY < -frame.Height - padding || layout.MinimumY > padding;
        if (VisualsSuppressed != suppress)
        {
            VisualsSuppressed = suppress;
            Invalidate(Invalidation.Presence);
        }

        if (!suppress) ApplyVisual(frame, layout);
    }

    private protected virtual BmsVisualLayout PrepareVisual(float y, in BmsColumnVisualFrame frame) => new(y, y, y);

    private protected virtual void ApplyVisual(in BmsColumnVisualFrame frame, in BmsVisualLayout layout)
    {
        ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
        Y = layout.OriginY;
    }

    protected virtual float VisualHeight => 0;

    protected virtual bool SkipFurtherUpdates => false;

    internal virtual void RestoreRewoundState()
    {
    }

    protected BmsLayoutVariant LayoutVariant => ParentColumn?.LayoutVariant ?? HitObject?.Beatmap.LayoutVariant ?? BmsLayoutVariant.Bme7K;

    protected double ScrollSpeedMultiplier => ParentColumn?.ScrollSpeedMultiplier ?? 1;

    // A separate visual layer lets appearance fades compose with hit/miss transforms
    // and long-note release opacity without changing judgement updates.
    protected Container Visuals { get; } = new() { RelativeSizeAxes = Axes.X };

    internal void UpdateSoftConstantVisibility(float alpha) => Visuals.Alpha = alpha;

    private float appliedNoteHeightScale = float.NaN;

    [Resolved(CanBeNull = true)]
    protected BmsColumn? ParentColumn { get; private set; }

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
        AddInternal(Visuals);
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
        ParentColumn?.JudgementOutput.CompleteScheduledRemoval(this,
            new BmsFrameContext(Time.Current, (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate,
                (Clock as IGameplayClock)?.IsRewinding == true));
    }

    protected override void Update()
    {
        base.Update();

        ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
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
    }

    protected virtual void ApplyVisualHeightScale(float scale)
    {
    }

    protected virtual void ResetVisualState()
    {
    }

    internal virtual void UpdateVisualState()
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
        Visuals.Alpha = 1;
        ResetVisualState();
    }

    protected override void OnFree()
    {
        // ApplyDefaults also frees the representation; its logical registration stays in place.
        ParentColumn?.JudgementOutput.UnbindNoteRepresentation(this, BindingGeneration);
        base.OnFree();
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new BmsJudgementResult(HitObject, judgement);

    protected override void LoadSamples()
    {
    }
}

public abstract partial class DrawableBmsHitObject<TCol> : DrawableBmsHitObject
    where TCol : struct, IColumnProvider
{
    protected int Column { get; } = default(TCol).Value;
}
