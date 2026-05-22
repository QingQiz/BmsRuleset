using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public class BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponentLookup? componentLookup = null, int? columnIndex = null)
{
    public readonly LegacyManiaSkinConfigurationLookups Lookup = lookup;

    public readonly BmsSkinComponentLookup? ComponentLookup = componentLookup;

    public readonly int? ColumnIndex = columnIndex;
}
