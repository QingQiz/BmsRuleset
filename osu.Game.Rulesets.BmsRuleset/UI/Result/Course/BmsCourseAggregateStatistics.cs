using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseAggregateStatistics : BmsStatisticsPanel
{
    protected override bool StartHidden => false;

    private readonly BmsCourseSession session;

    internal BmsCourseAggregateStatistics(BmsCourseSession session, ScoreInfo aggregate)
        : base(false)
    {
        this.session = session;
        Score.Value = aggregate;
    }

    // Course replays are restored per stage; the aggregate has no replay archive of its own.
    protected override Task RestoreReplayDataAsync(ScoreInfo score, CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        var stages = session.Stages
            .Select(stage => (stage.Score, WorkingBeatmap: Beatmaps.GetWorkingBeatmap(stage.Stage.Beatmap, true)))
            .ToArray();

        // Load the shared course data once so all charts use the same stages and cancellation lifetime.
        var data = await Task.Run(() => stages.Select(stage =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stageScore = stage.Score ?? score;
            return (stage.Score, Beatmap: stage.WorkingBeatmap.GetPlayableBeatmap(stageScore.Ruleset, stageScore.Mods));
        }).ToArray(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var played = data.Where(stage => stage.Score != null)
            .Select(stage => (stage.Beatmap, (IReadOnlyList<HitEvent>)stage.Score!.HitEvents)).ToArray();

        return await Task.Run(() => new BmsResultStatisticsData(
            BmsGaugeHistoryGraph.CreateCourseSeries(data),
            BmsGaugeHistoryGraph.CreateCourseStageBoundaries(data),
            BmsTimelineStatistic.CreateCourseData(data),
            BmsHitScatterStatistic.CreateCourseStatistics(played),
            BmsHitOffsetStatistic.CreateCourseStatistics(played)), cancellationToken).ConfigureAwait(false);
    }
}
