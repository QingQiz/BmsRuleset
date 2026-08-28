using System.Collections.Generic;
using osu.Game.Online.Leaderboards;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal static class BmsLocalLeaderboardService
{
    internal static bool IsApplicable(LeaderboardCriteria? criteria) =>
        criteria?.Scope == BeatmapLeaderboardScope.Local
        && criteria.Beatmap != null
        && criteria.Ruleset?.ShortName == Constant.SHORT_NAME;

    internal static LeaderboardScores CreateScores(IEnumerable<ScoreInfo> scores, LeaderboardCriteria criteria)
    {
        var beatmap = criteria.Beatmap!;
        var ruleset = criteria.Ruleset!;
        var selectedScores = BmsLocalLeaderboardScoreSelector.SelectScores(
            scores,
            beatmap.Hash,
            ruleset.ShortName,
            criteria.ExactMods,
            criteria.Sorting,
            beatmap);

        return LeaderboardScores.Success(selectedScores, selectedScores.Length, selectedScores.Length, null);
    }
}
