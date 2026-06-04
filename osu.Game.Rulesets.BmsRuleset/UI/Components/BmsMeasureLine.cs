using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsMeasureLine : CompositeDrawable
{
    private readonly BmsPlayfield playfield;
    private readonly BmsStage stage;
    private readonly Box line;
    private readonly double scrollAtTick;
    private readonly double timeAtTick;

    public BmsMeasureLine(long tick, BmsTimingMap timingMap, BmsPlayfield playfield, BmsStage stage)
    {
        this.playfield = playfield;
        this.stage = stage;
        scrollAtTick = timingMap.GetScrollPositionAtTick(tick);
        timeAtTick = timingMap.ProjectTickToTime(tick);

        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;

        InternalChild = line = new Box
        {
            RelativeSizeAxes = Axes.Both,
        };
    }

    protected override void Update()
    {
        base.Update();

        if (Parent == null || !stage.IsLoaded || stage.DrawHeight <= 0)
        {
            Alpha = 0;
            return;
        }

        var scrollRange = Math.Max(1, playfield.ScrollRange);
        var travelDistance = Math.Max(1, stage.DrawHeight - stage.HitTargetPosition);
        var progress = playfield.ConstantScrollActive
            ? timeAtTick - playfield.Time.Current
            : scrollAtTick - playfield.CurrentScrollPosition;
        var y = stage.DrawHeight - stage.HitTargetPosition - (float)(progress * playfield.ScrollSpeedMultiplier / scrollRange) * travelDistance;

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
