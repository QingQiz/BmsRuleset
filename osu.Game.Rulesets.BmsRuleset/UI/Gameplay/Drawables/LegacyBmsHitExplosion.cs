using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

/// <summary>
/// Additive legacy hit-light animation for a single column.
/// </summary>
/// <remarks>
/// BMS uses normal hit lights for notes and LN hit lights for held notes. Frame length is derived
/// from the animation frame count so short animations still finish near the expected stable timing.
/// </remarks>
internal sealed partial class LegacyBmsHitExplosion : CompositeDrawable
{
    // Geometry, tint and animation are already inherited by the sprite's own draw node.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    public float ResolvedScale { get; private set; }

    private readonly string imageName;
    private readonly Color4 colour;
    private readonly float hitPosition;
    private readonly BmsSkinComponentLookup lookup;

    private Drawable? hitExplosion;

    internal Sprite? StaticSprite => hitExplosion?.GetType() == typeof(Sprite) ? (Sprite)hitExplosion : null;

    public LegacyBmsHitExplosion(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup, Texture[] textures)
    {
        RelativeSizeAxes = Axes.Both;
        this.lookup = lookup;

        imageName = transformer.GetHitExplosionImageName(lookup);
        var scale = transformer
                        .GetManiaConfig<float>(
                            lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup)
                        ?.Value
                    ?? 1;
        colour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        hitPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? BmsStage.HIT_TARGET_POSITION;
        ResolvedScale = scale;

        setTextures(textures);
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        var style = (skin as BmsEmbeddedSkinSource)?.StageSkin?.Style;
        var scales = lookup.IsLongNote ? style?.HoldLightScales : style?.HitLightScales;
        if (lookup.ColumnIndex is int column && scales?.TryGetValue(column, out var customScale) == true)
        {
            ResolvedScale = customScale;
            if (hitExplosion != null)
                hitExplosion.Scale = new Vector2(customScale);
        }

        var image = lookup.IsLongNote ? BmsStageImage.HoldLight : BmsStageImage.HitLight;
        if ((skin as BmsEmbeddedSkinSource)?.StageSkin?.GetImage(image, lookup.ColumnIndex) is { Length: > 0 } custom)
        {
            setTextures(skin.GetTextures(custom, default, default, true, "-", null, out _));
            if (hitExplosion != null)
                hitExplosion.Colour = Color4.White;
        }
        else if (hitExplosion == null)
        {
            setTextures(skin.GetTextures(imageName, default, default, true, "-", null, out _));
            if (hitExplosion == null)
                InternalChild = Empty();
        }
    }

    private void setTextures(Texture[] textures)
    {
        if (textures.Length == 0)
            return;

        if (textures.Length == 1)
            hitExplosion = new Sprite { Texture = textures[0] };
        else
        {
            var animation = new LegacySkinExtensions.SkinnableTextureAnimation
            {
                DefaultFrameLength = Math.Max(1000 / 60.0, 170.0 / textures.Length),
                Loop = false,
            };
            foreach (var texture in textures)
                animation.AddFrame(texture);
            hitExplosion = animation;
        }

        hitExplosion.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.Centre;
            d.Y = -hitPosition;
            d.Blending = BlendingParameters.Additive;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour);
            d.Scale = new Vector2(ResolvedScale);
        });

        InternalChild = hitExplosion;
    }
}
