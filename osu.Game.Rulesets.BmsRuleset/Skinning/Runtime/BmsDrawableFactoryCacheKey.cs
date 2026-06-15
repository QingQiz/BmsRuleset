using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal readonly record struct BmsDrawableFactoryCacheKey(
    BmsSkinComponents Component,
    BmsLayoutVariant LayoutVariant,
    int? Column,
    bool IsLongNote)
{
    public static BmsDrawableFactoryCacheKey From(BmsSkinComponentLookup lookup) =>
        new(lookup.Component, lookup.LayoutVariant, lookup.ColumnIndex, lookup.IsLongNote);
}
