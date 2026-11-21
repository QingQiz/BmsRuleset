using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Randomisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

internal static class BmsLocalScores
{
    internal static IEnumerable<ScoreInfo> ForRuleset(this IEnumerable<ScoreInfo> scores, string rulesetShortName, IBeatmapInfo? beatmap)
    {
        var includeMania = rulesetShortName == Constant.SHORT_NAME && beatmap != null && Mania7KBeatmapConverter.CanConvert(beatmap);
        foreach (var score in scores)
        {
            if (score.Ruleset.ShortName == rulesetShortName)
                yield return score;
            else if (includeMania && score.Ruleset.OnlineID == 3)
            {
                // Keep the original identity for file actions. Playback gets its own BMS copy on request.
                var display = score.IsManaged ? score.Detach() : score.DeepClone();
                var nativeMods = score.Mods;
                var difficulty = nativeMods.OfType<ModDifficultyAdjust>().FirstOrDefault();
                display.Mods =
                [
                    .. nativeMods.Select(toDisplayMod),
                    new BmsModHideScratch(),
                    new BmsModManiaProfile
                    {
                        OverallDifficulty = { Value = difficulty?.OverallDifficulty.Value },
                        DrainRate = { Value = difficulty?.DrainRate.Value },
                    },
                ];
                yield return display;
            }
        }
    }

    internal static ScoreInfo ForPlayback(ScoreInfo displayed, RulesetInfo ruleset)
    {
        if (displayed.Ruleset.OnlineID != 3 || ruleset.ShortName != Constant.SHORT_NAME)
            return displayed;

        var displayMods = displayed.Mods;
        if (!displayMods.Any(mod => mod is BmsModManiaProfile))
            return displayed;

        // Foreign mod instances may cast their beatmap or playfield to mania types. Only BMS mods can be activated.
        var playbackMods = PlayableMods(displayMods).Select(mod => mod.DeepClone()).ToArray();
        // Apply CL only to the playback copy so the leaderboard's Use Mods action keeps the default windows.
        playbackMods.OfType<BmsModManiaProfile>().Single().ClassicHitWindows.Value = displayMods.Any(mod => mod is ModClassic);

        var result = displayed.DeepClone();
        result.Ruleset = ruleset;
        result.Mods = playbackMods;
        return result;
    }

    internal static IEnumerable<Mod> PlayableMods(IEnumerable<Mod> mods)
    {
        var available = new BmsRuleset().AllMods.Select(mod => mod.GetType()).ToHashSet();
        return mods.Where(mod => available.Contains(mod.GetType()));
    }

    // Only translate shared meanings. For example, mania NR (No Release) is unrelated to BMS NR (Note Random).
    private static Mod toDisplayMod(Mod mod) => mod.Acronym switch
    {
        "DT" or "HT" or "MR" or "IN" or "AT" => new APIMod(mod).ToMod(new BmsRuleset()),
        "NC" when mod is ModNightcore nc => new BmsModDoubleTime { SpeedChange = { Value = nc.SpeedChange.Value }, AdjustPitch = { Value = true } },
        "DC" when mod is ModDaycore dc => new BmsModHalfTime { SpeedChange = { Value = dc.SpeedChange.Value }, AdjustPitch = { Value = true } },
        "CS" => new BmsModConstant(),
        "RD" when mod is ModRandom { Seed.Value: int seed } => maniaLaneOrder(seed),
        _ => mod,
    };

    private static BmsModLaneRandom maniaLaneOrder(int seed)
    {
        // mania sorts lanes by seeded random keys; BMS uses a different shuffle for the same seed.
        var random = new System.Random(seed);
        return new BmsModLaneRandom { LaneOrder = { Value = string.Join(',', Enumerable.Range(1, 7).OrderBy(_ => random.Next()).Prepend(0)) } };
    }
}
