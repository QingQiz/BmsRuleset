using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseResultsLayout : CompositeDrawable
{
    internal BmsCourseAggregateStatistics AggregateStatistics { get; }

    private readonly ScoreInfo aggregate;
    private readonly BmsResultOverview overview;

    internal BmsCourseResultsLayout(BmsCourseSession session, ScoreInfo aggregate, Bindable<int?> selectedStage)
    {
        this.aggregate = aggregate;
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
    }

    internal void ShowScore(ScoreInfo? score) => overview.SetScore(score ?? aggregate);
}
