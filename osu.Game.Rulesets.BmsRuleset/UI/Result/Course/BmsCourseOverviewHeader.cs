using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseOverviewHeader : CompositeDrawable
{
    internal BmsCourseOverviewHeader(BmsCourseSession session, ScoreInfo aggregate, Bindable<int?> selectedStage)
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = [new Dimension(GridSizeMode.Absolute, 54), new Dimension(), new Dimension(GridSizeMode.Absolute, 24)],
            Content = new[]
            {
                new Drawable[]
                {
                    new GridContainer
                    {
                        Name = "Course heading",
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = [new Dimension(GridSizeMode.Relative, 0.6f), new Dimension()],
                        Padding = new MarginPadding { Horizontal = 4, Vertical = 2 },
                        Content = new[]
                        {
                            new Drawable[] { new BmsResultFittedText(session.Course.Name, 28, Anchor.Centre) },
                            new Drawable[] { new BmsResultFittedText(session.Course.TableName, 18, Anchor.Centre) },
                        },
                    },
                },
                new Drawable[] { new BmsCourseStageCardList(session, selectedStage) },
                new Drawable[] { new BmsResultModDisplay(aggregate, showStarRating: false) { Name = "Course ruleset and mods" } },
            },
        };
    }

}
