using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

internal sealed class BmsLongNoteVisualState
{
    private int? heldBodyDirection;

    public void PrepareHeadPin() => heldBodyDirection = null;

    public float ResolveHeldHeadY(float realHeadY, float realTailY, float pinnedHeadY, bool canPin, Func<float, float, int> directionResolver)
    {
        if (heldBodyDirection == null)
        {
            if (!canPin)
                return realHeadY;

            heldBodyDirection = directionResolver(realHeadY, realTailY);
        }

        return pinnedHeadY;
    }

    public float VisibleBodyTailOffset(float headOffset, float tailOffset)
        => BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset, tailOffset, heldBodyDirection);

    public void Reset() => heldBodyDirection = null;
}
