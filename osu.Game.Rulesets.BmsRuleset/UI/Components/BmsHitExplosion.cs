using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsHitExplosion : CompositeDrawable
{
    private readonly BmsCachedSkinnableDrawable skinnableExplosion;

    public BmsHitExplosion(BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        InternalChild = skinnableExplosion = new BmsCachedSkinnableDrawable(lookup)
        {
            RelativeSizeAxes = Axes.Both,
            ComponentAnchor = null,
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        skinnableExplosion.ResetAnimation();
        this.FadeInFromZero(80).Then().FadeOut(120).Expire();
    }
}
