using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultTextGroup : BmsResultFittedContainer
{
    internal BmsResultTextGroup((LocalisableString Text, float Size)[] parts, Anchor anchor = Anchor.BottomRight,
                                bool useFullGlyphHeight = true)
        : base(new FillFlowContainer
        {
            Anchor = anchor,
            Origin = anchor,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(3, 0),
            Children = parts.Select(part => new OsuSpriteText
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Text = part.Text,
                Font = OsuFont.GetFont(size: part.Size, weight: FontWeight.Bold),
                UseFullGlyphHeight = useFullGlyphHeight,
            }).ToArray(),
        })
    {
    }
}
