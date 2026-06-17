using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLongNote : DrawableBmsHitObject
{
    private bool longNoteStarted;
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
        // Freeze at the judgement line position, not at the early/late-hit Y,
        // so the LN head stays at the correct screen position during hold.
        longNoteHeadFixedY = -(Playfield?.Stage.HitTargetPosition ?? 200);
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
        longNoteHeadFixedY = null;

        longNoteBody.Alpha = 0;
        longNoteTailContainer.Alpha = 0;
    }

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null && Playfield != null)
        {
            longNoteBody.SetSkinLookup(Playfield.LayoutVariant, HitObject.Column);

            if (longNoteTailContainer.Count == 0)
            {
                longNoteTailContainer.Add(new BmsCachedSkinnableDrawable(
                    new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail,
                        Playfield.LayoutVariant, HitObject.Column))
                {
                    ComponentAnchor = Anchor.BottomCentre,
                });
            }
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
            },
        ]);
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
}
