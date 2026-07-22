using System;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal static class BmsBeatmapStatistics
{
    public static void WriteScratchObjectCount(IBeatmap beatmap, int scratchObjectCount)
    {
        var encoded = encodeScratchObjectCount(scratchObjectCount);
        beatmap.Difficulty.SliderTickRate = encoded;
        beatmap.BeatmapInfo.Difficulty.SliderTickRate = encoded;
    }

    public static void WriteScratchObjectCount(BeatmapInfo beatmapInfo, int scratchObjectCount) =>
        beatmapInfo.Difficulty.SliderTickRate = encodeScratchObjectCount(scratchObjectCount);

    public static bool TryGetScratchObjectCount(IBeatmapDifficultyInfo difficulty, out int scratchObjectCount)
    {
        // BMS has no slider ticks, so a negative value provides persistent ruleset-owned storage
        // while allowing legacy imports with the normal positive default to remain distinguishable.
        if (difficulty.SliderTickRate >= 0)
        {
            scratchObjectCount = 0;
            return false;
        }

        var decoded = -difficulty.SliderTickRate - 1;
        if (decoded > int.MaxValue || decoded != Math.Truncate(decoded))
        {
            scratchObjectCount = 0;
            return false;
        }

        scratchObjectCount = (int)decoded;
        return true;
    }

    private static double encodeScratchObjectCount(int scratchObjectCount) => -Math.Max(0, scratchObjectCount) - 1;
}
