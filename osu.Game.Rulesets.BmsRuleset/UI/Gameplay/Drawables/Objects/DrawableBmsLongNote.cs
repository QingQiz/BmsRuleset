using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLongNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.HoldNoteHead;

    private BmsLongNote ln => (BmsLongNote)HitObject;

    // Re-trigger the LN hit light this often while holding so the explosion pulses throughout the
    // hold instead of only firing at the head and tail endpoints.
    private const double hold_explosion_interval = BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION;

    // Body+tail fade to this alpha when the head is judged but the key is released before the tail judged
    private const float released_alpha = 0.4f;

    private readonly BmsLongNoteVisualState visualState = new();
    private readonly Func<float, float, int> resolveBodyDirection;
    private BmsLongNoteState state => ParentColumn!.Judgements.GetLongNoteState(ln);
    private BmsLongNotePresentation presentation;
    private BmsLongNotePresentationMapping mapPresentation = null!;
    private readonly BmsLongNoteStateObserver stateObserver;

    private double lastHoldExplosionTime;
    private bool restoreHeadPin;
    private bool bodyGeometryValid;
    private float lastHeadOffset;
    private float lastBodyTailOffset;
    private float lastTailOffset;
    private float lastBodyWidth;
    private float lastVisualNoteHeight;
    private float noteHeightScale = 1;
    private bool lastHoldingBody;
    private bool lastReleasedFast;
    private double lastSimulationTime = double.NaN;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private BmsCachedSkinnableDrawable longNoteTail = null!;

    internal override void UpdateDeferredVisuals(float y, float endY)
    {
        // Pin the held head at the simulation timestamp even when skins wait for the final frame.
        GetVisualHeadY(y, endY);
        UpdateColumnFrame();
    }

    public DrawableBmsLongNote()
    {
        // A held note resolves its visual head for both culling and geometry every frame.
        // Keep the instance delegate across pool reuse instead of allocating at each call.
        resolveBodyDirection = bodyDirectionBeforeTailPasses;
        stateObserver = applyPresentation;
    }

    /// <summary>
    /// Called by BmsColumnHitObjectContainer every frame with pre-computed
    /// head and end Y positions. Updates the body/tail visual geometry.
    /// </summary>
    internal override void UpdateVisualGeometry(float headY, float endY)
    {
        if (VisualsSuppressed)
        {
            bodyGeometryValid = false;
            return;
        }

        const float max_piece_height = 4096;
        var holdingBody = isHoldingBody();
        headY = GetVisualHeadY(headY, endY);

        var myY = Y;
        var headOffset = headY - myY;
        var tailOffset = endY - myY;
        var bodyTailOffset = holdingBody
            ? visualState.VisibleBodyTailOffset(headOffset, tailOffset)
            : tailOffset;
        var visualNoteHeight = NoteVisualHeight * noteHeightScale;

        var releasedFast =
            state.LongNoteStarted && HitObject != null && Time.Current < ln.EndTime && !holdingBody;
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
            longNoteBody.UpdateAnimation(state.LongNoteStarted);
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

        if (Math.Abs(NoteContainer.Y - headOffset) > 0.5f)
            NoteContainer.Y = headOffset;

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

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, state.LongNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? releasedFast ? released_alpha : 1f : 0;

        if (Math.Abs(longNoteTail.Y - tailOffset) > 0.5f)
            longNoteTail.Y = tailOffset;

        if (Math.Abs(longNoteTail.Height - Height) > 0.5f)
            longNoteTail.Height = Height;

        longNoteTail.Alpha = releasedFast ? released_alpha : 1f;
    }

    protected override float GetVisualHeadY(float y, float endY)
    {
        if (restoreHeadPin)
        {
            // Seeking into a hold can reactivate it without observing its head cross the line.
            // Reconstruct that position once, including visual offset, before resuming the pin.
            if (Time.Current >= HitObject.StartTime && ParentColumn != null)
                visualState.RestoreHeadYAtStartTime(ParentColumn.GetLongNoteHeadYAtStartTime(ln));
            restoreHeadPin = false;
        }

        // Observe positions even while culled so a held head can re-enter at its pinned position.
        visualState.UpdateHeadYAtStartTime(y, Time.Current, HitObject.StartTime, ParentColumn?.VisualOffset ?? 0);
        return isHoldingBody()
            ? visualState.ResolveHeldHeadY(y, endY, Time.Current >= HitObject.StartTime, resolveBodyDirection)
            : y;
    }

    protected override void ResetKindState()
    {
        lastSimulationTime = double.NaN;
        visualState.Reset();
        bodyGeometryValid = false;
        longNoteBody.ResetBody();
        longNoteBody.Alpha = 0;
        longNoteTail.Alpha = 0;
    }

    protected override void ApplyNoteHeightScaleToKind(float scale)
    {
        noteHeightScale = scale;
        longNoteTail.Scale = new Vector2(1, scale);
    }

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null)
        {
            mapPresentation = BmsLongNotePresentation.SelectMapping(ln.Beatmap.LockedLongNoteMode);
            ParentColumn!.Judgements.ObserveLongNoteState(this, stateObserver);
            presentation = mapPresentation(ln, state, isKeyHeld(), Time.Current);
            restoreHeadPin = state.LongNoteStarted;

            longNoteBody.SetSkinLookup(LayoutVariant, Column);
            ParentColumn.Judgements.RefreshNoteRepresentation(this);
        }
    }

    protected override void AddKindDrawablesBeforeNote()
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
    }

    internal override void RestoreRewoundState()
    {
        lastSimulationTime = double.NaN;
        visualState.Reset();
        restoreHeadPin = state.LongNoteStarted;
        bodyGeometryValid = false;
        lastHoldExplosionTime = Time.Current;
        presentation = mapPresentation(ln, state, isKeyHeld(), Time.Current);
        RefreshStateTransforms();
    }

    // Keep CN/HCN visuals alive after head judgement
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state == ArmedState.Hit && HitObject != null && Time.Current < ln.EndTime)
        {
            Alpha = 1;
            LifetimeEnd = presentation.LifetimeEnd;
            return;
        }

        if (presentation.KeepAfterHeadResult && state == ArmedState.Hit && !this.state.TailJudged)
        {
            Alpha = 1;

            if (HitObject != null)
                LifetimeEnd = presentation.LifetimeEnd;

            return;
        }

        if (state == ArmedState.Hit)
        {
            Alpha = 0;
            LifetimeEnd = Time.Current;
            return;
        }

        base.UpdateHitStateTransforms(state);
    }

    protected override void UpdateKindPostResultState()
    {
        if (HitObject == null)
            return;

        // The final visual traversal can revisit the same simulation timestamp. HCN elapsed
        // time and hold-light pulses must only be applied once at that timestamp.
        if (lastSimulationTime == Time.Current)
            return;

        lastSimulationTime = Time.Current;

        if (state.LongNoteStarted && !state.TailJudged
                                       && Time.Current >= HitObject.StartTime && Time.Current <= ln.EndTime
                                       && Time.Current - lastHoldExplosionTime >= hold_explosion_interval)
        {
            ParentColumn?.TriggerHitExplosion(presentation.HitLightStyle);
            lastHoldExplosionTime = Time.Current;
        }
    }

    private bool isHoldingBody()
    {
        presentation = mapPresentation(ln, state, isKeyHeld(), Time.Current);
        return presentation.HoldingBody;
    }

    private bool isKeyHeld() => ParentColumn?.IsPressed == true;

    private void applyPresentation(in BmsLongNoteState snapshot, in BmsFrameContext frame)
    {
        var previous = presentation;
        presentation = mapPresentation(ln, snapshot, isKeyHeld(), frame.SimulationTime);
        if (frame.IsPauseCompletion)
            return;

        if (!previous.HeadJudged && presentation.HeadJudged)
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
            visualState.Reset();
            longNoteBody.Alpha = 0;
            longNoteTail.Alpha = 0;
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

    protected override JudgementResult CreateResult(Judgement judgement)
        => new BmsLongNoteJudgementResult(HitObject, judgement);
}
