using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsScoreGraphScoreSelector
{
    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods, int maximumExScore) =>
        scores.Where(score => BmsLampScoreSelector.MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (BmsExScore.Calculate(score, maximumExScore), -score.Date.UtcDateTime.Ticks));
}
