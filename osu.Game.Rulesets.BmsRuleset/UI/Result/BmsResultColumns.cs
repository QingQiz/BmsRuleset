using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

internal partial class BmsResultColumns : GridContainer
{
    internal BmsResultColumns(Drawable? overview = null, Drawable? charts = null)
    {
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Horizontal = 8, Vertical = 6 };
        ColumnDimensions = [new Dimension(GridSizeMode.Relative, 0.21f), new Dimension()];
        Content = new[]
        {
            new[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Right = 4 },
                    Child = overview ?? new Container(),
                },
                charts ?? new Container(),
            },
        };
    }
}
