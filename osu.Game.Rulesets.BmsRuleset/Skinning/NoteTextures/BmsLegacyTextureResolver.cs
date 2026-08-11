using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;

/// <summary>
/// Centralises legacy mania/BMS image-name fallback rules and texture resolution.
/// </summary>
/// <remarks>
/// Keeping these rules outside drawable classes prevents rendering code from also becoming
/// responsible for skin.ini semantics.
/// </remarks>
public static class BmsLegacyTextureResolver
{
    public static string FallbackColumnIndex(BmsSkinComponentLookup lookup)
    {
        if (lookup.IsScratch)
            return "S";

        var maniaColumnsPerStage = lookup.LayoutVariant switch
        {
            BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7KDouble => 7,
            BmsLayoutVariant.Pms9KDouble => 9,
            _ => lookup.ManiaKeyCount,
        };
        var columnInStage = Math.Clamp(lookup.ManiaColumnIndex ?? 0, 0, Math.Max(0, maniaColumnsPerStage - 1)) % maniaColumnsPerStage;
        var distanceToEdge = Math.Min(columnInStage, maniaColumnsPerStage - 1 - columnInStage);
        return distanceToEdge % 2 == 0 ? "1" : "2";
    }

    /// <summary>
    /// Resolves note textures by iterating the fallback chain for the given component
    /// and returning the first set of valid textures (or animation frames) found.
    /// </summary>
    /// <returns>An array of textures (frames for animated notes), or an empty array if no candidate resolved.</returns>
    public static Texture[] ResolveNoteTextures(ISkin skin, BmsSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);

        foreach (var imageName in enumerateNoteCandidates(skin, lookup, fallback))
        {
            if (string.IsNullOrWhiteSpace(imageName))
                continue;

            var textures = skin.GetTextures(imageName, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
                .ToArray();

            if (textures.Length > 0)
                return textures;
        }

        return [];
    }

    /// <summary>
    /// Resolves the first valid note texture for the given component.
    /// Convenience wrapper around <see cref="ResolveNoteTextures"/> for callers that only
    /// need a single texture's dimensions rather than a full animation frame set.
    /// </summary>
    public static Texture? ResolveNoteTexture(ISkin skin, BmsSkinComponentLookup lookup)
        => ResolveNoteTextures(skin, lookup).FirstOrDefault();

    public static IEnumerable<string?> HoldBodyImageCandidates(ISkin skin, BmsSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);
        var configuredBody = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, lookup))?.Value;
        var configuredNote = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
        var bodyIsShortNoteFallback = !string.IsNullOrWhiteSpace(configuredBody) && configuredBody == configuredNote;

        if (bodyIsShortNoteFallback)
        {
            yield return $"mania-note{fallback}L";
            yield return configuredNote;
        }
        else
        {
            yield return configuredBody ?? $"mania-note{fallback}L";
        }
    }

    private static IEnumerable<string?> enumerateNoteCandidates(ISkin? skin, BmsSkinComponentLookup lookup, string fallback)
    {
        switch (lookup.Component)
        {
            case BmsSkinComponents.Mine:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.Hit100, lookup))?.Value
                             ?? "mania-noteS";

                break;

            case BmsSkinComponents.HoldNoteHead:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value
                             ?? $"mania-note{fallback}H";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;

            case BmsSkinComponents.HoldNoteTail:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup))?.Value
                             ?? $"mania-note{fallback}T";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value
                             ?? $"mania-note{fallback}H";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;

            default:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value
                             ?? $"mania-note{fallback}";

                break;
        }
    }
}
