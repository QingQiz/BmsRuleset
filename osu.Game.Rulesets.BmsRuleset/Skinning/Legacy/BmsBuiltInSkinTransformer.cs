using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

/// <inheritdoc />
/// <summary>
/// Skin transformer applied over osu! built-in skins (Argon, ArgonPro, Triangles,
/// DefaultLegacy, Retro) during BMS gameplay.
/// </summary>
public partial class BmsBuiltInSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    /// <inheritdoc/>
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult>
            ? null
            : BmsDefaultHud.GetDrawableComponent(lookup) ?? base.GetDrawableComponent(lookup);

    /// <inheritdoc/>
    /// return null for every skin lookup, thus fall through to BmsEmbeddedSkinFallbackChain
    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        // Built-in skins have no skin.ini BMS/mania config; short-circuit immediately.
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }
}
