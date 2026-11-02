using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Course;

internal static class BmsCourseScoreAggregation
{
    internal static ScoreInfo CreateScore(BmsCourseSession session)
    {
        var played = session.Stages.Where(stage => stage.Score != null).Select(stage => stage.Score!).ToArray();
        var passed = session.Status == BmsCourseStatus.Passed;
        var aggregate = BmsScoreAggregation.CreateScore(played, session.Stages[0].Stage.Beatmap, session.Mods, passed);
        if (!passed)
            aggregate.Rank = ScoreRank.F;
        return aggregate;
    }
}
