using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public sealed partial class DefaultBmsNotePiece : CompositeDrawable
{
    public const float NOTE_HEIGHT = 14;

    private readonly Box accent;

    public DefaultBmsNotePiece()
    {
        RelativeSizeAxes = Axes.X;
        Height = NOTE_HEIGHT;
        CornerRadius = 4;
        Masking = true;

        InternalChildren =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White,
            },
            accent = new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = NOTE_HEIGHT / 2,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Alpha = 0.16f,
            },
        ];

        SetAccentColour(Color4.White);
    }

    public void SetAccentColour(Color4 colour)
    {
        accent.Colour = colour.Lighten(0.8f);
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Glow,
            Colour = colour.Lighten(0.8f).Opacity(0.16f),
            Radius = 8,
        };
    }
}
