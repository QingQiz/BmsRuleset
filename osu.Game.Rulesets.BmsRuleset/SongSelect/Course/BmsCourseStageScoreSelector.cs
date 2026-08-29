using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal static class BmsCourseStageScoreSelector
{
    internal static IReadOnlyList<ScoreInfo> Select(
        IReadOnlyList<BmsCourseResult> courseResults,
        int stageIndex,
        string beatmapHash,
        IEnumerable<ScoreInfo> scores)
    {
        var scoreIds = courseResults
            .Select(result => result.Attempt)
            .Where(attempt => attempt != null && stageIndex < attempt.Stages.Length)
            .Select(attempt => attempt!.Stages[stageIndex])
            .Where(stage => stage.Status != BmsCourseStageStatus.NotPlayed
                            && stage.BeatmapHash == beatmapHash
                            && stage.ScoreId is { } scoreId
                            && scoreId != Guid.Empty)
            .Select(stage => stage.ScoreId!.Value)
            .ToHashSet();

        return scores.Where(score => scoreIds.Contains(score.ID)).ToArray();
    }
}
