using System;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

internal sealed class BmsLongNoteVisualState
{
    private float? fixedHeadY;
    private int? heldBodyDirection;

    public void PinHead(float y)
    {
        fixedHeadY = y;
        heldBodyDirection = null;
    }

    public float ResolveHeldHeadY(float realHeadY, float realTailY, Func<float, float, int> directionResolver)
    {
        if (fixedHeadY == null)
            return realHeadY;

        heldBodyDirection ??= directionResolver(realHeadY, realTailY);
        return fixedHeadY.Value;
    }

    public float VisibleBodyTailOffset(float headOffset, float tailOffset)
        => BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset, tailOffset, heldBodyDirection);

    public void Reset()
    {
        fixedHeadY = null;
        heldBodyDirection = null;
    }
}
