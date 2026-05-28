using System;
using System.Collections.Generic;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public static class BmsEmbeddedSkinDefinition
{
    public static readonly IReadOnlyDictionary<Type, BmsEmbeddedSkinKind> LEGACY_SKINS = new Dictionary<Type, BmsEmbeddedSkinKind>
    {
        [typeof(DefaultLegacySkin)] = BmsEmbeddedSkinKind.Legacy,
        [typeof(RetroSkin)] = BmsEmbeddedSkinKind.Legacy,
    };

    public static readonly IReadOnlyDictionary<Type, BmsEmbeddedSkinKind> MODERN_SKINS = new Dictionary<Type, BmsEmbeddedSkinKind>
    {
        [typeof(ArgonSkin)] = BmsEmbeddedSkinKind.Modern,
        [typeof(ArgonProSkin)] = BmsEmbeddedSkinKind.Modern,
        [typeof(TrianglesSkin)] = BmsEmbeddedSkinKind.Modern,
    };

    public static bool TryGetKind(ISkin skin, out BmsEmbeddedSkinKind kind)
    {
        var type = skin.GetType();
        return MODERN_SKINS.TryGetValue(type, out kind) || LEGACY_SKINS.TryGetValue(type, out kind);
    }
}
