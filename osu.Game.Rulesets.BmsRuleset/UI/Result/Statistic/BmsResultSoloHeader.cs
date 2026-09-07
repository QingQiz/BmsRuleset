using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultSoloHeader : GridContainer
{
    internal BmsResultSoloHeader(ScoreInfo score)
    {
        RelativeSizeAxes = Axes.Both;
        RowDimensions = [new Dimension(GridSizeMode.Absolute, 102), new Dimension()];
        Content = new[]
        {
            new Drawable[]
            {
                new Container
                {
                    Name = "Result beatmap card",
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(4),
                    Child = new BmsResultBeatmapBanner(score.BeatmapInfo!),
                },
            },
            new Drawable[]
            {
                new BmsResultSection(new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 24)],
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new DrawSizePreservingFillContainer
                                {
                                    // Include the outer badges without reserving extra square margins.
                                    TargetDrawSize = new Vector2(300, 260),
                                    Child = new BmsAccuracyCircle(score)
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Size = new Vector2(230),
                                    },
                                },
                            },
                        },
                        new Drawable[] { new BmsResultModDisplay(score) },
                    },
                }),
            },
        };
    }
}
