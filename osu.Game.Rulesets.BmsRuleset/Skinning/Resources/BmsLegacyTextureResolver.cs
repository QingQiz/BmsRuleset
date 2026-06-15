using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Resources;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Resources;

/// <summary>
/// Centralises legacy mania/BMS image-name fallback rules.
/// </summary>
/// <remarks>
/// Keeping these rules outside drawable classes prevents rendering code from also becoming
/// responsible for skin.ini semantics. The methods intentionally return candidate names only;
/// callers decide whether they need textures, animations, or raw resource streams.
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

    public static IEnumerable<string?> NoteImageCandidates(ISkin? skin, BmsSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);

        switch (lookup.Component)
        {
            case BmsSkinComponents.Mine:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.Hit100, lookup))?.Value;
                yield return "mania-noteS";

                break;

            case BmsSkinComponents.HoldNoteHead:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value;
                yield return $"mania-note{fallback}H";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
                yield return $"mania-note{fallback}";

                break;

            case BmsSkinComponents.HoldNoteTail:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup))?.Value;
                yield return $"mania-note{fallback}T";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value;
                yield return $"mania-note{fallback}H";
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
                yield return $"mania-note{fallback}";

                break;

            default:
                yield return skin?.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
                yield return $"mania-note{fallback}";

                break;
        }
    }

    public static IEnumerable<string?> HoldBodyImageCandidates(ISkin skin, BmsSkinComponentLookup lookup)
    {
        var fallback = FallbackColumnIndex(lookup);
        var configuredBody = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, lookup))?.Value;
        var configuredNote = skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value;
        var bodyIsShortNoteFallback = !string.IsNullOrWhiteSpace(configuredBody) && configuredBody == configuredNote;

        yield return bodyIsShortNoteFallback ? null : configuredBody;
        yield return $"mania-note{fallback}L";
        yield return configuredNote;
        yield return $"mania-note{fallback}";
    }
}
