using osu.Framework.Graphics.Rendering;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal readonly record struct BmsLongNoteBodyCacheKey(
    BmsSkinComponents Component,
    BmsLayoutVariant LayoutVariant,
    int? Column,
    bool IsLongNote,
    IRenderer Renderer)
{
    public static BmsLongNoteBodyCacheKey From(BmsSkinComponentLookup lookup, IRenderer renderer) =>
        new(lookup.Component, lookup.LayoutVariant, lookup.ColumnIndex, lookup.IsLongNote, renderer);
}
