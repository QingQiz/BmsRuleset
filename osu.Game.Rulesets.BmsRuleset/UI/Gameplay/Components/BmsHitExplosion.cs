using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsHitExplosion : PoolableDrawable
{
    // The skin child owns the drawing; this pooled wrapper only controls its fade and lifetime.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    private readonly BmsCachedSkinnableDrawable skinnableExplosion;
    private double? inheritedStartTime;

    internal void InheritStartTime(double? startTime) => inheritedStartTime = startTime;

    internal static float AlphaAt(double elapsed)
        => (float)Math.Clamp(elapsed < 80 ? elapsed / 80 : (200 - elapsed) / 120, 0, 1);

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
        LifetimeStart = inheritedStartTime ?? Time.Current;
        LifetimeEnd = LifetimeStart + 200;
        inheritedStartTime = null;
    }

    protected override void Update()
    {
        base.Update();
        // Keep each overlapping pulse's original envelope without allocating fade transforms.
        Alpha = AlphaAt(Time.Current - LifetimeStart);
    }

    protected override void FreeAfterUse()
    {
        ClearTransforms();
        inheritedStartTime = null;
        base.FreeAfterUse();
    }
}
