using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
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

    protected override async Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        var stages = session.Stages
            .Select(stage => (stage.Score, WorkingBeatmap: Beatmaps.GetWorkingBeatmap(stage.Stage.Beatmap, true)))
            .ToArray();

        var data = new (IBeatmap Beatmap, BmsResultStatisticsData Data)[stages.Length];
        for (var i = 0; i < stages.Length; i++)
        {
            var stage = stages[i];
            var replay = stage.Score != null ? await LoadReplayAsync(stage.Score, cancellationToken).ConfigureAwait(false) : null;
            var stageScore = replay?.ScoreInfo ?? score;
            data[i] = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mods = stageScore.Mods;
                using var windows = BmsJudgementProfileProvider.UseWindowMods(mods.OfType<IApplicableToJudgementWindow>().ToArray());
                var beatmap = stage.WorkingBeatmap.GetPlayableBeatmap(stageScore.Ruleset, mods, cancellationToken);
                var statistics = replay != null ? BmsResultStatisticsData.Load(stageScore, beatmap, replay.Replay, cancellationToken) : null;
                return (beatmap, statistics ?? BmsResultStatisticsData.Create(new ScoreInfo(), beatmap));
            }, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(() => BmsResultStatisticsData.CreateCourse(data), cancellationToken).ConfigureAwait(false);
    }
}
