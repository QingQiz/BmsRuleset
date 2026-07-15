using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public sealed partial class BmsRulesetIcon : CompositeDrawable
{
    private const float design_size = 40;

    private readonly Container iconContent;

    public BmsRulesetIcon()
    {
        Size = new Vector2(design_size);

        InternalChild = iconContent = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(design_size),
            Children =
            [
                new CircularContainer
                {
                    Name = "Outer ring",
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(42),
                    Masking = true,
                    BorderThickness = 4.2f,
                    BorderColour = Colour4.White,
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                },
                createKey(new Vector2(11.8f, 7f)),
                createKey(new Vector2(20.2f, 7f)),
                createKey(new Vector2(7.6f, 19.75f)),
                createKey(new Vector2(16, 19.75f)),
                createKey(new Vector2(24.4f, 19.75f)),
            ],
        };
    }

    protected override void Update()
    {
        base.Update();

        var fitScale = Math.Min(DrawWidth / design_size, DrawHeight / design_size);
        iconContent.Scale = new Vector2(fitScale);
    }

    private static Drawable createKey(Vector2 position) => new Container
    {
        Name = "Key",
        Position = position,
        Size = new Vector2(8, 12),
        Masking = true,
        CornerRadius = 1.5f,
        Child = new Box { RelativeSizeAxes = Axes.Both },
    };
}
