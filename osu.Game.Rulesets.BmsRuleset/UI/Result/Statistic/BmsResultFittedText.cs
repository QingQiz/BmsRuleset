using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultFittedText : BmsResultFittedContainer
{
    internal BmsResultFittedText(LocalisableString value, float size, Anchor anchor = Anchor.CentreLeft, ColourInfo? colour = null,
                                 bool useFullGlyphHeight = true)
        : base(new OsuSpriteText
        {
            Anchor = anchor,
            Origin = anchor,
            Text = value,
            Font = OsuFont.GetFont(size: size, weight: FontWeight.Bold),
            Colour = colour ?? ColourInfo.SingleColour(Color4.White),
            UseFullGlyphHeight = useFullGlyphHeight,
        })
    {
    }
}
