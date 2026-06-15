using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Resources;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

internal sealed class BmsLegacySkinResourceNames(
    Func<LegacyManiaSkinConfigurationLookups, BmsSkinComponentLookup?, int?, string?> getStringConfig,
    Func<string, bool> hasAnimation)
{
    public string GetNoteImageName(BmsSkinComponentLookup lookup) =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.NoteImage, lookup, null)
        ?? $"mania-note{BmsLegacyTextureResolver.FallbackColumnIndex(lookup)}";

    public string GetHoldNoteHeadImageName(BmsSkinComponentLookup lookup) =>
        getFirstAnimationName(getHoldNoteHeadImageNames(lookup)) ?? GetNoteImageName(lookup);

    public string GetHoldNoteTailImageName(BmsSkinComponentLookup lookup) =>
        getFirstAnimationName(getHoldNoteTailImageNames(lookup)) ?? GetHoldNoteHeadImageName(lookup);

    public string GetMineImageName(BmsSkinComponentLookup lookup) =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.Hit100, lookup, null)
        ?? "mania-noteS";

    public string GetKeyImageName(BmsSkinComponentLookup lookup, bool down) =>
        getStringConfig(down ? LegacyManiaSkinConfigurationLookups.KeyImageDown : LegacyManiaSkinConfigurationLookups.KeyImage, lookup, null)
        ?? $"mania-key{BmsLegacyTextureResolver.FallbackColumnIndex(lookup)}{(down ? "D" : string.Empty)}";

    public string GetHitExplosionImageName(BmsSkinComponentLookup lookup) =>
        lookup.IsLongNote
            ? getStringConfig(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, lookup, null) ?? "lightingL"
            : getStringConfig(LegacyManiaSkinConfigurationLookups.ExplosionImage, lookup, null) ?? "lightingN";

    public string GetHitTargetImageName() =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.HitTargetImage, null, null) ?? "mania-stage-hint";

    public string[] GetStageBackgroundImageNames() =>
    [
        getStringConfig(LegacyManiaSkinConfigurationLookups.LeftStageImage, null, null) ?? "mania-stage-left",
        getStringConfig(LegacyManiaSkinConfigurationLookups.RightStageImage, null, null) ?? "mania-stage-right",
    ];

    public string GetStageForegroundImageName() =>
        getStringConfig(LegacyManiaSkinConfigurationLookups.BottomStageImage, null, null) ?? "mania-stage-bottom";

    private string[] getHoldNoteHeadImageNames(BmsSkinComponentLookup lookup) =>
    [
        getStringConfig(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup, null) ?? string.Empty,
        GetNoteImageName(lookup),
    ];

    private string[] getHoldNoteTailImageNames(BmsSkinComponentLookup lookup) =>
    [
        getStringConfig(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup, null) ?? string.Empty,
        getStringConfig(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup, null) ?? string.Empty,
        GetNoteImageName(lookup),
    ];

    private string? getFirstAnimationName(IEnumerable<string> names)
        => names.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name) && hasAnimation(name));
}
