using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsLocalLeaderboardScoreSelector
{
    public static ScoreInfo[] SelectScores(IEnumerable<ScoreInfo> scores, string beatmapHash, string rulesetShortName, Mod[]? exactMods, LeaderboardSortMode sorting, BeatmapInfo? fallbackBeatmap = null)
    {
        var newScores = scores.Where(s => s.BeatmapHash == beatmapHash
                                          && s.Ruleset.ShortName == rulesetShortName
                                          && !s.DeletePending);

        if (exactMods != null)
        {
            var filterableExactMods = exactMods.Where(isFilterableMod).ToArray();

            if (filterableExactMods.Length == 0)
            {
                newScores = newScores.Where(s => !s.Mods.Any(isFilterableMod));
            }
            else
            {
                var selectedMods = filterableExactMods.Select(m => m.Acronym).ToHashSet();
                newScores = newScores.Where(s => selectedMods.SetEquals(s.Mods.Where(isFilterableMod).Select(m => m.Acronym)));
            }
        }

        var selectedScores = newScores.Detach().OrderByCriteria(sorting).ToArray();

        if (fallbackBeatmap != null)
        {
            foreach (var score in selectedScores.Where(s => s.BeatmapInfo == null))
                score.BeatmapInfo = fallbackBeatmap;
        }

        return selectedScores;
    }

    private static bool isFilterableMod(Mod mod) => mod.Type != ModType.System;
}
