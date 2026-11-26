using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;

internal static class BmsStageColumnLayout
{
    internal static float DefaultWidth(int column, BmsLayoutVariant layout) =>
        BmsLayout.IsScratchColumn(column, layout) ? BmsColumn.SCRATCH_COLUMN_WIDTH : BmsColumn.COLUMN_WIDTH;

    internal static float GetWidth(ISkinSource skin, BmsSkinComponentLookup lookup) =>
        skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup))?.Value
        ?? DefaultWidth(lookup.ColumnIndex!.Value, lookup.LayoutVariant);

    internal static MarginPadding GetMargin(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        var column = lookup.ColumnIndex!.Value;
        var layout = lookup.LayoutVariant;
        var count = BmsLayout.GetTotalColumns(layout);
        var visualIndex = VisualIndex(layout, column, count);
        var style = (skin as BmsEmbeddedSkinSource)?.StageSkin?.Style;
        return new MarginPadding
        {
            Left = visualIndex > 0 && style?.Gaps.TryGetValue(visualIndex - 1, out var leftGap) == true ? leftGap / 2 : getLegacySpacing(skin, lookup, true),
            Right = visualIndex < count - 1 && style?.Gaps.TryGetValue(visualIndex, out var rightGap) == true ? rightGap / 2 : getLegacySpacing(skin, lookup, false),
        };
    }

    internal static float GetLegacyGap(ISkin skin, BmsSkinComponentLookup left, BmsSkinComponentLookup right) =>
        getLegacySpacing(skin, left, false) + getLegacySpacing(skin, right, true);

    private static float getLegacySpacing(ISkin skin, BmsSkinComponentLookup lookup, bool left) =>
        getLegacyBoundaryValue(skin, lookup, left ? LegacyManiaSkinConfigurationLookups.LeftColumnSpacing : LegacyManiaSkinConfigurationLookups.RightColumnSpacing, left) ?? 0;

    internal static float GetLegacyLineWidth(ISkin skin, BmsSkinComponentLookup lookup, bool left) =>
        getLegacyBoundaryValue(skin, lookup, left ? LegacyManiaSkinConfigurationLookups.LeftLineWidth : LegacyManiaSkinConfigurationLookups.RightLineWidth, left) ?? 1;

    private static float? getLegacyBoundaryValue(ISkin skin, BmsSkinComponentLookup lookup, LegacyManiaSkinConfigurationLookups key, bool left)
    {
        if (BmsLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int column)
        {
            var (leftIndex, rightIndex) = BmsLayout.RemapColum2PGapIdx(column, BmsLayout.GetTotalColumns(lookup.LayoutVariant));
            lookup = new BmsSkinComponentLookup(lookup.Component, lookup.LayoutVariant, left ? leftIndex : rightIndex);
        }

        return skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(key, lookup))?.Value;
    }

    internal static int VisualIndex(BmsLayoutVariant layout, int column, int count) =>
        BmsLayout.Is2P(layout) ? (column + count - 1) % count : column;

    internal static int ColumnAtVisualIndex(BmsLayoutVariant layout, int index, int count) =>
        BmsLayout.Is2P(layout) ? (index + 1) % count : index;
}
