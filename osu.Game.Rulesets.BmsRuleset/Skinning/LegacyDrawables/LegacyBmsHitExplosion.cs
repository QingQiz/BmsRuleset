using System;
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

    public LegacyBmsHitExplosion(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        var tmp = transformer.GetAnimation(transformer.GetHitExplosionImageName(lookup), true, false);
        double frameLength = 0;

        if (tmp is IFramedAnimation tmpAnimation && tmpAnimation.FrameCount > 0)
            frameLength = Math.Max(1000 / 60.0, 170.0 / tmpAnimation.FrameCount);

        var scale = transformer
                        .GetManiaConfig<float>(
                            lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup)
                        ?.Value
                    ?? 1;
        var colour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        var hitPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? BmsStage.HIT_TARGET_POSITION;
        ResolvedScale = scale;

        InternalChild = transformer.GetAnimation(transformer.GetHitExplosionImageName(lookup), true, false, frameLength: frameLength)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.Centre;
            d.Y = -hitPosition;
            d.Blending = BlendingParameters.Additive;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour);
            d.Scale = new Vector2(scale);
        }) ?? Empty();
    }
}
