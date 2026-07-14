using System;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static class BmsTimelineLeadIn
{
    private const double minimum_first_hit_object_time = 2000;

    public static BmsParseResult Apply(BmsParseResult parseResult)
    {
        var firstHitObjectTime = parseResult.HitObjects.Count == 0
            ? null
            : (double?)parseResult.HitObjects[0].StartTime;

        // BmsPreviewTrack cannot seek below zero, so the preparation window must live in positive chart time.
        return parseResult.ShiftedBy(calculateOffset(firstHitObjectTime));
    }

    private static double calculateOffset(double? firstHitObjectTime) =>
        firstHitObjectTime is { } time
            ? Math.Max(0, minimum_first_hit_object_time - time)
            : 0;
}
