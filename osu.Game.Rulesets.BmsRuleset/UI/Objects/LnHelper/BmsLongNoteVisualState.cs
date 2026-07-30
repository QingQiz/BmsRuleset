using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

internal sealed class BmsLongNoteVisualState
{
    private float? headYAtStartTime;
    private double? previousHeadTime;
    private float previousHeadY;
    private double previousVisualOffset = double.NaN;
    private int? heldBodyDirection;

    public void PrepareHeadPin() => heldBodyDirection = null;

    public void UpdateHeadYAtStartTime(float realHeadY, double currentTime, double startTime, double visualOffset)
    {
        var visualOffsetChanged = previousVisualOffset != visualOffset;

        // Reuse the observed visual displacement so an offset change does not require a timing-map query.
        if (headYAtStartTime != null && visualOffsetChanged && previousHeadTime != null)
            headYAtStartTime += realHeadY - previousHeadY;

        if (headYAtStartTime == null)
        {
            if (currentTime >= startTime)
            {
                if (!visualOffsetChanged && previousHeadTime is { } previousTime && currentTime > previousTime)
                {
                    var progress = (float)Math.Clamp((startTime - previousTime) / (currentTime - previousTime), 0, 1);
                    headYAtStartTime = previousHeadY + (realHeadY - previousHeadY) * progress;
                }
                else
                {
                    headYAtStartTime = realHeadY;
                }
            }
        }

        previousHeadTime = currentTime;
        previousHeadY = realHeadY;
        previousVisualOffset = visualOffset;
    }

    public float ResolveHeldHeadY(float realHeadY, float realTailY, bool canPin, Func<float, float, int> directionResolver)
    {
        if (!canPin || headYAtStartTime == null)
            return realHeadY;

        heldBodyDirection ??= directionResolver(realHeadY, realTailY);

        return headYAtStartTime.Value;
    }

    public float VisibleBodyTailOffset(float headOffset, float tailOffset)
        => BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset, tailOffset, heldBodyDirection);

    public void Reset()
    {
        headYAtStartTime = null;
        previousHeadTime = null;
        previousHeadY = 0;
        previousVisualOffset = double.NaN;
        heldBodyDirection = null;
    }
}
