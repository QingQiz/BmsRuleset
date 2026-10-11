using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

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

    private float lastY = float.NaN;
    private float lastWidth = float.NaN;
    private float lastHeight = float.NaN;
    private float lastAlpha = float.NaN;
    private Color4 lastColour;
    private bool hasColour;

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
            if (lastAlpha != 0)
            {
                lastAlpha = 0;
                Alpha = 0;
            }

            return;
        }

        var progress = scrollController.ConstantScrollActive
            ? timeAtTick - scrollController.CurrentScrollPosition
            : scrollAtTick - scrollController.CurrentScrollPosition;

        // Once the measure start has passed the judgement line (progress < 0),
        // the measure line should no longer be displayed.
        if (progress < 0)
        {
            if (lastAlpha != 0)
            {
                lastAlpha = 0;
                Alpha = 0;
            }

            return;
        }

        var y = scrollController.YForScrollProgress(progress, stage.DrawHeight, stage.HitTargetPosition);

        if (!float.IsFinite(y))
        {
            if (lastAlpha != 0)
            {
                lastAlpha = 0;
                Alpha = 0;
            }

            return;
        }

        var barLineHeight = stage.BarLineHeight;
        var lineY = y - barLineHeight / 2;
        if (lineY != lastY)
        {
            lastY = lineY;
            Y = lineY;
        }

        var lineWidth = Math.Max(1, stage.DrawWidth);
        if (lineWidth != lastWidth)
        {
            lastWidth = lineWidth;
            Width = lineWidth;
        }

        var lineHeight = Math.Max(0, barLineHeight);
        if (lineHeight != lastHeight)
        {
            lastHeight = lineHeight;
            Height = lineHeight;
        }

        var lineAlpha = lineHeight > 0 ? 1f : 0f;
        if (lineAlpha != lastAlpha)
        {
            lastAlpha = lineAlpha;
            Alpha = lineAlpha;
        }

        var colour = stage.BarLineColour;
        if (!hasColour || colour != lastColour)
        {
            hasColour = true;
            lastColour = colour;
            line.Colour = colour;
        }
    }
}
