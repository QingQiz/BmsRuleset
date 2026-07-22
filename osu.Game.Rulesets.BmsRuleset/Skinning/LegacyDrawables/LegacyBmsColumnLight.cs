using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Separated from the lane background so legacy ordering places the light above the hit target but below notes.
/// </summary>
internal sealed partial class LegacyBmsColumnLight : CompositeDrawable, IKeyBindingHandler<BmsAction>
{
    private readonly BmsSkinComponentLookup lookup;
    private readonly string lightImage;
    private readonly Color4 lightColour;
    private readonly double lightFrameLength;
    private readonly float lightPosition;

    private Drawable? light;

    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    public LegacyBmsColumnLight(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        RelativeSizeAxes = Axes.Both;

        lightColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        lightImage = transformer.GetManiaConfig<string>(LegacyManiaSkinConfigurationLookups.LightImage, lookup)?.Value ?? "mania-stage-light";
        lightPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, lookup)?.Value ?? 0;
        var lightFramePerSecond = transformer.GetManiaConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, lookup)?.Value ?? 60;
        lightFrameLength = 1000d / lightFramePerSecond;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        // The active source includes the embedded legacy assets used when a user skin omits mania-stage-light.
        InternalChild = light = skin.GetAnimation(lightImage, true, true, frameLength: lightFrameLength)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.Y = -lightPosition;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(lightColour);
            d.Alpha = 0;
        }) ?? Empty();
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
