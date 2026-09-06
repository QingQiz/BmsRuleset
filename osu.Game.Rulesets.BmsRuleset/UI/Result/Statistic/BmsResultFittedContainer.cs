using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultFittedContainer : CompositeDrawable
{
    private readonly Drawable content;
    private readonly Axes fitAxes;

    protected BmsResultFittedContainer(Drawable content, Axes fitAxes = Axes.Both)
    {
        this.content = content;
        this.fitAxes = fitAxes;
        RelativeSizeAxes = Axes.Both;
        InternalChild = content;
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // Scale the entire group to preserve font hierarchy and icon proportions in compact cells.
        var scale = 1f;
        if ((fitAxes & Axes.X) != 0)
            scale = Math.Min(scale, ChildSize.X / Math.Max(1, content.DrawWidth));
        if ((fitAxes & Axes.Y) != 0)
            scale = Math.Min(scale, ChildSize.Y / Math.Max(1, content.DrawHeight));
        content.Scale = new Vector2(scale);
    }
}
