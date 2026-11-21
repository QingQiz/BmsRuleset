using System;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal readonly record struct BmsManiaReplayCadence(double StartTime)
{
    private const double step = 16;

    internal double NextIdleTimeAfter(double time)
    {
        if (time < StartTime)
            return StartTime;

        var index = Math.Floor((time - StartTime) / step) + 1;
        var next = StartTime + index * step;
        return next > time ? next : StartTime + (index + 1) * step;
    }
}
