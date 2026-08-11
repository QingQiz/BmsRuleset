using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Additive legacy hit-light animation for a single column.
/// </summary>
/// <remarks>
/// BMS uses normal hit lights for notes and LN hit lights for held notes. Frame length is derived
/// from the animation frame count so short animations still finish near the expected stable timing.
/// </remarks>
internal sealed partial class LegacyBmsHitExplosion : CompositeDrawable
{
    public float ResolvedScale { get; }

    private readonly string imageName;
    private readonly Color4 colour;
    private readonly float hitPosition;

    private Drawable? hitExplosion;

    public LegacyBmsHitExplosion(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        imageName = transformer.GetHitExplosionImageName(lookup);
        var scale = transformer
                        .GetManiaConfig<float>(
                            lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup)
                        ?.Value
                    ?? 1;
        colour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        hitPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? BmsStage.HIT_TARGET_POSITION;
        ResolvedScale = scale;

        setAnimation(frameLength => transformer.GetAnimation(imageName, true, false, frameLength: frameLength));
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        if (hitExplosion == null)
        {
            setAnimation(frameLength => skin.GetAnimation(imageName, true, false, frameLength: frameLength));
            if (hitExplosion == null)
                InternalChild = Empty();
        }
    }

    private void setAnimation(Func<double, Drawable?> getAnimation)
    {
        var tmp = getAnimation(0);
        double frameLength = 0;

        if (tmp is IFramedAnimation tmpAnimation && tmpAnimation.FrameCount > 0)
            frameLength = Math.Max(1000 / 60.0, 170.0 / tmpAnimation.FrameCount);

        hitExplosion = getAnimation(frameLength)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.Centre;
            d.Y = -hitPosition;
            d.Blending = BlendingParameters.Additive;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour);
            d.Scale = new Vector2(ResolvedScale);
        });

        if (hitExplosion != null)
            InternalChild = hitExplosion;
    }
}
