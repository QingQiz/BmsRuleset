using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect;

internal static class BmsLocalLeaderboardService
{
    internal static ScoreInfo[] SelectScores(
        IEnumerable<ScoreInfo> scores,
        string beatmapHash,
        string rulesetShortName,
        Mod[]? exactMods,
        LeaderboardSortMode sorting,
        BeatmapInfo? fallbackBeatmap = null)
    {
        var filteredScores = scores.Where(score => score.BeatmapHash == beatmapHash
                                                   && score.Ruleset.ShortName == rulesetShortName
                                                   && !score.DeletePending);

        if (exactMods != null)
        {
            var filterableExactMods = exactMods.Where(isFilterableMod).ToArray();
            filteredScores = filteredScores.Where(score => BmsLampScoreSelector.MatchesExactMods(score.Mods, filterableExactMods, isFilterableMod));
        }

        var selectedScores = filteredScores.Detach().OrderByCriteria(sorting).ToArray();

        if (fallbackBeatmap != null)
        {
            foreach (var score in selectedScores.Where(score => score.BeatmapInfo == null))
                score.BeatmapInfo = fallbackBeatmap;
        }

        return selectedScores;
    }

    internal static bool IsApplicable(LeaderboardCriteria? criteria) =>
        criteria?.Scope == BeatmapLeaderboardScope.Local
        && criteria.Beatmap != null
        && criteria.Ruleset?.ShortName == Constant.SHORT_NAME;

    internal static LeaderboardScores CreateScores(IEnumerable<ScoreInfo> scores, LeaderboardCriteria criteria)
    {
        var beatmap = criteria.Beatmap!;
        var ruleset = criteria.Ruleset!;
        var selectedScores = SelectScores(
            scores,
            beatmap.Hash,
            ruleset.ShortName,
            criteria.ExactMods,
            criteria.Sorting,
            beatmap);

        return LeaderboardScores.Success(selectedScores, selectedScores.Length, selectedScores.Length, null);
    }

    private static bool isFilterableMod(Mod mod) => mod.Type != ModType.System;
}
