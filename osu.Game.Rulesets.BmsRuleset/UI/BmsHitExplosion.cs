using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public sealed partial class BmsHitExplosion : CompositeDrawable
{
    public const double DURATION = 200;

    private readonly SkinnableDrawable skinnableExplosion;

    public BmsHitExplosion(BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChild = skinnableExplosion = new SkinnableDrawable(lookup, _ => Empty())
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
}
