using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal static class BmsGameplaySkinMetricsResolver
{
    /// <summary>
    /// Default note height used when no skin texture or configuration provides a value.
    /// </summary>
    public const float DEFAULT_NOTE_HEIGHT = 14;

    public static BmsResolvedNoteMetrics ResolveNoteMetrics(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        var configuredReferenceWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;

        var texture = BmsLegacyTextureResolver.ResolveNoteTexture(skin, lookup);
        if (texture != null)
            return new BmsResolvedNoteMetrics(configuredReferenceWidth, texture.DisplayHeight / texture.DisplayWidth);

        return new BmsResolvedNoteMetrics(configuredReferenceWidth, null);
    }

    /// <summary>
    /// Static fallback for callers without a BmsGameplaySkinCache.
    /// Returns the note height for the given lookup at the given draw width.
    /// </summary>
    public static float ResolveNoteHeight(ISkinSource? skin, BmsSkinComponentLookup lookup, float drawWidth)
    {
        if (skin == null)
            return DEFAULT_NOTE_HEIGHT;

        var metrics = ResolveNoteMetrics(skin, lookup);
        return metrics.HeightFor(drawWidth);
    }
}
