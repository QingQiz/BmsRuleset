using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsMeasureLine : CompositeDrawable
{

    private readonly Box line;
    private BmsGameplayScrollController? scrollController;
    private BmsStage? stage;

    // Two coordinate representations of the same measure line:
    //   scrollAtTick — tick-based scroll coordinate (used in normal BPM-aware mode)
    //   timeAtTick   — projected real time          (used in constant-scroll mode)
    // Progress = chosenValue - CurrentScrollPosition/Time.Current.
    private double scrollAtTick;
    private double timeAtTick;

    public BmsMeasureLine()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        InternalChild = line = new Box
        {
            RelativeSizeAxes = Axes.Both,
        };
    }

    internal void Apply(BmsMeasureLineContainer.MeasureLineInfo info, BmsGameplayScrollController scrollController, BmsStage stage)
    {
        this.scrollController = scrollController;
        this.stage = stage;
        scrollAtTick = info.ScrollPosition;
        timeAtTick = info.Time;
    }

    protected override void Update()
    {
        base.Update();

        if (Parent == null || scrollController == null || stage == null || !stage.IsLoaded || stage.DrawHeight <= 0)
        {
            Alpha = 0;
            return;
        }

        var progress = scrollController.ConstantScrollActive
            ? timeAtTick - scrollController.CurrentScrollPosition
            : scrollAtTick - scrollController.CurrentScrollPosition;

        // Once the measure start has passed the judgement line (progress < 0),
        // the measure line should no longer be displayed.
        if (progress < 0)
        {
            Alpha = 0;
            return;
        }

        var y = scrollController.YForScrollProgress(progress, stage.DrawHeight, stage.HitTargetPosition);

        if (!float.IsFinite(y))
        {
            Alpha = 0;
            return;
        }

        X = 0;
        Y = y - stage.BarLineHeight / 2;
        Width = Math.Max(1, stage.DrawWidth);
        Height = Math.Max(0, stage.BarLineHeight);
        Alpha = Height > 0 ? 1 : 0;
        line.Colour = stage.BarLineColour;
    }
}
