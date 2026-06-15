using System.Collections.Generic;
using osu.Framework.Graphics.Rendering;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;

/// <summary>
/// Builds the embedded BMS skin fallback chain that matches the currently active osu! skin style.
/// </summary>
public static class BmsEmbeddedSkinFallbackFactory
{
    public static BmsEmbeddedSkinFallbackChain Create(IEnumerable<ISkin> parentSources, BmsBeatmap beatmap, IRenderer renderer)
    {
        var kind = GetEmbeddedSkinKind(parentSources);
        var primary = createTransformer(kind, beatmap, renderer);
        var fallback = kind == BmsEmbeddedSkinKind.LegacyOld
            ? null
            : createTransformer(BmsEmbeddedSkinKind.LegacyOld, beatmap, renderer);

        return new BmsEmbeddedSkinFallbackChain(primary, fallback);
    }

    /// <summary>
    /// Determines which BmsEmbeddedSkinKind to use based on the currently active skin sources.
    /// </summary>
    public static BmsEmbeddedSkinKind GetEmbeddedSkinKind(IEnumerable<ISkin> sources)
    {
        foreach (var source in sources)
        {
            var skin = source is ISkinTransformer transformer ? transformer.Skin : source;

            if (skin is LegacyBeatmapSkin or BmsEmbeddedSkin)
                continue;

            if (BmsEmbeddedSkinDefinition.TryGetKind(skin, out var kind))
                return kind;

            if (skin is Skin)
                return BmsEmbeddedSkinKind.LegacyOld;
        }

        return BmsEmbeddedSkinKind.LegacyOld;
    }

    private static BmsLegacySkinTransformer createTransformer(BmsEmbeddedSkinKind kind, BmsBeatmap beatmap, IRenderer renderer) =>
        new(new BmsEmbeddedSkin(kind, renderer), beatmap);
}
