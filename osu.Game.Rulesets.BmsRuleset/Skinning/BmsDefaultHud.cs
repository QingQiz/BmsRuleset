using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;
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

        return containerLookup.Ruleset == null ? gHud() : bmsHud();
    }

    /// <summary>
    /// playfield, etc. hud in this can resolve DI in BmsDrawableRuleset
    /// </summary>
    /// <returns></returns>
    private static Drawable bmsHud()
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            Children =
            [
                new BmsTextHud(),
                new BmsHealthDisplay(),
                new LegacyScoreCounter(),
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
