using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLongNote<TCol> : DrawableBmsHitObject<TCol>, ILongNoteHolder
    where TCol : struct, IColumnProvider
{

    public bool IsHoldingLongNote => longNoteStarted && !tailJudged;

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.HoldNoteHead;

    private BmsLongNoteMode mode => HitObject?.LongNoteMode == BmsLongNoteMode.Undefined
        ? BmsLongNoteMode.LongNote
        : HitObject!.LongNoteMode;

    private bool isChargeMode => mode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote;

    private const double passive_poor_lifetime_margin = 100;
    private const double tail_visibility_grace = 50;

    private readonly BmsLongNoteVisualState visualState = new();
    private readonly BmsHellChargeBodyTracker hellChargeTracker = new();

    private bool headJudged;
    private bool longNoteStarted;
    private double headJudgeOffset;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;

    private bool tailJudged;

    public override bool TryHit(HitResult result)
    {
        if (Judged || HitObject == null || headJudged || longNoteStarted || result == HitResult.None)
            return false;

        if (mode == BmsLongNoteMode.HellChargeNote && result == HitResult.Meh)
        {
            startHellChargeBodyAfterHeadPoor();
            return true;
        }

        headJudged = true;
        longNoteStarted = result != HitResult.Meh;
        headJudgeOffset = Time.Current - HitObject.StartTime;
        pinVisualHeadToJudgementLine();
        hellChargeTracker.Reset();

        // CN/HCN score the head and tail as separate beatoraja-style events.
        if (isChargeMode)
            ApplyResult(result);

        return true;
    }

    public bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable)
    {
        if (HitObject == null || !longNoteStarted || tailJudged)
            return false;

        if (!isChargeMode)
        {
            if (Judged)
                return false;

            applyLongNoteReleaseResult(tailTable, releaseOffset);
            return true;
        }

        if (mode == BmsLongNoteMode.HellChargeNote)
            hellChargeTracker.MarkReleased();

        applyChargeTailResult(tailTable, releaseOffset, Time.Current);
        return true;
    }

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

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, longNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;
        longNoteBody.Colour = shouldGreyBody() ? new Color4(128, 128, 128, 255) : Color4.White;

        if (Math.Abs(longNoteTailContainer.Y - tailOffset) > 0.5f)
            longNoteTailContainer.Y = tailOffset;

        if (Math.Abs(longNoteTailContainer.Height - Height) > 0.5f)
            longNoteTailContainer.Height = Height;

        longNoteTailContainer.Alpha = 1;
    }

    protected override void ResetKindState()
    {
        headJudged = false;
        longNoteStarted = false;
        tailJudged = false;
        headJudgeOffset = 0;
        hellChargeTracker.Reset();
        visualState.Reset();

        longNoteBody.Alpha = 0;
        longNoteTailContainer.Alpha = 0;
    }

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null && Playfield != null)
            longNoteBody.SetSkinLookup(Playfield.LayoutVariant, Column);
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
                    new BmsCachedSkinnableDrawable(
                        new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail,
                            Playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K, Column))
                    {
                        ComponentAnchor = Anchor.BottomCentre,
                    },
                ],
            },
        ]);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject == null || Playfield == null)
            return;

        var headTable = BmsJudgementProfileProvider.GetTable(Playfield.LayoutVariant, HitObject.Column, HitObject.BmsRank, tail: false);

        if (!longNoteStarted)
        {
            if (!headTable.IsPastPassivePoorOffset(Time.Current - HitObject.StartTime))
                return;

            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                startHellChargeBodyAfterHeadPoor();
                return;
            }

            if (isChargeMode)
            {
                // CN: missed head -> POOR for head, then another POOR for tail
                ApplyResult(HitResult.Meh);
                Playfield?.RegisterLongNoteEndpoint(this, HitObject.EndTime, Time.Current, HitResult.Meh);
                tailJudged = true;
                return;
            }

            ApplyResult(HitResult.Meh);
            tailJudged = true;
            return;
        }

        var tailTable = BmsJudgementProfileProvider.GetTable(Playfield.LayoutVariant, HitObject.Column, HitObject.BmsRank, tail: true);
        var tailOffset = Time.Current - HitObject.EndTime;

        if (isChargeMode)
        {
            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, Time.Current);

            return;
        }

        if (tailOffset >= 0)
            applyLongNoteReleaseResult(tailTable, tailOffset);
    }

    // Keep CN/HCN visuals alive after head judgement
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state == ArmedState.Hit && HitObject != null && Time.Current < HitObject.EndTime)
        {
            Alpha = 1;
            LifetimeEnd = isChargeMode ? chargeTailLifetimeEnd() : HitObject.EndTime;
            return;
        }

        if (isChargeMode && state == ArmedState.Hit && !tailJudged)
        {
            Alpha = 1;

            if (HitObject != null)
                LifetimeEnd = chargeTailLifetimeEnd();

            return;
        }

        base.UpdateHitStateTransforms(state);
    }

    protected override void Update()
    {
        base.Update();

        if (HitObject == null || Playfield == null)
            return;

        if (isChargeMode && longNoteStarted && !tailJudged)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(Playfield.LayoutVariant, HitObject.Column, HitObject.BmsRank, tail: true);
            var tailOffset = Time.Current - HitObject.EndTime;

            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, Time.Current);
        }

        if (longNoteStarted && Time.Current > HitObject.EndTime + tail_visibility_grace && (!isChargeMode || tailJudged))
        {
            longNoteStarted = false;
            this.FadeOut();
            LifetimeEnd = Time.Current;
            return;
        }

        if (mode != BmsLongNoteMode.HellChargeNote || !longNoteStarted)
            return;

        if (Time.Current < HitObject.StartTime || Time.Current > HitObject.EndTime)
            return;

        var elapsed = boundedHellChargeElapsed();

        if (elapsed <= 0)
            return;

        var holding = Playfield.IsColumnPressed(HitObject.Column);
        hellChargeTracker.Update(elapsed, holding, Playfield.ApplyHellChargeTick);
    }

    private void applyLongNoteReleaseResult(BmsJudgementWindowTable tailTable, double tailOffset)
    {
        var heldOffset = Math.Abs(headJudgeOffset) > Math.Abs(tailOffset) ? headJudgeOffset : tailOffset;
        var result = tailTable.ResultForOffset(heldOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        ApplyResult(endpointResult);
        tailJudged = true;
        clearVisualIfTailWasNotPoor(endpointResult);
    }

    private void applyChargeTailResult(BmsJudgementWindowTable tailTable, double tailOffset, double eventTime)
    {
        if (HitObject == null || tailJudged)
            return;

        var result = tailTable.ResultForOffset(tailOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;
        Playfield?.RegisterLongNoteEndpoint(this, HitObject.EndTime, eventTime, endpointResult);
        tailJudged = true;
        clearVisualIfTailWasNotPoor(endpointResult);
    }

    private bool shouldGreyBody()
        => longNoteStarted
           && HitObject != null
           && Time.Current < HitObject.EndTime
           && !isHoldingBody();

    private bool isHoldingBody()
        => longNoteStarted
           && HitObject != null
           && Playfield?.IsColumnPressed(HitObject.Column) == true;

    private int bodyDirectionBeforeTailPasses(float realHeadY, float realTailY)
    {
        if (HitObject == null)
            return Math.Sign(realTailY - realHeadY);

        return BmsLongNoteGeometry.BodyDirectionBeforeTailPasses(
            HitObject.ScrollPositionAtEndTime - HitObject.ScrollPositionAtStartTime,
            HitObject.Duration,
            Playfield?.ScrollSpeedMultiplier ?? 1,
            realHeadY,
            realTailY);
    }

    private void clearVisualIfTailWasNotPoor(HitResult tailResult)
    {
        if (tailResult == HitResult.Meh)
            return;

        longNoteStarted = false;
        visualState.Reset();
        longNoteBody.Alpha = 0;
        longNoteTailContainer.Alpha = 0;
        this.FadeOut();
        LifetimeEnd = Time.Current;
    }

    private void startHellChargeBodyAfterHeadPoor()
    {
        if (HitObject == null || headJudged)
            return;

        headJudged = true;
        longNoteStarted = true;
        headJudgeOffset = Time.Current - HitObject.StartTime;
        pinVisualHeadToJudgementLine();
        hellChargeTracker.Reset();
        Alpha = 1;
        LifetimeEnd = chargeTailLifetimeEnd();

        Playfield?.RegisterLongNoteHead(this, Time.Current, HitResult.Meh);
    }

    private void pinVisualHeadToJudgementLine() => visualState.PinHead(-(Playfield?.Stage.HitTargetPosition ?? 200));

    private double chargeTailLifetimeEnd()
    {
        if (HitObject == null)
            return Time.Current;

        var tailTable = BmsJudgementProfileProvider.GetTable(
            Playfield?.LayoutVariant ?? HitObject.LayoutVariant,
            HitObject.Column,
            HitObject.BmsRank,
            tail: true);

        return HitObject.EndTime + tailTable.LateWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    private double boundedHellChargeElapsed()
    {
        if (HitObject == null)
            return 0;

        var frameStart = Time.Current - Time.Elapsed;
        var start = Math.Max(frameStart, HitObject.StartTime);
        var end = Math.Min(Time.Current, HitObject.EndTime);

        return Math.Max(0, end - start);
    }
}
