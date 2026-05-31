using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Layout;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Placeholder drawable for native BMS notes.
/// </summary>
/// <remarks>
///     This intentionally favours clarity over final gameplay fidelity. The object is rendered in a
///     lane determined by <see cref="BmsHitObject.Column" /> and scrolls by projected osu! time.
/// </remarks>
public sealed partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{
    private const float max_long_note_piece_height = 4096;

    private readonly record struct LayoutMetrics(
        int Column,
        BmsLayoutVariant LayoutVariant,
        Drawable? ColumnContainer,
        float ParentWidth,
        float ParentHeight,
        float ScaledParentWidth,
        float TravelDistance,
        float HitTargetPosition,
        double TimeRange,
        double ScrollSpeedMultiplier,
        BmsTimingMap? TimingMap,
        double CurrentScrollPosition,
        double ScrollRange);

    private readonly record struct LayoutReferences(
        BmsPlayfield? Playfield,
        BmsStage? Stage,
        Drawable? ColumnContainer,
        int Column,
        BmsLayoutVariant LayoutVariant);

    private Container noteContainer = null!;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;
    private bool longNoteStarted;
    private HitResult? longNoteHeadResult;
    private float? longNoteHeadFixedY;
    private BmsPlayfield? playfield;
    private float currentNoteHeight = BmsNoteSizing.DEFAULT_NOTE_HEIGHT;
    private int skinnedColumn = -1;
    private BmsLayoutVariant? skinnedLayout;
    private BmsSkinComponents? skinnedComponent;
    private LayoutReferences? layoutReferences;
    private LayoutMetrics? latestLayout;
    private float cachedNoteHeightWidth = -1;
    private int cachedNoteHeightColumn = -1;
    private BmsLayoutVariant? cachedNoteHeightLayout;
    private BmsSkinComponents? cachedNoteHeightComponent;

    private float cachedScaledParentWidth = -1;
    private float cachedParentWidthForTransform;
    private float cachedParentHeightForTransform;

    private bool longNotePiecesApplied;

    [Resolved(CanBeNull = true)]
    private ISkinSource? skin { get; set; }

    public DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.TopLeft;
        Size = new Vector2(40, BmsNoteSizing.DEFAULT_NOTE_HEIGHT);
    }

    public bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
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

    public bool TryRelease()
    {
        if (Judged || HitObject?.HitWindows == null || !HitObject.IsLongNote || !longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.EndTime);

        if (result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        longNoteStarted = false;
        longNotePiecesApplied = false;
        longNoteHeadResult = null;
        longNoteHeadFixedY = null;
        playfield = null;
        layoutReferences = null;
        latestLayout = null;
        cachedScaledParentWidth = -1;
        cachedParentWidthForTransform = 0;
        cachedParentHeightForTransform = 0;
        skinnedColumn = -1;
        skinnedLayout = null;
        skinnedComponent = null;
        invalidateNoteHeightCache();
        updateSkinPieces();
    }

    protected override bool OnInvalidate(Invalidation invalidation, InvalidationSource source)
    {
        var result = base.OnInvalidate(invalidation, source);

        if ((invalidation & Invalidation.Parent) != 0)
            invalidateLayoutReferences();

        return result;
    }

    protected override void Update()
    {
        base.Update();

        // HitObject may not be set yet during early pool lifecycle; bail out.
        if (HitObject == null)
            return;

        if (!tryRefreshLayoutMetrics(out var layout))
        {
            UpdateResult(false);
            return;
        }

        // How far in time this note is from the current playback position.
        var timeUntilHit = HitObject.StartTime - Time.Current;
        var endTimeUntilHit = HitObject.EndTime - Time.Current;

        // NaN/Infinity can occur before timing is initialised; bail out safely.
        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return;
        }

        var y = yForTimeOffset(timeUntilHit, layout);
        var tailY = yForTimeOffset(endTimeUntilHit, layout);
        var judgementHeadY = judgementHeadYFor(layout);

        var visualHeadY = visualHeadYFor(y, judgementHeadY);
        var visualTailY = HitObject.IsLongNote && longNoteStarted
            ? Math.Min(tailY, judgementHeadY)
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
            return;
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
        if (HitObject.IsMine && !Judged && Time.Current >= HitObject.StartTime)
        {
            if (playfield?.IsColumnPressedForLandmine(HitObject.Column) == true)
            {
                playfield.DetonateLandmine(HitObject);
                ApplyResult(HitResult.Meh);
            }
            else
                Expire(); // column was not held — mine passes silently

            return;
        }

        // Passive miss check: called every frame so CheckForResult can apply a miss once the hit window closes.
        UpdateResult(false);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null || HitObject.IsMine)
            return;

        var missWindow = HitObject.HitWindows.WindowFor(HitResult.Ok); // BAD is the passive-miss boundary

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
        base.UpdateInitialTransforms();
        this.FadeInFromZero(100);
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

    protected override void LoadSamples()
    {
        if (!string.IsNullOrEmpty(HitObject.SamplePath))
            Samples.Samples = [new BmsSampleInfo(HitObject.SamplePath)];
        else
            base.LoadSamples();
    }

    private bool ensureLayoutReferences()
    {
        if (layoutReferences != null)
            return true;

        playfield = Parent?.FindClosestParent<BmsPlayfield>();
        var stage = playfield?.Stage;
        var column = Math.Clamp(HitObject.Column, 0, playfield?.TotalColumns - 1 ?? 0);
        var layoutVariant = playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var columnContainer = stage != null && column < stage.Columns.Length ? stage.Columns[column].HitObjectArea : Parent;

        if (columnContainer == null)
            return false;

        layoutReferences = new LayoutReferences(playfield, stage, columnContainer, column, layoutVariant);

        // These operations depend only on column/layout/component/skin lookup. They are comparatively
        // expensive and should not be part of the per-frame metrics refresh path.
        longNoteBody.SetSkinLookup(layoutVariant, column);
        updateSkinPieces(layoutVariant, column);
        invalidateNoteHeightCache();
        return true;
    }

    private bool tryRefreshLayoutMetrics(out LayoutMetrics layout)
    {
        if (!ensureLayoutReferences() || layoutReferences is not { } references)
        {
            layout = default;
            return false;
        }

        var stage = references.Stage;
        var columnContainer = references.ColumnContainer;
        var parentWidth = columnContainer?.DrawWidth ?? Parent?.DrawWidth ?? 0;
        var parentHeight = columnContainer?.DrawHeight ?? Parent?.DrawHeight ?? 0;

        if (!float.IsFinite(parentWidth) || !float.IsFinite(parentHeight) || parentWidth <= 0 || parentHeight <= 0)
        {
            layout = default;
            return false;
        }

        var scaledParentWidth = parentWidth;
        var transformChanged = Math.Abs(parentWidth - cachedParentWidthForTransform) >= 1
                               || Math.Abs(parentHeight - cachedParentHeightForTransform) >= 1;

        if (transformChanged && columnContainer != null && Parent != null)
        {
            var left = columnContainer.ToSpaceOfOtherDrawable(Vector2.Zero, Parent);
            var right = columnContainer.ToSpaceOfOtherDrawable(new Vector2(parentWidth, 0), Parent);
            scaledParentWidth = (right - left).Length;
            cachedScaledParentWidth = scaledParentWidth;
            cachedParentWidthForTransform = parentWidth;
            cachedParentHeightForTransform = parentHeight;
        }
        else if (!transformChanged && cachedScaledParentWidth > 0)
        {
            scaledParentWidth = cachedScaledParentWidth;
        }

        layout = new LayoutMetrics(
            references.Column,
            references.LayoutVariant,
            columnContainer,
            parentWidth,
            parentHeight,
            Math.Max(1, scaledParentWidth),
            Math.Max(1f, parentHeight - (stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION)),
            stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION,
            playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(8),
            playfield?.ScrollSpeedMultiplier ?? 1,
            playfield?.TimingMap,
            playfield?.CurrentScrollPosition ?? Time.Current,
            playfield?.ScrollRange ?? (playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(8)));

        latestLayout = layout;
        updateNoteHeight(layout);
        tryResolveLongNoteHeadFixedY(longNoteHeadResult);
        return true;
    }

    private void invalidateNoteHeightCache()
    {
        cachedNoteHeightWidth = -1;
        cachedNoteHeightColumn = -1;
        cachedNoteHeightLayout = null;
        cachedNoteHeightComponent = null;
    }

    private void invalidateLayoutReferences()
    {
        layoutReferences = null;
        latestLayout = null;
        cachedScaledParentWidth = -1;
        cachedParentWidthForTransform = 0;
        cachedParentHeightForTransform = 0;
        invalidateNoteHeightCache();
    }

    private void updateNoteHeight(LayoutMetrics layout)
    {
        var component = currentSkinComponent();

        if (Math.Abs(cachedNoteHeightWidth - layout.ScaledParentWidth) < 1
            && cachedNoteHeightColumn == layout.Column
            && cachedNoteHeightLayout == layout.LayoutVariant
            && cachedNoteHeightComponent == component)
            return;

        cachedNoteHeightWidth = layout.ScaledParentWidth;
        cachedNoteHeightColumn = layout.Column;
        cachedNoteHeightLayout = layout.LayoutVariant;
        cachedNoteHeightComponent = component;
        currentNoteHeight = getCurrentNoteHeight(layout.ScaledParentWidth, layout.LayoutVariant, layout.Column);
    }

    private float yForTimeOffset(double timeUntilHit, LayoutMetrics layout)
    {
        var progressUntilHit = layout.TimingMap == null || HitObject.TickInfo.Tick == HitObject.TickInfo.EndTick && HitObject.TickInfo.Tick == 0 && HitObject.StartTime != 0
            ? timeUntilHit
            : scrollPositionFor(timeUntilHit, layout) - layout.CurrentScrollPosition;

        return layout.ParentHeight - layout.HitTargetPosition - (float)(progressUntilHit * layout.ScrollSpeedMultiplier / layout.ScrollRange) * layout.TravelDistance - currentNoteHeight;
    }

    private double scrollPositionFor(double timeUntilHit, LayoutMetrics layout)
    {
        if (layout.TimingMap == null)
            return Time.Current + timeUntilHit;

        if (Math.Abs(timeUntilHit - (HitObject.StartTime - Time.Current)) < 0.001)
            return layout.TimingMap.GetScrollPositionAtTick(HitObject.TickInfo.Tick);

        if (HitObject.IsLongNote && Math.Abs(timeUntilHit - (HitObject.EndTime - Time.Current)) < 0.001)
            return layout.TimingMap.GetScrollPositionAtTick(HitObject.TickInfo.EndTick);

        return layout.TimingMap.GetScrollPositionAtTime(Time.Current + timeUntilHit);
    }

    private float judgementHeadYFor(LayoutMetrics layout)
        => layout.ParentHeight - layout.HitTargetPosition - currentNoteHeight;

    private float visualHeadYFor(float naturalY, float judgementHeadY)
    {
        if (!HitObject.IsLongNote || !longNoteStarted)
            return naturalY;

        return Math.Min(longNoteHeadFixedY ?? naturalY, judgementHeadY);
    }

    private void tryResolveLongNoteHeadFixedY(HitResult? result)
    {
        if (result == null || longNoteHeadFixedY != null || latestLayout is not { } layout)
            return;

        // PGREAT/GREAT are visually accepted as “on the line”. Lower but still valid head hits are
        // held at the actual press position so the skin test shows the timing offset instead of
        // correcting it back to the judgement line.
        longNoteHeadFixedY = result is HitResult.Perfect or HitResult.Great
            ? judgementHeadYFor(layout)
            : yForTimeOffset(HitObject.StartTime - Time.Current, layout);
    }

    private float getCurrentNoteHeight(float drawWidth, BmsLayoutVariant layoutVariant, int column)
        => BmsNoteSizing.GetNoteHeight(skin, new BmsSkinComponentLookup(
            currentSkinComponent(),
            layoutVariant, column), drawWidth);

    private BmsSkinComponents currentSkinComponent()
        => HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;

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
                Height = BmsNoteSizing.DEFAULT_NOTE_HEIGHT,
                Alpha = 0,
            },
            noteContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = BmsNoteSizing.DEFAULT_NOTE_HEIGHT,
            },
        ]);
    }

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

    private void updateSkinPieces(BmsLayoutVariant? layoutVariant = null, int? column = null)
    {
        if (HitObject == null)
            return;

        var resolvedLayoutVariant = layoutVariant ?? Parent?.FindClosestParent<BmsPlayfield>()?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var resolvedColumn = column ?? HitObject.Column;
        var component = HitObject.IsMine ? BmsSkinComponents.Mine : HitObject.IsLongNote ? BmsSkinComponents.HoldNoteHead : BmsSkinComponents.Note;

        if (skinnedColumn == resolvedColumn && skinnedLayout == resolvedLayoutVariant && skinnedComponent == component)
            return;

        skinnedColumn = resolvedColumn;
        skinnedLayout = resolvedLayoutVariant;
        skinnedComponent = component;

        longNoteBody.BodyColour = Color4.Cyan;
        longNoteBody.Alpha = HitObject.IsLongNote ? 0.65f : 0;
        longNoteTailContainer.Alpha = HitObject.IsLongNote ? 1 : 0;

        noteContainer.Clear();
        longNoteTailContainer.Clear();

        noteContainer.Add(new SkinnableDrawable(new BmsSkinComponentLookup(component, resolvedLayoutVariant, resolvedColumn), _ => new DefaultBmsNotePiece(defaultColourFor(component)))
        {
            RelativeSizeAxes = Axes.Both,
            // Legacy BMS note pieces are normalised to top-left anchoring. Do not centre them,
            // otherwise judgement alignment shifts by roughly one cap height.
            CentreComponent = false,
        });

        if (!HitObject.IsLongNote)
            return;

        longNoteTailContainer.Add(new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, resolvedLayoutVariant, resolvedColumn), _ => Empty())
        {
            RelativeSizeAxes = Axes.Both,
            // See noteContainer above: cap geometry uses top-left coordinates.
            CentreComponent = false,
        });

        static Color4 defaultColourFor(BmsSkinComponents component) => component switch
        {
            BmsSkinComponents.Mine => Color4.OrangeRed,
            BmsSkinComponents.HoldNoteHead => Color4.Cyan,
            _ => Color4.White,
        };
    }

    private sealed partial class DefaultBmsNotePiece : CompositeDrawable
    {
        public DefaultBmsNotePiece(Color4 colour)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colour,
            };
        }
    }
}
