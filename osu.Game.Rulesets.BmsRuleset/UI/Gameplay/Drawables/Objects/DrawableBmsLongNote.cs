using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLongNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    // The framework's reflection fallback misses the inherited protected draw-node factory.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    private BmsNoteVisual head = null!;

    protected override float VisualHeight => head.VisualHeight * (ParentColumn?.NoteHeightScale ?? 1);

    private BmsLongNote ln => (BmsLongNote)HitObject;

    // Re-trigger the LN hit light this often while holding so the explosion pulses throughout the
    // hold instead of only firing at the head and tail endpoints.
    private const double hold_explosion_interval = BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION;

    // Body+tail fade to this alpha when the head is judged but the key is released before the tail judged
    private const float released_alpha = 0.4f;

    private readonly BmsLongNoteVisualState visualState = new();
    private readonly Func<float, float, int> resolveBodyDirection;

    private BmsLongNotePresentation presentation;
    private BmsLongNotePresentationMapping mapPresentation = null!;
    private readonly BmsLongNoteStateObserver stateObserver;
    private readonly BmsVisualTimeObserver visualTimeObserver;
    private (double Time, double Offset, BmsColumnVisualFrame Frame)? lastVisualObservation;

    private double lastHoldExplosionTime;
    private bool restoreHeadPin;
    private bool bodyGeometryValid;
    private float preparedHeadY;
    private float preparedTailY;
    private bool preparedHoldingBody;
    private BmsLongNotePresentation preparedPresentation;
    private float lastHeadOffset;
    private float lastBodyTailOffset;
    private float lastTailOffset;
    private float lastBodyWidth;
    private float lastVisualNoteHeight;
    private float noteHeightScale = 1;
    private bool lastHoldingBody;
    private bool lastReleasedFast;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private BmsCachedSkinnableDrawable longNoteTail = null!;

    public DrawableBmsLongNote()
    {
        // A held note resolves its visual head for both culling and geometry every frame.
        // Keep the instance delegate across pool reuse instead of allocating at each call.
        resolveBodyDirection = bodyDirectionBeforeTailPasses;
        stateObserver = applyPresentation;
        visualTimeObserver = observeVisualTime;
    }

    /// <summary>
    /// Keeps the head-to-tail bounds with the object so column layout does not depend on its geometry.
    /// </summary>
    protected private override BmsVisualLayout PrepareVisual(float y, in BmsColumnVisualFrame frame)
        => prepareVisual(y, readPresentation(Time.Current), tailY(frame));

    internal override void UpdateVisualFrame(float y, in BmsColumnVisualFrame frame)
    {
        if (!IsCurrentVisualTimeObserver(visualTimeObserver))
        {
            base.UpdateVisualFrame(y, frame);
            return;
        }

        // Only the full simulation traversal records visual history. Share its read with
        // geometry preparation without caching across same-time input or result callbacks.
        var current = readPresentation(Time.Current);
        // Hold feedback follows visible frames, independently of the next judgement deadline.
        if (Time.Current >= ln.StartTime && Time.Current <= ln.EndTime && current.HoldLightActive
            && Time.Current - lastHoldExplosionTime >= hold_explosion_interval)
        {
            ParentColumn?.TriggerHitExplosion(current.HitLightStyle);
            lastHoldExplosionTime = Time.Current;
        }

        var tail = tailY(frame);
        observeVisualTime(y, frame, current, tail);
        ApplyVisualLayout(frame, prepareVisual(y, current, tail));
    }

    private BmsVisualLayout prepareVisual(float y, in BmsLongNotePresentation current, float tail)
    {
        preparedTailY = tail;
        preparedPresentation = current;
        preparedHoldingBody = preparedPresentation.HoldingBody;
        preparedHeadY = preparedHoldingBody ? visualState.ResolveHeldHeadY(y, preparedPresentation.HeadReached) : y;
        // A culled skin may change before re-entry, even when the projected endpoints are unchanged.
        bodyGeometryValid &= !VisualsSuppressed;
        return new BmsVisualLayout(y, Math.Min(preparedHeadY, preparedTailY), Math.Max(preparedHeadY, preparedTailY));
    }

    protected private override void ApplyVisual(in BmsVisualLayout layout)
    {
        base.ApplyVisual(layout);
        updateVisualGeometry(preparedHeadY, preparedTailY, preparedHoldingBody);
    }

    private float tailY(in BmsColumnVisualFrame frame)
        => frame.YAt(double.IsNaN(ln.VisualScrollPositionAtEndTime) ? ln.ScrollPositionAtEndTime : ln.VisualScrollPositionAtEndTime);

    private void updateVisualGeometry(float headY, float endY, bool holdingBody)
    {
        const float max_piece_height = 4096;

        var myY = Y;
        var headOffset = headY - myY;
        var tailOffset = endY - myY;
        var bodyTailOffset = holdingBody
            ? visualState.VisibleBodyTailOffset(headOffset, tailOffset)
            : tailOffset;
        var visualNoteHeight = head.VisualHeight * noteHeightScale;

        var releasedFast = preparedPresentation.ReleasedStyle;
        var geometryChanged = !bodyGeometryValid
                              || Math.Abs(lastHeadOffset - headOffset) > 0.5f
                              || Math.Abs(lastBodyTailOffset - bodyTailOffset) > 0.5f
                              || Math.Abs(lastTailOffset - tailOffset) > 0.5f
                              || Math.Abs(lastBodyWidth - longNoteBody.DrawWidth) >= 1
                              || Math.Abs(lastVisualNoteHeight - visualNoteHeight) > 0.5f
                              || lastHoldingBody != holdingBody
                              || lastReleasedFast != releasedFast;

        if (!geometryChanged)
        {
            // Static LNs move with their parent. Their relative body geometry does not need to be
            // invalidated every frame, but animated skins must still be allowed to advance.
            longNoteBody.UpdateAnimation(preparedPresentation.AnimateBody);
            return;
        }

        bodyGeometryValid = true;
        lastHeadOffset = headOffset;
        lastBodyTailOffset = bodyTailOffset;
        lastTailOffset = tailOffset;
        lastBodyWidth = longNoteBody.DrawWidth;
        lastVisualNoteHeight = visualNoteHeight;
        lastHoldingBody = holdingBody;
        lastReleasedFast = releasedFast;

        if (Math.Abs(head.Y - headOffset) > 0.5f)
            head.Y = headOffset;

        // Half-overlap keeps rounded or transparent endpoint edges visually connected, matching mania.
        var bodyHeadCentre = BmsLongNoteGeometry.BodyCentreOffset(headOffset, visualNoteHeight);
        var bodyTailCentre = BmsLongNoteGeometry.BodyCentreOffset(bodyTailOffset, visualNoteHeight);
        var tailAtTop = bodyTailCentre < bodyHeadCentre;
        var bodyTop = Math.Min(bodyHeadCentre, bodyTailCentre);
        var bodyBottom = Math.Max(bodyHeadCentre, bodyTailCentre);

        var visibleTop = Math.Max(bodyTop, bodyHeadCentre - max_piece_height);
        var visibleBottom = Math.Min(bodyBottom, bodyHeadCentre + max_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        if (Math.Abs(longNoteBody.Y - visibleTop) > 0.5f)
            longNoteBody.Y = visibleTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(1, bodyHeight);

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, preparedPresentation.AnimateBody);
        longNoteBody.Alpha = bodyHeight > 0 ? releasedFast ? released_alpha : 1f : 0;

        if (Math.Abs(longNoteTail.Y - tailOffset) > 0.5f)
            longNoteTail.Y = tailOffset;

        if (Math.Abs(longNoteTail.Height - Height) > 0.5f)
            longNoteTail.Height = Height;

        longNoteTail.Alpha = releasedFast ? released_alpha : 1f;
    }

    private void observeVisualTime(float y, in BmsColumnVisualFrame frame)
    {
        var current = readPresentation(Time.Current);
        observeVisualTime(y, frame, current, current.HoldingBody ? tailY(frame) : 0);
    }

    private void observeVisualTime(float y, in BmsColumnVisualFrame frame, in BmsLongNotePresentation current, float tail)
    {
        if (restoreHeadPin)
        {
            // Seeking into a hold can reactivate it without observing its head cross the line.
            // Reconstruct that position once, including visual offset, before resuming the pin.
            if (current.HeadReached && ParentColumn != null)
                visualState.RestoreHeadYAtStartTime(ParentColumn.GetLongNoteHeadYAtStartTime(ln));
            restoreHeadPin = false;
        }

        var observation = (Time: Time.Current, Offset: ParentColumn?.VisualOffset ?? 0, Frame: frame);
        if (lastVisualObservation != observation)
        {
            // A final skin traversal can revisit a sampled simulation frame without extending its history.
            visualState.UpdateHeadYAtStartTime(y, observation.Time, HitObject.StartTime, observation.Offset);
            lastVisualObservation = observation;
        }

        if (current.HoldingBody)
            visualState.ObserveHeldBodyDirection(y, tail, current.HeadReached, resolveBodyDirection);
    }

    protected override void ResetVisualState()
    {
        lastVisualObservation = null;
        visualState.Reset();
        bodyGeometryValid = false;
        longNoteBody.ResetBody();
        longNoteBody.Alpha = 0;
        longNoteTail.Alpha = 0;
    }

    protected override void ApplyVisualHeightScale(float scale)
    {
        head.Scale = new Vector2(1, scale);
        noteHeightScale = scale;
        longNoteTail.Scale = new Vector2(1, scale);
    }

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null)
        {
            mapPresentation = BmsLongNotePresentation.SelectMapping(ln.Beatmap.LockedLongNoteMode);
            ParentColumn!.JudgementBridge.ObserveLongNoteState(this, stateObserver);
            ParentColumn.JudgementBridge.ObserveVisualTime(this, visualTimeObserver);
            presentation = readPresentation(Time.Current);
            restoreHeadPin = presentation.RestoreHeadPin;

            longNoteBody.SetSkinLookup(LayoutVariant, Column);
            ParentColumn.JudgementBridge.RefreshNoteRepresentation(this);
        }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Visuals.AddRange([
            longNoteBody = new BmsSegmentedLongNoteBody
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                BodyColour = Color4.Cyan,
                Alpha = 0,
            },
            longNoteTail = new BmsCachedSkinnableDrawable(
                new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail,
                    LayoutVariant, Column))
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
                ComponentAnchor = Anchor.BottomCentre,
            },
        ]);
        Visuals.Add(head = new BmsNoteVisual(BmsSkinComponents.HoldNoteHead, LayoutVariant, Column));
    }

    internal override void RestoreRewoundVisuals()
    {
        lastVisualObservation = null;
        visualState.Reset();
        bodyGeometryValid = false;
        lastHoldExplosionTime = Time.Current;
        presentation = readPresentation(Time.Current);
        restoreHeadPin = presentation.RestoreHeadPin;
        RefreshStateTransforms();
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state == ArmedState.Hit && HitObject != null)
        {
            var current = readPresentation(Time.Current);
            Alpha = current.KeepVisibleAfterHit ? 1 : 0;
            LifetimeEnd = current.KeepVisibleAfterHit ? current.LifetimeEnd : Time.Current;
            return;
        }

        base.UpdateHitStateTransforms(state);
        BmsNoteVisual.ApplyResultEffects(this, state, Time.Current);
    }

    private BmsLongNotePresentation readPresentation(double time)
        => mapPresentation(ln, ParentColumn!.Judgements.GetLongNoteState(ln), presentationContext(time));

    private BmsLongNotePresentationContext presentationContext(double time)
        => new(time, ParentColumn?.Judgements.PressedDirections is > 0);

    private void applyPresentation(in BmsLongNoteState snapshot, in BmsFrameContext frame)
    {
        var previous = presentation;
        presentation = mapPresentation(ln, snapshot, presentationContext(frame.SimulationTime));
        if (!previous.HeadJudged && presentation.PrepareHeadPin)
        {
            visualState.PrepareHeadPin();
            lastHoldExplosionTime = Time.Current - hold_explosion_interval;
        }

        if (presentation.KeepAlive)
        {
            Alpha = 1;
            LifetimeEnd = presentation.LifetimeEnd;
        }

        if (presentation.Retired)
        {
            // Settled LN/CN tails clear immediately; body expiry retains its final geometry for rewind.
            if (presentation.ClearBodyOnRetire)
            {
                visualState.Reset();
                longNoteBody.Alpha = 0;
                longNoteTail.Alpha = 0;
            }

            Alpha = 0;
            LifetimeEnd = frame.SimulationTime;
        }
    }

    private int bodyDirectionBeforeTailPasses(float realHeadY, float realTailY) => HitObject == null
        ? Math.Sign(realTailY - realHeadY)
        : BmsLongNoteGeometry.BodyDirectionBeforeTailPasses(
            ((BmsLongNote)HitObject).ScrollPositionAtEndTime - HitObject.ScrollPositionAtStartTime,
            ln.Duration,
            ScrollSpeedMultiplier,
            realHeadY,
            realTailY);

    protected override JudgementResult CreateResult(osu.Game.Rulesets.Judgements.Judgement judgement)
        => new BmsLongNoteJudgementResult(HitObject, judgement);
}
