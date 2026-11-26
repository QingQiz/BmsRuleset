using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

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

    private readonly BmsSkinComponentLookup lookup;
    private readonly float lineOpacity;
    private readonly bool hasRightLine;

    public LegacyBmsColumnBackground(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;

        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
        lineOpacity = lineColour.A;
        var backgroundColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;

        var totalColumns = BmsLayout.GetTotalColumns(lookup.LayoutVariant);
        var isLastColumn = lookup.ColumnIndex == (BmsLayout.Is2P(lookup.LayoutVariant) ? 0 : totalColumns - 1);

        var leftLineWidth = BmsStageColumnLayout.GetLegacyLineWidth(transformer, lookup, true);
        var rightLineWidth = BmsStageColumnLayout.GetLegacyLineWidth(transformer, lookup, false);

        hasRightLine = (rightLineWidth > 0
                && transformer.GetConfig<SkinConfiguration.LegacySetting, decimal>(SkinConfiguration.LegacySetting.Version)?.Value >= 2.4m)
                      || isLastColumn;

        InternalChildren =
        [
            LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Depth = 1,
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
                        Alpha = hasRightLine ? lineColour.A : 0,
                    },
                ],
            },
        ];
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        var stageSkin = (skin as BmsEmbeddedSkinSource)?.StageSkin;
        if (stageSkin == null)
            return;

        if (BmsStageSkin.GetDrawable(skin, BmsStageImage.ColumnBackground, lookup.ColumnIndex) is { } background)
        {
            background.RelativeSizeAxes = Axes.Both;
            background.Depth = 0.5f;
            AddInternal(background);
        }

        if (lookup.ColumnIndex is not int column)
            return;

        var count = BmsLayout.GetTotalColumns(lookup.LayoutVariant);
        var visualIndex = BmsStageColumnLayout.VisualIndex(lookup.LayoutVariant, column, count);
        var style = stageSkin.Style;
        var separators = style.SeparatorWidths;
        var opacity = lineOpacity;
        if (style.GetSeparatorColour() is { } colour)
        {
            // Colours set in the editor use their actual alpha rather than legacy doubled alpha.
            LeftSeparator.Colour = RightSeparator.Colour = colour;
            LeftSeparator.Alpha = LeftSeparator.Width > 0 ? 1 : 0;
            RightSeparator.Alpha = hasRightLine ? 1 : 0;
            opacity = 1;
        }

        if (separators.TryGetValue(visualIndex - 1, out var width))
        {
            LeftSeparator.Width = width;
            LeftSeparator.Scale = Vector2.One;
            LeftSeparator.Origin = Anchor.TopCentre;
            LeftSeparator.X = -BmsStageColumnLayout.GetMargin(skin, lookup).Left;
            LeftSeparator.Alpha = width > 0 ? opacity : 0;
        }

        if (separators.ContainsKey(visualIndex))
            RightSeparator.Hide();
    }
}
