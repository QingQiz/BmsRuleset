using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

internal sealed class BmsLongNoteVisualState
{
    private float? headYAtStartTime;
    private double? previousHeadTime;
    private float previousHeadY;
    private int? heldBodyDirection;

    public void PrepareHeadPin() => heldBodyDirection = null;

    public void CaptureHeadYAtStartTime(float realHeadY, double currentTime, double startTime)
    {
        if (headYAtStartTime != null)
            return;

        if (currentTime < startTime)
        {
            previousHeadTime = currentTime;
            previousHeadY = realHeadY;
            return;
        }

        if (previousHeadTime is { } previousTime && currentTime > previousTime)
        {
            var progress = (float)Math.Clamp((startTime - previousTime) / (currentTime - previousTime), 0, 1);
            headYAtStartTime = previousHeadY + (realHeadY - previousHeadY) * progress;
            return;
        }

        headYAtStartTime = realHeadY;
    }

    public float ResolveHeldHeadY(float realHeadY, float realTailY, bool canPin, Func<float, float, int> directionResolver)
    {
        if (!canPin || headYAtStartTime == null)
            return realHeadY;

        if (heldBodyDirection == null)
        {
            heldBodyDirection = directionResolver(realHeadY, realTailY);
        }

        return headYAtStartTime.Value;
    }

    public float VisibleBodyTailOffset(float headOffset, float tailOffset)
        => BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset, tailOffset, heldBodyDirection);

    public void Reset()
    {
        headYAtStartTime = null;
        previousHeadTime = null;
        previousHeadY = 0;
        heldBodyDirection = null;
    }
}
