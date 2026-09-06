using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultSection : Container
{
    internal BmsResultSection(Drawable content, float verticalPadding = 4)
    {
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding(4);
        Children =
        [
            CreateBackground(),
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 8, Vertical = verticalPadding },
                Child = content,
            },
        ];
    }

    internal static Container CreateBackground() => new()
    {
        RelativeSizeAxes = Axes.Both,
        Masking = true,
        CornerRadius = 8,
        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.2f), Alpha = 0.6f },
    };
}
