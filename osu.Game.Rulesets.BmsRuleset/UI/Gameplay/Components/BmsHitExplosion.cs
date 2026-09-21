using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

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
        AlwaysPresent = true;
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
        LifetimeEnd = Time.Current + 200;
    }

    protected override void Update()
    {
        base.Update();
        // Keep each overlapping pulse's original envelope without allocating fade transforms.
        var elapsed = Time.Current - LifetimeStart;
        Alpha = (float)Math.Clamp(elapsed < 80 ? elapsed / 80 : (200 - elapsed) / 120, 0, 1);
    }

    protected override void FreeAfterUse()
    {
        ClearTransforms();
        base.FreeAfterUse();
    }
}
