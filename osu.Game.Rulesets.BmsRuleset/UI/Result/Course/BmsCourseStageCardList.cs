using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Course;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseStageCardList : CompositeDrawable
{
    internal BmsCourseStageCardList(BmsCourseSession session, Bindable<int?> selectedStage)
    {
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Top = 4 };
        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            Content = session.Stages.Select((stage, index) => new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Vertical = 3 },
                    Child = new BmsCourseStageCard(stage, index, selectedStage),
                },
            }).ToArray(),
        };
    }
}
