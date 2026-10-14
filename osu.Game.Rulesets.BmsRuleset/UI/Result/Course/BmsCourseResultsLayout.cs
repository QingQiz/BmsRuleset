using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseResultsLayout : CompositeDrawable
{
    internal BmsCourseAggregateStatistics AggregateStatistics { get; }

    private readonly ScoreInfo aggregate;
    private readonly BmsResultOverview overview;
    private ScoreInfo? displayedScore;
    private IReadOnlyList<HitEvent> aggregateHitEvents;
    private IReadOnlyList<HitEvent>? displayedHitEvents;

    internal BmsCourseResultsLayout(BmsCourseSession session, ScoreInfo aggregate, Bindable<int?> selectedStage)
    {
        this.aggregate = aggregate;
        aggregateHitEvents = aggregate.HitEvents;
        RelativeSizeAxes = Axes.Both;
        InternalChildren =
        [
            new BmsResultColumns(overview = new BmsResultOverview(aggregate, new BmsCourseOverviewHeader(session, aggregate, selectedStage))),
            new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = AggregateStatistics = new BmsCourseAggregateStatistics(session, aggregate)
                {
                    RelativeSizeAxes = Axes.Both,
                },
            },
        ];
        AggregateStatistics.StatisticsLoaded = hitEvents =>
        {
            aggregateHitEvents = hitEvents;
            overview.SetScore(displayedScore ?? aggregate, displayedScore != null ? displayedHitEvents : aggregateHitEvents);
        };
    }

    internal void ShowScore(ScoreInfo? score, IReadOnlyList<HitEvent>? hitEvents = null)
    {
        displayedScore = score;
        displayedHitEvents = hitEvents;
        overview.SetScore(score ?? aggregate, score != null ? hitEvents : aggregateHitEvents);
    }
}
