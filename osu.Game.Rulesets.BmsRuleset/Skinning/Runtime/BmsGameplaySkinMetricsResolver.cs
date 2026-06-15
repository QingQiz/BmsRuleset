using System.Linq;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Resources;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal static class BmsGameplaySkinMetricsResolver
{
    public static BmsResolvedNoteMetrics ResolveNoteMetrics(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        var configuredReferenceWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;

        foreach (var name in BmsLegacyTextureResolver.NoteImageCandidates(skin, lookup).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var texture = skin
                .GetTextures(name!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .FirstOrDefault(t => t.DisplayWidth > 0 && t.DisplayHeight > 0);

            if (texture != null)
                return new BmsResolvedNoteMetrics(configuredReferenceWidth, texture.DisplayHeight / texture.DisplayWidth);
        }

        return new BmsResolvedNoteMetrics(configuredReferenceWidth, null);
    }
}
