using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsHitExplosion : PoolableDrawable
{
    private readonly BmsCachedSkinnableDrawable skinnableExplosion;

    public BmsHitExplosion()
        : this(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion))
    {
    }

    public BmsHitExplosion(BmsSkinComponentLookup lookup, float positionOffset = 0)
    {
        RelativeSizeAxes = Axes.Both;
        ApplyPositionOffset(positionOffset);

        InternalChild = skinnableExplosion = new BmsCachedSkinnableDrawable(lookup)
        {
            RelativeSizeAxes = Axes.Both,
            ComponentAnchor = null,
        };
    }

    public void ApplyPositionOffset(float positionOffset) => Y = -positionOffset;

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        ClearTransforms();
        skinnableExplosion.ResetAnimation();
        LifetimeStart = Time.Current;
        this.FadeInFromZero(80).Then().FadeOut(120).Expire();
    }

    protected override void FreeAfterUse()
    {
        ClearTransforms();
        base.FreeAfterUse();
    }
}
