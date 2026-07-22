using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Legacy lane background and separator lines for one BMS column.
/// </summary>
/// <remarks>
/// The lane background and separator widths come from legacy mania/BMS skin.ini values.
/// </remarks>
internal sealed partial class LegacyBmsColumnBackground : CompositeDrawable
{
    internal BmsColumnSeparator LeftSeparator { get; }

    internal BmsColumnSeparator RightSeparator { get; }

    internal BmsHitTargetInsetContainer SeparatorContainer { get; }

    public LegacyBmsColumnBackground(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
        var backgroundColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;

        var totalColumns = BmsLayout.GetTotalColumns(lookup.LayoutVariant);
        var isLastColumn = lookup.ColumnIndex == (BmsLayout.Is2P(lookup.LayoutVariant) ? 0 : totalColumns - 1);

        float leftLineWidth;
        float rightLineWidth;

        if (BmsLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var (lIdx, rIdx) = BmsLayout.RemapColum2PGapIdx(colIdx, totalColumns);

            leftLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth,
                new BmsSkinComponentLookup(lookup.Component, lookup.LayoutVariant, lIdx))?.Value ?? 1;

            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth,
                new BmsSkinComponentLookup(lookup.Component, lookup.LayoutVariant, rIdx))?.Value ?? 1;
        }
        else
        {
            leftLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, lookup)?.Value ?? 1;
            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, lookup)?.Value ?? 1;
        }

        var hasRightLine = (rightLineWidth > 0
                            && transformer.GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version)?.Value >= 2.4m)
                           || isLastColumn;

        InternalChildren =
        [
            LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
            {
                RelativeSizeAxes = Axes.Both,
            }, backgroundColour),
            SeparatorContainer = new BmsHitTargetInsetContainer
            {
                Children =
                [
                    LeftSeparator = new BmsColumnSeparator(leftLineWidth, lineColour)
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                    },
                    RightSeparator = new BmsColumnSeparator(rightLineWidth, lineColour)
                    {
                        X = isLastColumn ? -0.16f : 0,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopLeft,
                        Alpha = hasRightLine ? 1 : 0,
                    },
                ],
            },
        ];
    }
}
