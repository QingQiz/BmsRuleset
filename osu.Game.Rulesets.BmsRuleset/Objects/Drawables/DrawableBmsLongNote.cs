using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLongNote<TCol> : DrawableBmsHitObject<TCol>, ILongNoteHolder, IBmsLongNoteHooks
    where TCol : struct, IColumnProvider
{
    public bool IsHoldingLongNote => controller.LongNoteStarted && !controller.TailJudged;

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.HoldNoteHead;

    private BmsLongNote ln => (BmsLongNote)HitObject;

    // Re-trigger the LN hit light this often while holding so the explosion pulses throughout the
    // hold instead of only firing at the head and tail endpoints.
    private const double hold_explosion_interval = BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION;

    // Body+tail fade to this alpha when the head is judged but the key is released before the tail judged
    private const float released_alpha = 0.4f;

    private readonly BmsLongNoteVisualState visualState = new();
    private readonly BmsLongNoteJudgementController controller = new();

    private double lastHoldExplosionTime;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;
    private BmsCachedSkinnableDrawable longNoteTail = null!;

    [Resolved(CanBeNull = true)]
    private IBmsLnScoring? scoring { get; set; }

    private double gameplayRate => (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate;

    public override bool TryHit(HitResult result)
    {
        if (Judged || HitObject == null)
            return false;

        return controller.TryHit(Time.Current, result, gameplayRate);
    }

    public bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable)
        => HitObject != null && controller.TryRelease(Time.Current, releaseOffset, tailTable, gameplayRate);

    /// <summary>
    /// Called by BmsColumnHitObjectContainer every frame with pre-computed
    /// head and end Y positions. Updates the body/tail visual geometry.
    /// </summary>
    public void UpdateBodyGeometry(float headY, float endY)
    {
        const float max_piece_height = 4096;
        var holdingBody = isHoldingBody();

        // A held LN should visually stay attached to the judgement line until its tail passes it.
        if (holdingBody)
            headY = visualState.ResolveHeldHeadY(headY, endY, bodyDirectionBeforeTailPasses);

        var myY = Y;
        var headOffset = headY - myY;
        var tailOffset = endY - myY;
        var bodyTailOffset = holdingBody
            ? visualState.VisibleBodyTailOffset(headOffset, tailOffset)
            : tailOffset;

        if (Math.Abs(NoteContainer.Y - headOffset) > 0.5f)
            NoteContainer.Y = headOffset;

        var tailAtTop = bodyTailOffset < headOffset;
        var bodyTop = Math.Min(headOffset, bodyTailOffset);
        var bodyBottom = Math.Max(headOffset, bodyTailOffset);

        var visibleTop = Math.Max(bodyTop, headOffset - max_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        if (Math.Abs(longNoteBody.Y - visibleTop) > 0.5f)
            longNoteBody.Y = visibleTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(1, bodyHeight);

        var releasedEarly =
            controller.LongNoteStarted && HitObject != null && Time.Current < ln.EndTime && !holdingBody;
        longNoteBody.UpdateBody(bodyHeight, tailAtTop, controller.LongNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? releasedEarly ? released_alpha : 1f : 0;

        if (Math.Abs(longNoteTailContainer.Y - tailOffset) > 0.5f)
            longNoteTailContainer.Y = tailOffset;

        if (Math.Abs(longNoteTailContainer.Height - Height) > 0.5f)
            longNoteTailContainer.Height = Height;

        longNoteTailContainer.Alpha = releasedEarly ? released_alpha : 1f;
    }

    protected override void ResetKindState()
    {
        controller.Reset();
        visualState.Reset();
        longNoteBody.Alpha = 0;
        longNoteTailContainer.Alpha = 0;
    }

    protected override void ApplyNoteHeightScaleToKind(float scale) => longNoteTail.Scale = new Vector2(1, scale);

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null)
        {
            controller.Bind((BmsLongNote)HitObject, this);
            longNoteBody.SetSkinLookup(LayoutVariant, Column);
        }
    }

    protected override void AddKindDrawablesBeforeNote()
    {
        AddRangeInternal([
            longNoteBody = new BmsSegmentedLongNoteBody
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                BodyColour = Color4.Cyan,
                Alpha = 0,
            },
            longNoteTailContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
                Children =
                [
                    longNoteTail = new BmsCachedSkinnableDrawable(
                        new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail,
                            LayoutVariant, Column))
                    {
                        ComponentAnchor = Anchor.BottomCentre,
                    },
                ],
            },
        ]);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject == null)
            return;

        controller.CheckPassiveResult(Time.Current, gameplayRate);
    }

    // Keep CN/HCN visuals alive after head judgement
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state == ArmedState.Hit && HitObject != null && Time.Current < ln.EndTime)
        {
            Alpha = 1;
            LifetimeEnd = controller.IsChargeMode ? controller.ChargeTailLifetimeEnd() : ln.EndTime;
            return;
        }

        if (controller.IsChargeMode && state == ArmedState.Hit && !controller.TailJudged)
        {
            Alpha = 1;

            if (HitObject != null)
                LifetimeEnd = controller.ChargeTailLifetimeEnd();

            return;
        }

        base.UpdateHitStateTransforms(state);
    }

    protected override void UpdateKindPostResultState()
    {
        if (HitObject == null)
            return;

        // Hold-explosion pulse runs first, matching the original per-frame order (pulse, then
        // charge-tail passive miss, retire, HCN tick). It reads pre-mutation controller state.
        if (controller.LongNoteStarted && !controller.TailJudged
                                       && Time.Current >= HitObject.StartTime && Time.Current <= ln.EndTime
                                       && Time.Current - lastHoldExplosionTime >= hold_explosion_interval)
        {
            ParentColumn?.TriggerHitExplosion(true);
            lastHoldExplosionTime = Time.Current;
        }

        controller.UpdatePostResult(Time.Current, Time.Elapsed, ParentColumn?.IsPressed == true, gameplayRate);
    }

    private bool isHoldingBody()
        => HitObject != null && controller.ShouldShowHeldVisual(ParentColumn?.IsPressed == true);

    private int bodyDirectionBeforeTailPasses(float realHeadY, float realTailY) => HitObject == null
        ? Math.Sign(realTailY - realHeadY)
        : BmsLongNoteGeometry.BodyDirectionBeforeTailPasses(
            ((BmsLongNote)HitObject).ScrollPositionAtEndTime - HitObject.ScrollPositionAtStartTime,
            ln.Duration,
            ScrollSpeedMultiplier,
            realHeadY,
            realTailY);

    // --- IBmsLongNoteHooks: side-effects driven by the judgement controller. ---

    void IBmsLongNoteHooks.OnUserHeadJudged()
    {
        visualState.PinHead(-HitTargetPosition);
        lastHoldExplosionTime = Time.Current - hold_explosion_interval;
    }

    void IBmsLongNoteHooks.OnHellChargeHeadPoor(double eventTime, double lifetimeEnd)
    {
        visualState.PinHead(-HitTargetPosition);
        Alpha = 1;
        LifetimeEnd = lifetimeEnd;
    }

    void IBmsLongNoteHooks.ApplyJudgementResult(HitResult result, System.Collections.Generic.IReadOnlyList<BmsLongNoteEndpointResult> endpoints)
    {
        ((BmsLongNoteJudgementResult)Result).SetEndpointResults(endpoints);
        ApplyResult(result);
    }

    void IBmsLongNoteHooks.ApplySyntheticEndpoint(HitResult result, BmsLongNoteEndpointResult endpoint)
        => scoring?.ApplySyntheticLongNoteEndpoint(this, endpoint);

    void IBmsLongNoteHooks.ClearVisualIfTailWasNotPoor(HitResult tailResult)
    {
        if (tailResult == HitResult.Meh)
            return;

        visualState.Reset();
        longNoteBody.Alpha = 0;
        longNoteTailContainer.Alpha = 0;
        this.FadeOut();
        LifetimeEnd = Time.Current;
    }

    void IBmsLongNoteHooks.ApplyHellChargeTick(bool holding, double scale)
        => scoring?.ApplyHellChargeTick(holding, scale);

    void IBmsLongNoteHooks.Retire()
    {
        this.FadeOut();
        LifetimeEnd = Time.Current;
    }

    protected override JudgementResult CreateResult(Judgement judgement)
        => new BmsLongNoteJudgementResult(HitObject, judgement);
}
