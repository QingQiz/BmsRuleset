using System;
using System.Collections.Generic;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;

/// <summary>
/// Static registry that maps known osu! built-in skin types to the
/// BmsEmbeddedSkinKind whose asset set best matches their visual style.
/// </summary>
/// <remarks>
/// Used by BmsEmbeddedSkinSource.GetEmbeddedSkinKind to decide which
/// embedded texture pack to activate during gameplay.
/// Lookups are exact-type matches (<c>GetType() == typeof(T)</c>); subclasses are
/// not considered, so an unrecognised user skin falls through to the default.
/// </remarks>
public static class BmsEmbeddedSkinDefinition
{
    /// <summary>
    /// Skins that use the BmsEmbeddedSkinKind.LegacyOld asset set —
    /// classic osu!stable-style visuals.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, BmsEmbeddedSkinKind> LEGACY_SKINS = new Dictionary<Type, BmsEmbeddedSkinKind>
    {
        [typeof(DefaultLegacySkin)] = BmsEmbeddedSkinKind.LegacyOld,
        [typeof(RetroSkin)] = BmsEmbeddedSkinKind.LegacyOld,
    };

    /// <summary>
    /// Skins that use the BmsEmbeddedSkinKind.LegacyModern asset set —
    /// modern Argon-compatible visuals.
    /// </summary>
    public static readonly IReadOnlyDictionary<Type, BmsEmbeddedSkinKind> MODERN_SKINS = new Dictionary<Type, BmsEmbeddedSkinKind>
    {
        [typeof(ArgonSkin)] = BmsEmbeddedSkinKind.LegacyModern,
        [typeof(ArgonProSkin)] = BmsEmbeddedSkinKind.LegacyModern,
        [typeof(TrianglesSkin)] = BmsEmbeddedSkinKind.LegacyModern,
    };

    /// <summary>
    /// Attempts to resolve the BmsEmbeddedSkinKind for a given skin.
    /// Modern skins are checked first; legacy skins are checked as a fallback.
    /// </summary>
    /// <param name="skin">The skin to look up.</param>
    /// <param name="kind">
    /// When this method returns <c>true</c>, the matched BmsEmbeddedSkinKind.
    /// </param>
    /// <returns>
    /// <c>true</c> if the skin's exact runtime type is in MODERN_SKINS
    /// or LEGACY_SKINS; otherwise <c>false</c>.
    /// </returns>
    public static bool TryGetKind(ISkin skin, out BmsEmbeddedSkinKind kind)
    {
        var type = skin.GetType();
        return MODERN_SKINS.TryGetValue(type, out kind) || LEGACY_SKINS.TryGetValue(type, out kind);
    }
}
