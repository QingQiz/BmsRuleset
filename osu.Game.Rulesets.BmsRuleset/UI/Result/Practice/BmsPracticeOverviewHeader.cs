using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal partial class BmsPracticeOverviewHeader : CompositeDrawable
{
    internal BmsPracticeOverviewHeader(BmsPracticeSession session, ScoreInfo aggregate, Bindable<BmsPracticeAttemptResult?> selectedAttempt)
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            RowDimensions = [new Dimension(GridSizeMode.Absolute, 72), new Dimension(), new Dimension(GridSizeMode.Absolute, 24)],
            Content = new Drawable[][]
            {
                [new OsuClickableContainer
                {
                    Name = "Practice aggregate selector",
                    RelativeSizeAxes = Axes.Both,
                    Action = () => selectedAttempt.Value = null,
                    Child = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        RowDimensions = [new Dimension(), new Dimension(), new Dimension()],
                        Content = new Drawable[][]
                        {
                            [new BmsResultFittedText(BmsStrings.PracticeOverallResults, 22)],
                            [new BmsResultFittedText(BmsStrings.PracticeChartTitle(session.Source.Metadata.Title, session.Source.BeatmapInfo.DifficultyName), 14)],
                            [new BmsResultFittedText(BmsStrings.PracticeCompletedAttempts(session.AttemptResults.Count), 13)],
                        },
                    },
                }],
                new Drawable[] { new BmsPracticeAttemptCardList(session, selectedAttempt) },
                new Drawable[] { new BmsResultModDisplay(aggregate, showStarRating: false) },
            },
        };
    }
}
