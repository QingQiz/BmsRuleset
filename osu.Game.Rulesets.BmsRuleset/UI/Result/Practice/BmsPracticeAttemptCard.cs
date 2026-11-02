using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal partial class BmsPracticeAttemptCard : OsuClickableContainer
{
    private readonly BmsPracticeAttemptResult result;
    private readonly Bindable<BmsPracticeAttemptResult?> selectedAttempt;
    private readonly Box highlight;

    internal bool IsSelected => selectedAttempt.Value == result;

    internal BmsPracticeAttemptCard(BmsPracticeAttemptResult result, Bindable<BmsPracticeAttemptResult?> selectedAttempt)
    {
        this.result = result;
        this.selectedAttempt = selectedAttempt.GetBoundCopy();
        Name = $"Practice attempt {result.Attempt} result";
        RelativeSizeAxes = Axes.X;
        Height = 106;
        Masking = true;
        CornerRadius = 6;
        BorderColour = BmsResultColours.ACCENT;
        Action = () => this.selectedAttempt.Value = IsSelected ? null : result;
        Children =
        [
            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.2f), Alpha = 0.6f },
            highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = BmsResultColours.ACCENT, Alpha = 0 },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 8, Vertical = 6 },
                RowDimensions = [new Dimension(GridSizeMode.Absolute, 22), new Dimension(GridSizeMode.Absolute, 22), new Dimension(), new Dimension(GridSizeMode.Absolute, 18)],
                Content = new Drawable[][]
                {
                    [new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Relative, 0.36f)],
                        Content = new Drawable[][] { [new BmsResultFittedText(BmsStrings.PracticeAttempt(result.Attempt), 16),
                            new BmsResultFittedText(BmsStrings.ResultPercentage(result.Score.Accuracy), 16, Anchor.CentreRight)] },
                    }],
                    [new BmsResultFittedText(BmsStrings.PracticeAttemptMetrics(result.ExScore, result.Score.MaxCombo, result.Health), 12)],
                    [new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Content = new Drawable[][] { new[] { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss }
                            .Select(judgement => (Drawable)new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Content = new Drawable[][]
                                {
                                    [new BmsResultFittedText(BmsStrings.ResultJudgement(judgement), 9, Anchor.Centre, BmsHitResultColours.ForHitResult(judgement))],
                                    [new BmsResultFittedText(BmsStrings.ResultNumber(result.Score.Statistics.GetValueOrDefault(judgement)), 14, Anchor.Centre)],
                                },
                            }).ToArray() },
                    }],
                    [new BmsResultFittedText(BmsStrings.PracticeTiming(result.Fast, result.Slow), 11)],
                },
            },
        ];
    }

    [BackgroundDependencyLoader]
    private void load() => selectedAttempt.BindValueChanged(selectionChanged, true);

    protected override bool OnHover(HoverEvent e)
    {
        highlight.FadeTo(0.12f, 120);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        highlight.FadeOut(120);
        base.OnHoverLost(e);
    }

    private void selectionChanged(ValueChangedEvent<BmsPracticeAttemptResult?> _) => BorderThickness = IsSelected ? 2 : 0;
}
