using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

/// <summary>
/// Owns the BMS-specific score and lamp selection used by song-select rank displays.
/// The Harmony adapter only supplies osu!'s private display state and lifecycle.
/// </summary>
internal static class BmsSongSelectLampService
{
    internal static ScoreInfo[] GetLocalScores(
        RealmAccess realm,
        BeatmapInfo beatmap,
        IBindable<APIUser> localUser,
        IBindable<RulesetInfo> ruleset)
    {
        var beatmapHash = beatmap.Hash;

        return realm.Run(r => r.All<ScoreInfo>()
            .Where(s => s.BeatmapHash == beatmapHash && !s.DeletePending)
            .ToArray()
            .Where(s => s.UserID == localUser.Value.Id || s.UserID <= 1)
            .Where(s => ruleset.Value.Equals(s.Ruleset))
            .Select(s => s.DeepClone())
            .ToArray());
    }

    internal static (ScoreInfo? Score, ScoreRank? Rank) Select(
        IEnumerable<ScoreInfo> scores,
        IReadOnlyList<Mod> selectedMods)
    {
        var scoreList = scores as ScoreInfo[] ?? scores.ToArray();
        var score = BmsScoreSelector.SelectBest(scoreList, selectedMods);
        var rank = scoreList
            .Where(s => BmsScoreSelector.MatchesSelectedMods(s, selectedMods))
            .Select(s => (ScoreRank?)s.Rank)
            .DefaultIfEmpty()
            .Max();

        return (score, rank);
    }
}
