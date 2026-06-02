using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public static class BmsDefaultHud
{
    public static Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is not GlobalSkinnableContainerLookup containerLookup)
            return null;

        if (containerLookup.Lookup != GlobalSkinnableContainers.MainHUDComponents)
            return null;

        return containerLookup.Ruleset == null ? globalHud() : rulesetHud();
    }

    /// <summary>
    /// play field
    /// </summary>
    /// <returns></returns>
    private static Drawable rulesetHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            Children =
            [
                // new ArgonSongProgress(),
                new BmsTextHud(),

                new BarHitErrorMeter
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.CentreLeft,
                    Rotation = -90,
                },
            ],
        };
    }

    /// <summary>
    /// score, acc, combo, etc
    /// </summary>
    /// <returns></returns>
    private static Drawable globalHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            Children =
            [
                new LegacyScoreCounter(),
            ],
        };
    }
}
