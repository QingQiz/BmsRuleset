using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal partial class BmsPracticeStatisticsPanel(Func<BmsResultStatisticsData?> statistics, Func<ScoreInfo, Drawable>? header = null) : BmsStatisticsPanel
{
    // Keep the attempt list and its scroll position when switching between completed rounds.
    protected override bool RecreateOverviewOnScoreChange => header == null;

    protected override BmsResultOverview CreateOverview(ScoreInfo score) => new(score, header?.Invoke(score), showComparison: false);

    protected override Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(statistics());
    }
}
