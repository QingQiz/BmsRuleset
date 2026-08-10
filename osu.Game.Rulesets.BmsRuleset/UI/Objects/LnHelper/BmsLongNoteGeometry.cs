using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

internal static class BmsLongNoteGeometry
{
    public static float BodyCentreOffset(float noteAnchorOffset, float noteHeight)
        => noteAnchorOffset - noteHeight / 2;

    public static float VisibleBodyTailOffset(float headOffset, float tailOffset, int? heldBodyDirection)
    {
        if (heldBodyDirection is not { } direction || direction == 0)
            return tailOffset;

        var delta = tailOffset - headOffset;

        return Math.Sign(delta) == direction ? tailOffset : headOffset;
    }

    public static int BodyDirectionBeforeTailPasses(double scrollDelta, double duration, double visualDirection, float realHeadY, float realTailY)
    {
        if (scrollDelta == 0 && duration != 0)
            scrollDelta = duration;

        var direction = -Math.Sign(scrollDelta * visualDirection);

        return direction != 0 ? direction : Math.Sign(realTailY - realHeadY);
    }
}
