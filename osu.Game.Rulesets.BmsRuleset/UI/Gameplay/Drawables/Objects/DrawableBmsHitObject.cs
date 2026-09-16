using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public abstract partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{
    private bool visualsSuppressed;

    public override bool IsPresent => !visualsSuppressed && base.IsPresent;

    internal bool VisualsSuppressed => visualsSuppressed;

    public override bool UpdateSubTree()
    {
        var updated = base.UpdateSubTree();
        // CompositeDrawable skips UpdateAfterChildren when culled. Passive judgements must
        // still run at the same point in the column traversal, rather than waiting for OnKilled.
        if (UsesPassiveResultCheck && visualsSuppressed && IsLoaded)
            UpdateResult(false);
        return updated;
    }

    internal void UpdateVisualPosition(float y, float columnHeight, float? endY = null)
    {
        var headY = GetVisualHeadY(y, endY ?? y);
        // The full head-to-tail interval includes long-note bodies spanning the viewport.
        // Oversized skin art needs more slack than a fixed number of screens.
        var padding = Math.Max(columnHeight, VisualHeight);
        var suppress = SkipFurtherUpdates || Math.Max(headY, endY ?? headY) < -columnHeight - padding
                                          || Math.Min(headY, endY ?? headY) > padding;
        if (visualsSuppressed != suppress)
        {
            visualsSuppressed = suppress;
            Invalidate(Invalidation.Presence);
        }

        if (!suppress)
        {
            ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
            Y = y;
        }
    }

    protected virtual float GetVisualHeadY(float y, float endY) => y;

    protected virtual float VisualHeight => 0;

    protected abstract BmsSkinComponents SkinComponent { get; }

    protected virtual bool SkipFurtherUpdates => false;

    protected virtual bool RequiresResultBeforeKindPostState => false;

    protected virtual bool UsesPassiveResultCheck => true;

    internal virtual bool RequiresColumnFrameUpdate => true;

    internal virtual void RestoreRewoundState()
    {
    }

    protected BmsLayoutVariant LayoutVariant => ParentColumn?.LayoutVariant ?? HitObject?.Beatmap.LayoutVariant ?? BmsLayoutVariant.Bme7K;

    protected double ScrollSpeedMultiplier => ParentColumn?.ScrollSpeedMultiplier ?? 1;

    protected Container NoteContainer = null!;

    private float appliedNoteHeightScale = float.NaN;

    [Resolved(CanBeNull = true)]
    protected BmsColumn? ParentColumn { get; private set; }

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
    }

    public virtual bool TryHit(HitResult result)
    {
        if (Judged || result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    internal void UpdateColumnFrame()
    {
        if (HitObject == null) return;

        if (!Judged && !SkipFurtherUpdates)
        {
            if (!UpdateKindState() && RequiresResultBeforeKindPostState)
                UpdateResult(false);
        }

        UpdateKindPostResultState();
    }

    protected override void Update()
    {
        base.Update();

        ApplyNoteHeightScale(ParentColumn?.NoteHeightScale ?? 1);
    }

    // BMS objects appear immediately; a zero-duration fade only adds transform tracking and cleanup.
    protected override void UpdateInitialTransforms() => Alpha = 1;

    internal void ApplyNoteHeightScale(float scale)
    {
        if (scale == appliedNoteHeightScale)
            return;

        appliedNoteHeightScale = scale;
        NoteContainer.Scale = new Vector2(1, scale);
        ApplyNoteHeightScaleToKind(scale);
    }

    protected virtual void ApplyNoteHeightScaleToKind(float scale)
    {
    }

    protected virtual void ResetKindState()
    {
    }

    protected virtual bool UpdateKindState() => false;

    protected virtual void UpdateKindPostResultState()
    {
    }

    protected virtual void AddKindDrawablesBeforeNote()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();
        if (visualsSuppressed)
        {
            visualsSuppressed = false;
            Invalidate(Invalidation.Presence);
        }

        Alpha = 1;
        ResetKindState();
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);
        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Time.Current;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                LifetimeEnd = Time.Current + 300;
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    protected override void LoadSamples()
    {
    }
}

public abstract partial class DrawableBmsHitObject<TCol> : DrawableBmsHitObject
    where TCol : struct, IColumnProvider
{

    protected int Column { get; } = default(TCol).Value;

    private BmsCachedSkinnableDrawable? cachedSkinnableDrawable;

    [BackgroundDependencyLoader]
    private void load()
    {
        AddKindDrawablesBeforeNote();

        NoteContainer = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
        };

        cachedSkinnableDrawable = new BmsCachedSkinnableDrawable(
            new BmsSkinComponentLookup(SkinComponent, LayoutVariant, Column))
        {
            ComponentAnchor = Anchor.BottomCentre,
        };

        NoteContainer.Add(cachedSkinnableDrawable);
        AddInternal(NoteContainer);
    }

    protected float NoteVisualHeight
    {
        get
        {
            var drawable = cachedSkinnableDrawable?.Drawable;
            if (drawable == null)
                return 0;

            // Width-relative skins still have an absolute height. Querying DrawHeight also resolves
            // the parent's width layout, needlessly waking culled skin trees on every frame.
            return (drawable.RelativeSizeAxes & Axes.Y) == 0 ? drawable.Height : drawable.DrawHeight;
        }
    }

    protected override float VisualHeight => NoteVisualHeight * (ParentColumn?.NoteHeightScale ?? 1);
}
