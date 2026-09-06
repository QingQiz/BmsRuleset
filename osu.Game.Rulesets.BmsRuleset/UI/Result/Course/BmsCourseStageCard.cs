using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseStageCard : OsuClickableContainer
{
    private readonly int stageIndex;
    private readonly Bindable<int?> selectedStage;
    private readonly Box highlight;
    private readonly Container selectionBorder;

    internal bool IsSelected => selectedStage.Value == stageIndex;

    internal BmsCourseStageCard(BmsCourseStageAttempt attempt, int stageIndex, Bindable<int?> selectedStage)
    {
        this.stageIndex = stageIndex;
        this.selectedStage = selectedStage;
        Name = $"Course stage {stageIndex + 1} result";
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 8;
        Action = attempt.Score == null ? null : () => selectedStage.Value = IsSelected ? null : stageIndex;
        Children =
        [
            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.2f), Alpha = 0.6f },
            new BmsResultBeatmapBanner(attempt.Stage.Beatmap, fitText: true),
            highlight = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0 },
            // A separate top layer keeps nested beatmap masks from covering the selection border.
            selectionBorder = new Container
            {
                Name = "Stage selection border",
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 2,
                BorderColour = BmsResultColours.ACCENT,
                Alpha = 0,
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            },
        ];
    }

    [BackgroundDependencyLoader]
    private void load() => selectedStage.BindValueChanged(selectionChanged, true);

    protected override void Dispose(bool isDisposing)
    {
        selectedStage.ValueChanged -= selectionChanged;
        base.Dispose(isDisposing);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateColours();
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        updateColours();
        base.OnHoverLost(e);
    }

    private void selectionChanged(ValueChangedEvent<int?> _) => updateColours();

    private void updateColours()
    {
        selectionBorder.Alpha = IsSelected ? 1 : 0;
        highlight.FadeTo(IsHovered ? 0.12f : 0, 120);
    }
}
