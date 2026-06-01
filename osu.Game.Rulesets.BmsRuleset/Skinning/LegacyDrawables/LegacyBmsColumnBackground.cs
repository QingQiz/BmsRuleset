using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Legacy lane background, separator lines, and column light for one BMS column.
/// </summary>
/// <remarks>
/// The lane background and separator widths come from legacy mania/BMS skin.ini values. The column
/// light is an input-reactive additive visual: it fades in while the corresponding key is held and
/// collapses vertically on release, matching the old mania visual language.
/// </remarks>
internal sealed partial class LegacyBmsColumnBackground : CompositeDrawable, IKeyBindingHandler<BmsAction>
{
    private readonly BmsSkinComponentLookup lookup;
    private readonly Drawable? light;

    public LegacyBmsColumnBackground(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;

        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
        var backgroundColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;
        var lightColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        var lightImage = transformer.GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LightImage, lookup)?.Value ?? "mania-stage-light";
        var lightPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, lookup)?.Value ?? 0;
        var lightFramePerSecond = transformer.GetManiaConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, lookup)?.Value ?? 60;

        // For 2P variants, the visual column order is [keys…, scratch]. ColumnLineWidth
        // indices must be remapped so the separator lines follow visual adjacency, not
        // BMS index order.
        var is2P = lookup.LayoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P;
        var totalColumns = BmsLayout.GetTotalColumns(lookup.LayoutVariant);

        float leftLineWidth;
        float rightLineWidth;

        if (is2P && lookup.ColumnIndex is int colIdx)
        {
            // Visual position in the reordered layout: scratch (BMS 0) is last, keys shift left.
            var v = colIdx == 0 ? totalColumns - 1 : colIdx - 1;

            // Left edge index in ColumnLineWidth[]:
            //   v=0 (stage left)       → 0
            //   v=N-1 (scratch)        → 1 (between BMS 0 and 1)
            //   otherwise              → v+1 (maps to same as original right-edge of preceding col)
            var lineLeft = v == 0 ? 0 : v == totalColumns - 1 ? 1 : v + 1;

            // Right edge index:
            //   v=N-1 (scratch, rightmost) → N (stage right border)
            //   v=N-2 (last key)           → 1 (between BMS 0 and 1, now adjacent to scratch)
            //   otherwise                   → v+2
            var lineRight = v == totalColumns - 1 ? totalColumns : v == totalColumns - 2 ? 1 : v + 2;

            leftLineWidth = colIdx == 0
                ? transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth,
                    new BmsSkinComponentLookup(lookup.Component, lookup.LayoutVariant, lineLeft))?.Value ?? 1
                : 0;

            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth,
                new BmsSkinComponentLookup(lookup.Component, lookup.LayoutVariant, lineRight))?.Value ?? 1;
        }
        else
        {
            leftLineWidth = lookup.ColumnIndex == 0
                ? transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, lookup)?.Value ?? 1
                : 0;

            rightLineWidth = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, lookup)?.Value ?? 1;
        }

        light = transformer.GetAnimation(lightImage, true, true, frameLength: 1000d / lightFramePerSecond)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.Y = -lightPosition;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(lightColour);
            d.Alpha = 0;
        });

        InternalChildren =
        [
            LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box
            {
                RelativeSizeAxes = Axes.Both,
            }, backgroundColour),
            light ?? Empty(),
            new BmsColumnSeparator(leftLineWidth, lineColour)
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
            },
            new BmsColumnSeparator(rightLineWidth, lineColour)
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
            },
        ];
    }

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        light?.FadeIn();
        light?.ScaleTo(Vector2.One);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
    {
        if (lookup.ColumnIndex == null || BmsKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        light?.FadeTo(0, 250);
        light?.ScaleTo(new Vector2(1, 0), 250);
    }
}
