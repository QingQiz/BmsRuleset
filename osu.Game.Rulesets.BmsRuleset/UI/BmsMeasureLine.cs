using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public sealed partial class BmsMeasureLine : CompositeDrawable
{
    public long Tick { get; }

    private readonly BmsTimingMap timingMap;
    private readonly BmsPlayfield playfield;
    private readonly BmsStage stage;
    private readonly Box line;
    private readonly double scrollAtTick;

    public BmsMeasureLine(long tick, BmsTimingMap timingMap, BmsPlayfield playfield, BmsStage stage)
    {
        Tick = tick;
        this.timingMap = timingMap;
        this.playfield = playfield;
        this.stage = stage;
        scrollAtTick = timingMap.GetScrollPositionAtTick(tick);

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

        var currentScroll = playfield.CurrentScrollPosition;
        var scrollUntilLine = scrollAtTick - currentScroll;
        var scrollRange = Math.Max(1, playfield.ScrollRange);
        var travelDistance = Math.Max(1, stage.DrawHeight - stage.HitTargetPosition);
        var y = stage.DrawHeight - stage.HitTargetPosition - (float)(scrollUntilLine * playfield.ScrollSpeedMultiplier / scrollRange) * travelDistance;

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
