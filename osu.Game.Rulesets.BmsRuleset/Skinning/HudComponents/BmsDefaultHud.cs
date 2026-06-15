using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;

public static class BmsDefaultHud
{
    public static Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is not GlobalSkinnableContainerLookup containerLookup)
            return null;

        if (containerLookup.Lookup != GlobalSkinnableContainers.MainHUDComponents)
            return null;

        return containerLookup.Ruleset == null ? gHud() : bmsHud();
    }

    /// <summary>
    /// playfield, etc. hud in this can resolve DI in BmsDrawableRuleset
    /// </summary>
    /// <returns></returns>
    private static Drawable bmsHud()
    {
        return new DefaultSkinComponentsContainer(_ => { })
        {
            Children =
            [
                new BmsTextHud(),
                new BmsHealthDisplay
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Margin = new MarginPadding { Horizontal = 10, Vertical = 20 },
                },
                new BarHitErrorMeter
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.CentreRight,
                    Rotation = 90,
                    Scale = new Vector2(2),
                },
                new BmsComboCounter(),
                new ArgonScoreCounter
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
                new ArgonAccuracyCounter
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Top = 60 },
                },
            ],
        };
    }

    /// <summary>
    /// hud. hud in this can NOT resolve DI in BmsDrawableRuleset
    /// </summary>
    /// <returns></returns>
    private static Drawable gHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            Children = [],
        };
    }
}
