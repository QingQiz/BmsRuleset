using System;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

internal sealed class BmsLegacySkinResourceNames(
    Func<LegacyManiaSkinConfigurationLookups, BmsSkinComponentLookup?, int?, string?> getStringConfig)
{
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
}
