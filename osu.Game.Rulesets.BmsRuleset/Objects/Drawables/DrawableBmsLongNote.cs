using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLongNote : DrawableBmsHitObject
{
    private const float max_piece_height = 4096;

    private bool longNoteStarted;
    private HitResult? longNoteHeadResult;
    private float? longNoteHeadFixedY;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.HoldNoteHead;

    public bool IsHoldingLongNote => longNoteStarted && !Judged;

    public override bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null || longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        longNoteStarted = true;
        longNoteHeadResult = result;
        longNoteHeadFixedY = null;
        tryResolveLongNoteHeadFixedY(result);
        return true;
    }

    public bool TryRelease()
    {
        if (Judged || HitObject?.HitWindows == null || !longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.EndTime);

        if (result == HitResult.None)
        {
            if (Time.Current > HitObject.EndTime + bmsWindows.WindowFor(HitResult.Ok))
                return false;

            ApplyResult(HitResult.Meh);
            return true;
        }

        ApplyResult(result);
        return true;
    }

    protected override void ResetKindState()
    {
        longNoteStarted = false;
        longNoteHeadResult = null;
        longNoteHeadFixedY = null;
    }

    protected override void OnLayoutMetricsRefreshed(LayoutMetrics layout) => tryResolveLongNoteHeadFixedY(longNoteHeadResult);

    protected override void OnLayoutResolved(BmsLayoutVariant layoutVariant, int column) => longNoteBody.SetSkinLookup(layoutVariant, column);

    protected override void AddKindDrawablesBeforeNote()
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
        ]);
    }

    protected override void UpdateSkinPieces(BmsLayoutVariant? layoutVariant = null, int? column = null)
    {
        base.UpdateSkinPieces(layoutVariant, column);

        if (HitObject == null)
            return;

        var resolvedLayoutVariant = layoutVariant ?? Playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
        var resolvedColumn = column ?? HitObject.Column;

        longNoteBody.BodyColour = Color4.Cyan;
        longNoteBody.Alpha = 0.65f;
        longNoteTailContainer.Alpha = 1;
        longNoteTailContainer.Clear();
        longNoteTailContainer.Add(new BmsCachedSkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, resolvedLayoutVariant, resolvedColumn))
        {
            RelativeSizeAxes = Axes.Both,
            CentreComponent = false,
        });
    }

    protected override float VisualHeadYFor(float naturalY, float judgementHeadY)
    {
        if (!longNoteStarted)
            return naturalY;

        return Math.Min(longNoteHeadFixedY ?? naturalY, judgementHeadY);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null)
            return;

        var missWindow = HitObject.HitWindows.WindowFor(HitResult.Ok);

        if (!longNoteStarted && Time.Current > HitObject.StartTime + missWindow)
        {
            ApplyResult(HitResult.Meh);
            return;
        }

        if (longNoteStarted && Time.Current > HitObject.EndTime + missWindow)
            ApplyResult(HitResult.Meh);
    }

    protected override VisualLayout CreateVisualLayout(float visualY, LayoutMetrics layout)
    {
        var endY = YForTimeOffset(HitObject.EndTime - Time.Current, layout);
        var visualEndY = longNoteStarted ? Math.Min(endY, visualY) : endY;
        var objectTop = Math.Min(visualY, visualEndY);

        return new VisualLayout(
            objectTop,
            visualY - objectTop,
            visualEndY - objectTop,
            Math.Max(CurrentNoteHeight, Math.Abs(visualEndY - visualY) + CurrentNoteHeight));
    }

    protected override double GetScrollPositionForOffset(double timeUntilHit)
    {
        if (Math.Abs(timeUntilHit - (HitObject.EndTime - Time.Current)) < 0.001)
            return HitObject.ScrollPositionAtEndTime;

        return base.GetScrollPositionForOffset(timeUntilHit);
    }

    protected override void UpdateVisualPieces(float headOffset, float tailOffset)
    {
        VisualPiecesApplied = true;

        if (Math.Abs(NoteContainer.Y - headOffset) > 0.5f)
            NoteContainer.Y = headOffset;

        if (Math.Abs(NoteContainer.Height - CurrentNoteHeight) > 0.5f)
            NoteContainer.Height = CurrentNoteHeight;

        var tailAtTop = tailOffset < headOffset;
        var bodyTop = Math.Min(headOffset, tailOffset);
        var bodyBottom = Math.Max(headOffset, tailOffset) + CurrentNoteHeight;

        var visibleTop = Math.Max(bodyTop, headOffset - max_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        if (Math.Abs(longNoteBody.Y - visibleTop) > 0.5f)
            longNoteBody.Y = visibleTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(1, bodyHeight);

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, longNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;

        if (Math.Abs(longNoteTailContainer.Y - tailOffset) > 0.5f)
            longNoteTailContainer.Y = tailOffset;

        if (Math.Abs(longNoteTailContainer.Height - CurrentNoteHeight) > 0.5f)
            longNoteTailContainer.Height = CurrentNoteHeight;

        longNoteTailContainer.Alpha = 1;
    }

    private void tryResolveLongNoteHeadFixedY(HitResult? result)
    {
        if (result == null || longNoteHeadFixedY != null || LatestLayout is not { } layout)
            return;

        longNoteHeadFixedY = result is HitResult.Perfect or HitResult.Great
            ? JudgementHeadYFor(layout)
            : YForTimeOffset(HitObject.StartTime - Time.Current, layout);
    }
}
