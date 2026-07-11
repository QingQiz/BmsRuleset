using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Skinning;
using osuTK;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI;
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
    private readonly float lightPosition;

    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    public LegacyBmsColumnBackground(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;

        var lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, lookup)?.Value ?? Color4.White;
        var backgroundColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup)?.Value ?? Color4.Black;
        var lightColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        var lightImage = transformer.GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LightImage, lookup)?.Value ?? "mania-stage-light";
        lightPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, lookup)?.Value ?? 0;
        var lightFramePerSecond = transformer.GetManiaConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, lookup)?.Value ?? 60;

        var totalColumns = BmsLayout.GetTotalColumns(lookup.LayoutVariant);

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

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (playfield == null)
            return;

        playfield.Stage.HitTargetPositionOffsetChanged += updateLightPosition;
        updateLightPosition(playfield.Stage.HitTargetPositionOffset);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (playfield != null)
            playfield.Stage.HitTargetPositionOffsetChanged -= updateLightPosition;

        base.Dispose(isDisposing);
    }

    private void updateLightPosition(float offset)
    {
        light?.Y = -(lightPosition + offset);
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
