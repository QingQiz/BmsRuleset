using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLongNote<TCol> : DrawableBmsHitObject<TCol>, ILongNoteHolder
    where TCol : struct, IColumnProvider
{

    public bool IsHoldingLongNote => longNoteStarted && !Judged;

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.HoldNoteHead;

    private bool longNoteStarted;
    private double headJudgeOffset;
    private float? longNoteHeadFixedY;
    private BmsSegmentedLongNoteBody longNoteBody = null!;
    private Container longNoteTailContainer = null!;

    public override bool TryHit(HitResult result)
    {
        if (Judged || HitObject == null || longNoteStarted || result == HitResult.None)
            return false;

        longNoteStarted = true;
        headJudgeOffset = Time.Current - HitObject.StartTime;
        longNoteHeadFixedY = -(Playfield?.Stage.HitTargetPosition ?? 200);
        return true;
    }

    public bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable)
    {
        if (Judged || HitObject == null || !longNoteStarted)
            return false;

        applyReleaseResult(tailTable, releaseOffset);
        return true;
    }

    private void applyReleaseResult(BmsJudgementWindowTable tailTable, double tailOffset)
    {
        var heldOffset = Math.Abs(headJudgeOffset) > Math.Abs(tailOffset) ? headJudgeOffset : tailOffset;
        var result = tailTable.ResultForOffset(heldOffset);
        ApplyResult(result == HitResult.None ? HitResult.Meh : result);
    }

    /// <summary>
    /// Called by BmsColumnHitObjectContainer every frame with pre-computed
    /// head and end Y positions. Updates the body/tail visual geometry.
    /// </summary>
    public void UpdateBodyGeometry(float headY, float endY)
    {
        const float max_piece_height = 4096;

        // If head is frozen at a fixed position (held LN), use that instead.
        // Math.Min works in normal scroll (head moves downward → freeze at the higher/fixed Y),
        // but in reverse scroll the head moves upward, so Min would pick the moving headY.
        // Always use the captured fixed Y to freeze correctly in both directions.
        if (longNoteStarted && longNoteHeadFixedY.HasValue)
            headY = longNoteHeadFixedY.Value;

        var myY = Y;
        var headOffset = headY - myY;
        var tailOffset = endY - myY;

        // Position head
        if (Math.Abs(NoteContainer.Y - headOffset) > 0.5f)
            NoteContainer.Y = headOffset;

        // Compute body geometry
        var tailAtTop = tailOffset < headOffset;
        var bodyTop = Math.Min(headOffset, tailOffset);
        var bodyBottom = Math.Max(headOffset, tailOffset);

        var visibleTop = Math.Max(bodyTop, headOffset - max_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_piece_height);
        var bodyHeight = Math.Max(0, visibleBottom - visibleTop);

        if (Math.Abs(longNoteBody.Y - visibleTop) > 0.5f)
            longNoteBody.Y = visibleTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(1, bodyHeight);

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, longNoteStarted);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;

        // Position tail
        if (Math.Abs(longNoteTailContainer.Y - tailOffset) > 0.5f)
            longNoteTailContainer.Y = tailOffset;

        if (Math.Abs(longNoteTailContainer.Height - Height) > 0.5f)
            longNoteTailContainer.Height = Height;

        longNoteTailContainer.Alpha = 1;
    }

    protected override void ResetKindState()
    {
        longNoteStarted = false;
        headJudgeOffset = 0;
        longNoteHeadFixedY = null;

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
            if (headTable.IsPastPassivePoorOffset(Time.Current - HitObject.StartTime))
                ApplyResult(HitResult.Meh);

            return;
        }

        var tailTable = BmsJudgementProfileProvider.GetTable(Playfield.LayoutVariant, HitObject.Column, HitObject.BmsRank, tail: true);
        var tailOffset = Time.Current - HitObject.EndTime;

        if (tailOffset >= 0)
        {
            applyReleaseResult(tailTable, tailOffset);
        }
    }
}
