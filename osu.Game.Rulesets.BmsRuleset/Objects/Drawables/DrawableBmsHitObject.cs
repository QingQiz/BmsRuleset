using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Layout;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Drawable for a single BMS note (normal, long-note, or landmine).  Handles
///     user hit/release judgement, scroll-position projection through
///     <see cref="BmsTimingMap" />, and long-note body/tail layout.
/// </summary>
public sealed partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{

    #region Constants

    private const float max_long_note_piece_height = 4096;

    #endregion

    #region Cache

    private readonly DrawableCache cache = new();

    #endregion

    #region Construction

    public DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.TopLeft;
        Size = new Vector2(40, BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT);
    }

    #endregion

    #region Skin

    private void updateSkinPieces(BmsLayoutVariant? layoutVariant = null, int? column = null)
    {
        if (HitObject == null)
            return;

        var resolvedLayoutVariant = layoutVariant ?? Parent?.FindClosestParent<BmsPlayfield>()?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var resolvedColumn = column ?? HitObject.Column;
        var component = HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;

        if (cache.IsSkinValid(resolvedColumn, resolvedLayoutVariant, component))
            return;

        cache.SkinnedColumn = resolvedColumn;
        cache.SkinnedLayout = resolvedLayoutVariant;
        cache.SkinnedComponent = component;

        longNoteBody.BodyColour = Color4.Cyan;
        longNoteBody.Alpha = HitObject.IsLongNote ? 0.65f : 0;
        longNoteTailContainer.Alpha = HitObject.IsLongNote ? 1 : 0;

        noteContainer.Clear();
        longNoteTailContainer.Clear();

        noteContainer.Add(new BmsCachedSkinnableDrawable(new BmsSkinComponentLookup(component, resolvedLayoutVariant, resolvedColumn))
        {
            RelativeSizeAxes = Axes.Both,
            CentreComponent = false, // legacy BMS pieces use top-left anchoring
        });

        if (!HitObject.IsLongNote)
            return;

        longNoteTailContainer.Add(new BmsCachedSkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, resolvedLayoutVariant, resolvedColumn))
        {
            RelativeSizeAxes = Axes.Both,
            // See noteContainer above: cap geometry uses top-left coordinates.
            CentreComponent = false,
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

    private Container noteContainer = null!;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;

    #endregion

    #region Judgement / long-note state

    private bool longNoteStarted;
    private HitResult? longNoteHeadResult;
    private float? longNoteHeadFixedY;

    /// <summary>
    ///     When <c>true</c>, the mine has already passed harmlessly and subsequent
    ///     frames must not re-check <see cref="BmsPlayfield.IsColumnPressedForLandmine"/>
    ///     Reset in <see cref="OnApply"/> for pool reuse safety.
    /// </summary>
    private bool mineHandled;

    #endregion

    #region Sizing state

    private float currentNoteHeight = BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT;
    private bool longNotePiecesApplied;

    #endregion

    #region Public API

    /// <summary>
    ///     Attempts a key-down judgement on this note.  Returns <c>true</c> if the
    ///     note was in a valid hit window and was consumed.
    /// </summary>
    public bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
            return false;

        if (HitObject.IsLongNote && longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        if (HitObject.IsLongNote)
        {
            longNoteStarted = true;
            longNoteHeadResult = result;
            longNoteHeadFixedY = null;
            tryResolveLongNoteHeadFixedY(result);
            return true;
        }

        ApplyResult(result);
        return true;
    }

    /// <summary>
    ///     Whether this drawable is a long-note whose head has been pressed and is still
    ///     being held (not yet judged). Used by the playfield to route key-up events.
    /// </summary>
    public bool IsHoldingLongNote => HitObject is { IsLongNote: true } && longNoteStarted && !Judged;

    /// <summary>
    ///     Attempts a key-up judgement on a held long-note.  Returns <c>true</c> if the
    ///     release consumed the note. A release inside the tail window scores normally;
    ///     a release earlier than the tail window is treated as a drop and scores POOR
    ///     (so the note is judged immediately instead of staying frozen at the judgement
    ///     line until its tail time passes).
    /// </summary>
    public bool TryRelease()
    {
        if (Judged || HitObject?.HitWindows == null || !HitObject.IsLongNote || !longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.EndTime);

        if (result == HitResult.None)
        {
            // Released after the tail window already closed: the passive drop in
            // CheckForResult handles this; ignore the key-up.
            if (Time.Current > HitObject.EndTime + bmsWindows.WindowFor(HitResult.Ok))
                return false;

            // Released before the tail window opened: the long note is dropped → POOR.
            ApplyResult(HitResult.Meh);
            return true;
        }

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    #endregion

    #region Framework overrides

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        longNoteStarted = false;
        longNotePiecesApplied = false;
        longNoteHeadResult = null;
        longNoteHeadFixedY = null;
        mineHandled = false;
        cache.InvalidateAll();
        updateSkinPieces();
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

        // Once a note is judged or a mine has been handled, there is nothing left to compute:
        // scroll position, geometry, and layout metrics are all irrelevant.
        if (Judged || mineHandled)
            return;

        if (!tryRefreshLayoutMetrics(out var layout))
        {
            UpdateResult(false);
            return;
        }

        // How far in time this note is from the current playback position.
        var timeUntilHit = HitObject.StartTime - Time.Current;
        var endTimeUntilHit = HitObject.EndTime - Time.Current;

        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return; // NaN/Infinity before timing is initialised
        }

        var y = yForTimeOffset(timeUntilHit, layout);
        var tailY = yForTimeOffset(endTimeUntilHit, layout);
        var judgementHeadY = judgementHeadYFor(layout);

        var visualHeadY = visualHeadYFor(y, judgementHeadY);
        // While a long note is held, clamp the tail so it can never travel below the
        // (pinned) head. Clamping to the judgement line instead of the head caused the
        // body to flip/reverse when the head was pressed early (pinned above the line)
        // and the tail then scrolled past it during a late release.
        var visualTailY = HitObject.IsLongNote && longNoteStarted
            ? Math.Min(tailY, visualHeadY)
            : tailY;

        // Map the column-local Y into the parent drawable's coordinate space.
        var objectTop = HitObject.IsLongNote ? Math.Min(visualHeadY, visualTailY) : visualHeadY;
        var headOffset = visualHeadY - objectTop;
        var tailOffset = visualTailY - objectTop;
        var position = layout.ColumnContainer != null && Parent != null
            ? layout.ColumnContainer.ToSpaceOfOtherDrawable(new Vector2(0, objectTop), Parent)
            : new Vector2(0, objectTop);

        // A non-finite mapped position means the coordinate transform is not ready yet.
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            UpdateResult(false);
            return; // coordinate transform not ready yet
        }

        Position = position;

        // Enforce a 1 px minimum in both dimensions to avoid zero-size drawables.
        var objectHeight = HitObject.IsLongNote
            ? Math.Max(currentNoteHeight, Math.Abs(visualTailY - visualHeadY) + currentNoteHeight)
            : currentNoteHeight;

        Size = new Vector2(Math.Max(1, layout.ScaledParentWidth), objectHeight);
        // Reposition the LN body and tail pieces to match the updated head/tail Y values.
        updateLongNotePieces(headOffset, tailOffset);

        // The execution of the Update function is frame-by-frame.
        // Therefore, when the execution finds that the current time is
        // greater than the trigger time of the hitobject,
        // it means it is being triggered in the current or next frame.
        // At this point, the mine is processed:
        // if it is held down, trigger the mine; otherwise, let it expire immediately.
        if (HitObject.IsMine && !Judged && !mineHandled && Time.Current >= HitObject.StartTime)
        {
            mineHandled = true;

            if (cache.Playfield?.IsColumnPressedForLandmine(HitObject.Column) == true)
            {
                cache.Playfield.DetonateLandmine(HitObject);
                ApplyResult(HitResult.Meh);
            }
            else
            {
                // Mine passed harmlessly without being pressed.
                // Hide the mine and let the HitObjectLifetimeEntry expire naturally
                Alpha = 0;
            }

            return;
        }

        // Passive miss check: called every frame so CheckForResult can apply a miss once the hit window closes.
        UpdateResult(false);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null || HitObject.IsMine)
            return;

        var missWindow = HitObject.HitWindows.WindowFor(HitResult.Ok);

        if (HitObject.IsLongNote)
        {
            // LN head never pressed: passive POOR once the head BAD window is exhausted.
            if (!longNoteStarted && Time.Current > HitObject.StartTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                return;
            }

            // LN held but player never released before the tail BAD window expired:
            // this is a "drop" — scores POOR (Meh) in BMS.
            if (longNoteStarted && Time.Current > HitObject.EndTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                // ReSharper disable once RedundantJumpStatement
                return;
            }

            // For an in-progress LN the framework-supplied timeOffset is relative to
            // StartTime; do not apply the generic miss check below until the tail window.
            return;
        }

        // Normal note: passive POOR (Meh) once the BAD window is passed with no keypress.
        if (timeOffset > missWindow)
            ApplyResult(HitResult.Meh);
    }

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
        longNoteBody.SetSkinLookup(layoutVariant, column);
        updateSkinPieces(layoutVariant, column);
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
        tryResolveLongNoteHeadFixedY(longNoteHeadResult);
        return true;
    }

    #endregion

    #region Position math

    /// <summary>
    ///     Converts a time offset (<paramref name="timeUntilHit" />, where negative means past)
    ///     into a vertical pixel position relative to the column container's top.  For charts
    ///     with tick-based timing, the scroll coordinate is projected through
    ///     <see cref="getScrollPositionForOffset" />; otherwise a simple linear time-to-pixel scaling
    ///     is used.
    /// </summary>
    private float yForTimeOffset(double timeUntilHit, LayoutMetrics layout)
    {
        // Notes at tick0 with non-zero StartTime sit at the origin of the scroll
        // coordinate axis (scroll=0).  Using tick-based progress would make them
        // appear pinned far below the judgement line regardless of real time, so
        // fall back to linear time for these notes.
        var progressUntilHit = cache.Playfield?.TimingMap == null
                               || cache.Playfield?.ConstantScrollActive == true
                               || (HitObject.TickInfo.Tick == HitObject.TickInfo.EndTick && HitObject.TickInfo.Tick == 0 && HitObject.StartTime != 0)
            ? timeUntilHit
            : getScrollPositionForOffset(timeUntilHit) - layout.CurrentScrollPosition;

        return cache.Playfield!.YForScrollProgress(progressUntilHit, layout.ParentHeight, currentNoteHeight);
    }

    /// <summary>
    ///     Returns the precomputed scroll position for the given time offset,
    ///     using <see cref="BmsHitObject.ScrollPositionAtStartTime"/> /
    ///     <see cref="BmsHitObject.ScrollPositionAtEndTime"/> which were computed
    ///     once during beatmap loading.  This replaces the old per-frame
    ///     <see cref="BmsTimingMap.GetScrollPositionAtTime"/> call chain that
    ///     traversed the full timing-point array for every hitobject every frame.
    /// </summary>
    private double getScrollPositionForOffset(double timeUntilHit)
    {
        // Primary hot path: scroll position at note's start time (hit by yForTimeOffset for head Y).
        if (Math.Abs(timeUntilHit - (HitObject.StartTime - Time.Current)) < 0.001)
            return HitObject.ScrollPositionAtStartTime;

        // Secondary hot path: scroll position at long note's end time (hit by yForTimeOffset for tail Y).
        if (HitObject.IsLongNote && Math.Abs(timeUntilHit - (HitObject.EndTime - Time.Current)) < 0.001)
            return HitObject.ScrollPositionAtEndTime;

        // Fallback: off-boundary times (should rarely occur in practice).
        return Time.Current + timeUntilHit;
    }

    /// <summary>
    ///     Y coordinate of the judgement line in the column container's local space.
    /// </summary>
    private float judgementHeadYFor(LayoutMetrics layout)
        => cache.Playfield!.YForScrollProgress(0, layout.ParentHeight, currentNoteHeight);

    /// <summary>
    ///     Returns the visual head Y for a held long-note.  While the head has a fixed Y
    ///     (determined by <see cref="tryResolveLongNoteHeadFixedY" />), it is clamped
    ///     to the judgement line so the head never drifts below it.  For normal notes
    ///     this simply returns the natural Y.
    /// </summary>
    private float visualHeadYFor(float naturalY, float judgementHeadY)
    {
        if (!HitObject.IsLongNote || !longNoteStarted)
            return naturalY;

        return Math.Min(longNoteHeadFixedY ?? naturalY, judgementHeadY);
    }

    /// <summary>
    ///     Snaps the LN head Y to the judgement line when the head hit was perfect/great,
    ///     otherwise holds it at the actual press position so the visual offset is preserved.
    /// </summary>
    private void tryResolveLongNoteHeadFixedY(HitResult? result)
    {
        if (result == null || longNoteHeadFixedY != null || cache.LatestLayout is not { } layout)
            return;

        longNoteHeadFixedY = result is HitResult.Perfect or HitResult.Great
            ? judgementHeadYFor(layout)
            : yForTimeOffset(HitObject.StartTime - Time.Current, layout);
    }

    #endregion

    #region Note sizing

    private void updateNoteHeight(LayoutMetrics layout)
    {
        var component = currentSkinComponent();

        if (cache.IsNoteHeightValid(layout, component))
            return;

        cache.NoteHeightWidth = layout.ScaledParentWidth;
        cache.NoteHeightColumn = layout.Column;
        cache.NoteHeightLayout = layout.LayoutVariant;
        cache.NoteHeightComponent = component;
        currentNoteHeight = getCurrentNoteHeight(layout.ScaledParentWidth, layout.LayoutVariant, layout.Column);
    }

    private float getCurrentNoteHeight(float drawWidth, BmsLayoutVariant layoutVariant, int column)
    {
        var lookup = new BmsSkinComponentLookup(currentSkinComponent(), layoutVariant, column);
        return gameplaySkinCache?.GetNoteHeight(lookup, drawWidth)
               ?? BmsGameplaySkinMetricsResolver.ResolveNoteHeight(skin, lookup, drawWidth);
    }

    private BmsSkinComponents currentSkinComponent()
        => HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;

    #endregion

    #region Long-note rendering

    [BackgroundDependencyLoader]
    private void load()
    {
        AddRangeInternal([
            longNoteBody = new BmsSegmentedLongNoteBody
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
            },
            longNoteTailContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT,
                Alpha = 0,
            },
            noteContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT,
            },
        ]);
    }

    /// <summary>
    ///     Positions the LN head container, body, and tail container to match the
    ///     current visual head/tail Y offsets.  Clamps the body to
    ///     <see cref="max_long_note_piece_height" /> above and below the head.
    /// </summary>
    private void updateLongNotePieces(float headOffset, float tailOffset)
    {
        if (!HitObject.IsLongNote)
        {
            if (!longNotePiecesApplied)
            {
                noteContainer.Y = 0;
                noteContainer.Height = currentNoteHeight;
                longNoteBody.Alpha = 0;
                longNoteTailContainer.Alpha = 0;
                longNotePiecesApplied = true;
            }

            return;
        }

        longNotePiecesApplied = true;

        if (Math.Abs(noteContainer.Y - headOffset) > 0.5f)
            noteContainer.Y = headOffset;

        if (Math.Abs(noteContainer.Height - currentNoteHeight) > 0.5f)
            noteContainer.Height = currentNoteHeight;

        var tailAtTop = tailOffset < headOffset;
        var bodyTop = Math.Min(headOffset, tailOffset);
        var bodyBottom = Math.Max(headOffset, tailOffset) + currentNoteHeight;

        var visibleTop = Math.Max(bodyTop, headOffset - max_long_note_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_long_note_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        if (Math.Abs(longNoteBody.Y - visibleTop) > 0.5f)
            longNoteBody.Y = visibleTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(1, bodyHeight);

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, longNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;

        if (Math.Abs(longNoteTailContainer.Y - tailOffset) > 0.5f)
            longNoteTailContainer.Y = tailOffset;

        if (Math.Abs(longNoteTailContainer.Height - currentNoteHeight) > 0.5f)
            longNoteTailContainer.Height = currentNoteHeight;

        longNoteTailContainer.Alpha = 1;
    }

    #endregion

}
