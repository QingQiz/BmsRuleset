using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsHitExplosion : CompositeDrawable
{
    public const double DURATION = 200;

    private readonly SkinnableDrawable skinnableExplosion;

    public BmsHitExplosion(BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChild = skinnableExplosion = new SkinnableDrawable(lookup, _ => new DefaultBmsHitExplosion())
        {
            RelativeSizeAxes = Axes.Both,
            CentreComponent = false,
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        skinnableExplosion.ResetAnimation();
        this.FadeInFromZero(80).Then().FadeOut(120).Expire();
    }

    /// <summary>
    ///     Code-drawn default hit explosion: an additive white flash that fills the column.
    /// </summary>
    private sealed partial class DefaultBmsHitExplosion : CompositeDrawable
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            RelativeSizeAxes = Axes.Both;
            Blending = BlendingParameters.Additive;

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White.Opacity(0.55f),
            };
        }
    }
}
