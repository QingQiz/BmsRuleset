using System;
using System.Linq;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Resources;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

/// <summary>
/// Computes legacy BMS note cap heights from skin.ini and texture aspect rules.
/// </summary>
/// <remarks>
/// <c>WidthForNoteHeightScale</c> is not a scale factor against the current column width.
/// It means: calculate the note height as if the note texture were drawn at this width.
/// For example, <c>WidthForNoteHeightScale: 30</c> gives the same height the texture would
/// have at 30px width, even if the lane is actually wider.
/// </remarks>
public static class BmsNoteSizing
{
    public const float DEFAULT_NOTE_HEIGHT = 14;

    public static float GetNoteHeight(ISkinSource? skin, BmsSkinComponentLookup lookup, float drawWidth)
    {
        var configuredReferenceWidth = skin?.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;
        var referenceWidth = configuredReferenceWidth ?? drawWidth;
        var textureHeight = getTextureHeightForReferenceWidth(skin, lookup, referenceWidth);

        if (textureHeight != null)
            return textureHeight.Value;

        return configuredReferenceWidth != null ? Math.Max(1, referenceWidth) : DEFAULT_NOTE_HEIGHT;
    }

    private static float? getTextureHeightForReferenceWidth(ISkinSource? skin, BmsSkinComponentLookup lookup, float referenceWidth)
    {
        if (skin == null)
            return null;

        foreach (var name in BmsLegacyTextureResolver.NoteImageCandidates(skin, lookup).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var texture = skin
                .GetTextures(name!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .FirstOrDefault(t => t.DisplayWidth > 0 && t.DisplayHeight > 0);

            if (texture != null)
                return Math.Max(1, texture.DisplayHeight * referenceWidth / texture.DisplayWidth);
        }

        return null;
    }
}
