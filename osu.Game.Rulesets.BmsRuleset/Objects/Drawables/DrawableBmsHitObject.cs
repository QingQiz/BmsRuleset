using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Layout;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Shared visual base for a single BMS note. Concrete subclasses own
///     note-kind-specific judgement and state handling.
/// </summary>
public abstract partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{

    #region Cache

    private readonly DrawableCache cache = new();

    #endregion

    #region Construction

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.TopLeft;
        Size = new Vector2(40, BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT);
    }

    #endregion

    #region Skin

    protected abstract BmsSkinComponents SkinComponent { get; }

    protected virtual void UpdateSkinPieces(BmsLayoutVariant? layoutVariant = null, int? column = null)
    {
        if (HitObject == null)
            return;

        var resolvedLayoutVariant = layoutVariant ?? Parent?.FindClosestParent<BmsPlayfield>()?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var resolvedColumn = column ?? HitObject.Column;
        var component = SkinComponent;

        if (cache.IsSkinValid(resolvedColumn, resolvedLayoutVariant, component))
            return;

        cache.SkinnedColumn = resolvedColumn;
        cache.SkinnedLayout = resolvedLayoutVariant;
        cache.SkinnedComponent = component;

        NoteContainer.Clear();

        NoteContainer.Add(new BmsCachedSkinnableDrawable(new BmsSkinComponentLookup(component, resolvedLayoutVariant, resolvedColumn))
        {
            RelativeSizeAxes = Axes.Both,
            CentreComponent = false, // legacy BMS pieces use top-left anchoring
        });
    }

    #endregion

    #region DI

    [Resolved(CanBeNull = true)]
    private ISkinSource? skin { get; set; }

    [Resolved(CanBeNull = true)]
    private BmsGameplaySkinCache? gameplaySkinCache { get; set; }

    #endregion

    #region Core drawable fields

    protected Container NoteContainer = null!;

    #endregion

    #region Sizing state

    protected float CurrentNoteHeight = BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT;
    protected bool VisualPiecesApplied;

    #endregion

    #region Public API

    /// <summary>
    ///     Attempts a key-down judgement on this note.  Returns <c>true</c> if the
    ///     note was in a valid hit window and was consumed.
    /// </summary>
    public virtual bool TryHit() => false;

    public override void PlaySamples()
    {
    }

    #endregion

    #region Kind hooks

    protected virtual bool SkipFurtherUpdates => false;

    protected BmsPlayfield? Playfield => cache.Playfield;

    protected LayoutMetrics? LatestLayout => cache.LatestLayout;

    protected virtual void ResetKindState()
    {
    }

    /// <summary>
    ///     Runs kind-specific state after common layout has been updated.
    ///     Return <c>true</c> when the common passive result check should be skipped.
    /// </summary>
    protected virtual bool UpdateKindState() => false;

    protected virtual void OnLayoutMetricsRefreshed(LayoutMetrics layout)
    {
    }

    protected virtual void OnLayoutResolved(BmsLayoutVariant layoutVariant, int column)
    {
    }

    protected virtual float VisualHeadYFor(float naturalY, float judgementHeadY) => naturalY;

    protected readonly record struct VisualLayout(
        float ObjectTop,
        float PrimaryOffset,
        float SecondaryOffset,
        float ObjectHeight);

    #endregion

    #region Framework overrides

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        VisualPiecesApplied = false;
        cache.InvalidateAll();
        ResetKindState();
        UpdateSkinPieces();
    }

    protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
        var result = base.OnInvalidate(invalidation, source);

        if ((invalidation & Invalidation.Parent) != 0)
            cache.InvalidateAll();

        return result;
    }

    protected override void Update()
    {
        base.Update();

        if (HitObject == null)
            return; // HitObject may not be set yet during early pool lifecycle

        // Once a note is judged or a kind-specific state says it is finished,
        // scroll position, geometry, and layout metrics are all irrelevant.
        if (Judged || SkipFurtherUpdates)
            return;

        if (!tryRefreshLayoutMetrics(out var layout))
        {
            UpdateResult(false);
            return;
        }

        // How far in time this note is from the current playback position.
        var timeUntilHit = HitObject.StartTime - Time.Current;

        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return; // NaN/Infinity before timing is initialised
        }

        var y = YForTimeOffset(timeUntilHit, layout);
        var judgementHeadY = JudgementHeadYFor(layout);
        var visualHeadY = VisualHeadYFor(y, judgementHeadY);
        var visualLayout = CreateVisualLayout(visualHeadY, layout);

        // Map the column-local Y into the parent drawable's coordinate space.
        var position = layout.ColumnContainer != null && Parent != null
            ? layout.ColumnContainer.ToSpaceOfOtherDrawable(new Vector2(0, visualLayout.ObjectTop), Parent)
            : new Vector2(0, visualLayout.ObjectTop);

        // A non-finite mapped position means the coordinate transform is not ready yet.
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            UpdateResult(false);
            return; // coordinate transform not ready yet
        }

        Position = position;

        // Enforce a 1 px minimum in both dimensions to avoid zero-size drawables.
        Size = new Vector2(Math.Max(1, layout.ScaledParentWidth), visualLayout.ObjectHeight);
        UpdateVisualPieces(visualLayout.PrimaryOffset, visualLayout.SecondaryOffset);

        if (UpdateKindState())
            return;

        // Passive miss check: called every frame so CheckForResult can apply a miss once the hit window closes.
        UpdateResult(false);
    }

    protected virtual VisualLayout CreateVisualLayout(float visualY, LayoutMetrics layout)
        => new(visualY, 0, 0, CurrentNoteHeight);

    protected override void UpdateInitialTransforms()
    {
        // Hidden scratch notes must stay invisible from the start. The base fade-in transform
        // is skipped for these so a one-shot Alpha=0 is sufficient — no per-frame enforcement needed.
        if (isColumnHidden())
        {
            Alpha = 0;
            return;
        }

        base.UpdateInitialTransforms();
        this.FadeInFromZero(100);
    }

    /// <summary>
    ///     Whether this note belongs to a scratch column that is currently hidden
    ///     (AutoScratch with hide-scratch enabled).  Resolves the playfield lazily and
    ///     caches the result so it is valid even before the layout references are built.
    /// </summary>
    private bool isColumnHidden()
    {
        if (cache.ColumnHidden is { } cached)
            return cached;

        if (HitObject == null || !ensureLayoutReferences())
            return false;

        return cache.ColumnHidden == true;
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);

        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Math.Max(HitObject.EndTime, HitStateUpdateTime) + 100;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    /// <summary>
    /// the key sound is proceeded by the play filed, so the sample is no need to load.
    /// </summary>
    protected override void LoadSamples()
    {
    }

    #endregion

    #region Layout

    /// <summary>
    ///     Walks up the parent hierarchy to find the enclosing <see cref="BmsPlayfield" />
    ///     and stage, then caches the column container and associated metadata.
    /// </summary>
    private bool ensureLayoutReferences()
    {
        if (cache.HasLayout)
            return true;

        cache.Playfield = Parent?.FindClosestParent<BmsPlayfield>();
        var stage = cache.Playfield?.Stage;
        var column = Math.Clamp(HitObject.Column, 0, cache.Playfield?.TotalColumns - 1 ?? 0);
        var layoutVariant = cache.Playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var columnContainer = stage != null && column < stage.Columns.Length ? stage.Columns[column].HitObjectArea : Parent;

        if (columnContainer == null)
            return false;

        cache.ApplyLayout(stage, columnContainer, column, layoutVariant);
        cache.ComputeColumnHidden();

        if (cache.ColumnHidden == true)
            Alpha = 0;

        // These operations depend only on column/layout/component/skin lookup. They are comparatively
        // expensive and should not be part of the per-frame metrics refresh path.
        OnLayoutResolved(layoutVariant, column);
        UpdateSkinPieces(layoutVariant, column);
        cache.InvalidateNoteHeight();
        return true;
    }

    /// <summary>
    ///     Rebuilds the per-frame layout snapshot.  Computes scaled column width,
    ///     travel distance, hit-target position, and timing-derived scroll fields.
    /// </summary>
    private bool tryRefreshLayoutMetrics(out LayoutMetrics layout)
    {
        if (!ensureLayoutReferences() || !cache.TryGetLayout(out var references))
        {
            layout = default;
            return false;
        }

        var columnContainer = references.ColumnContainer;
        var parentWidth = columnContainer?.DrawWidth ?? Parent?.DrawWidth ?? 0;
        var parentHeight = columnContainer?.DrawHeight ?? Parent?.DrawHeight ?? 0;

        if (!float.IsFinite(parentWidth) || !float.IsFinite(parentHeight) || parentWidth <= 0 || parentHeight <= 0)
        {
            layout = default;
            return false;
        }

        var scaledParentWidth = parentWidth;
        var transformChanged = cache.TransformChanged(parentWidth, parentHeight);

        if (transformChanged && columnContainer != null && Parent != null)
        {
            var left = columnContainer.ToSpaceOfOtherDrawable(Vector2.Zero, Parent);
            var right = columnContainer.ToSpaceOfOtherDrawable(new Vector2(parentWidth, 0), Parent);
            scaledParentWidth = (right - left).Length;
            cache.ApplyTransform(parentWidth, parentHeight, scaledParentWidth);
        }
        else if (!transformChanged && cache.ScaledParentWidth > 0)
        {
            scaledParentWidth = cache.ScaledParentWidth;
        }

        layout = cache.CreateLayoutMetrics(parentHeight, Math.Max(1, scaledParentWidth), Time.Current);

        cache.LatestLayout = layout;
        updateNoteHeight(layout);
        OnLayoutMetricsRefreshed(layout);
        return true;
    }

    #endregion

    #region Position math

    /// <summary>
    ///     Converts a time offset (<paramref name="timeUntilHit" />, where negative means past)
    ///     into a vertical pixel position relative to the column container's top.  For charts
    ///     with tick-based timing, the scroll coordinate is projected through
    ///     <see cref="GetScrollPositionForOffset" />; otherwise a simple linear time-to-pixel scaling
    ///     is used.
    /// </summary>
    protected float YForTimeOffset(double timeUntilHit, LayoutMetrics layout)
    {
        // Notes at tick0 with non-zero StartTime sit at the origin of the scroll
        // coordinate axis (scroll=0).  Using tick-based progress would make them
        // appear pinned far below the judgement line regardless of real time, so
        // fall back to linear time for these notes.
        var progressUntilHit = cache.Playfield?.TimingMap == null
                               || cache.Playfield?.ConstantScrollActive == true
                               || (HitObject.TickInfo.Tick == HitObject.TickInfo.EndTick && HitObject.TickInfo.Tick == 0 && HitObject.StartTime != 0)
            ? timeUntilHit
            : GetScrollPositionForOffset(timeUntilHit) - layout.CurrentScrollPosition;

        return cache.Playfield!.YForScrollProgress(progressUntilHit, layout.ParentHeight, CurrentNoteHeight);
    }

    /// <summary>
    ///     Returns the precomputed scroll position for the given time offset,
    ///     using a precomputed scroll position from the hit object. This replaces the
    ///     old per-frame <see cref="BmsTimingMap.GetScrollPositionAtTime"/> call chain
    ///     that traversed the full timing-point array for every hitobject every frame.
    /// </summary>
    protected virtual double GetScrollPositionForOffset(double timeUntilHit)
    {
        // Primary hot path: scroll position at note's start time (hit by yForTimeOffset for head Y).
        if (Math.Abs(timeUntilHit - (HitObject.StartTime - Time.Current)) < 0.001)
            return HitObject.ScrollPositionAtStartTime;

        // Fallback: off-boundary times (should rarely occur in practice).
        return Time.Current + timeUntilHit;
    }

    /// <summary>
    ///     Y coordinate of the judgement line in the column container's local space.
    /// </summary>
    protected float JudgementHeadYFor(LayoutMetrics layout)
        => cache.Playfield!.YForScrollProgress(0, layout.ParentHeight, CurrentNoteHeight);

    #endregion

    #region Note sizing

    private void updateNoteHeight(LayoutMetrics layout)
    {
        var component = SkinComponent;

        if (cache.IsNoteHeightValid(layout, component))
            return;

        cache.NoteHeightWidth = layout.ScaledParentWidth;
        cache.NoteHeightColumn = layout.Column;
        cache.NoteHeightLayout = layout.LayoutVariant;
        cache.NoteHeightComponent = component;
        CurrentNoteHeight = getCurrentNoteHeight(layout.ScaledParentWidth, layout.LayoutVariant, layout.Column);
    }

    private float getCurrentNoteHeight(float drawWidth, BmsLayoutVariant layoutVariant, int column)
    {
        var lookup = new BmsSkinComponentLookup(SkinComponent, layoutVariant, column);
        return gameplaySkinCache?.GetNoteHeight(lookup, drawWidth)
               ?? BmsGameplaySkinMetricsResolver.ResolveNoteHeight(skin, lookup, drawWidth);
    }

    #endregion

    #region Rendering

    [BackgroundDependencyLoader]
    private void load()
    {
        AddKindDrawablesBeforeNote();

        AddInternal(
            NoteContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT,
            });
    }

    protected virtual void AddKindDrawablesBeforeNote()
    {
    }

    protected virtual void UpdateVisualPieces(float primaryOffset, float secondaryOffset)
    {
        if (!VisualPiecesApplied)
        {
            NoteContainer.Y = 0;
            NoteContainer.Height = CurrentNoteHeight;
            VisualPiecesApplied = true;
        }
    }

    #endregion

}
