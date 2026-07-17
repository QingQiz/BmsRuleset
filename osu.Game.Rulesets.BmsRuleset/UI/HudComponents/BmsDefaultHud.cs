using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.HUD.HitErrorMeters;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public static class BmsDefaultHud
{
    public static Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is not GlobalSkinnableContainerLookup containerLookup)
            return null;

        switch (containerLookup.Lookup)
        {
            case GlobalSkinnableContainers.MainHUDComponents:
                return containerLookup.Ruleset == null ? gHud() : bmsHud();

            case GlobalSkinnableContainers.Playfield:
                return bmsPlayfield();

            case GlobalSkinnableContainers.SongSelect:
                break;
        }

        return null;
    }

    private static Drawable bmsPlayfield()
    {
        return new DefaultSkinComponentsContainer(_ => { })
        {
            Children =
            [
                new BmsSongProgress
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomRight,
                    X = -15,
                },
                new BmsTextHud(),
                new BmsHealthDisplay
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomLeft,
                    X = 5,
                },
                new BmsJudgementDisplay(),
                new BmsComboCounter(),
            ],
        };
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
                new BmsStageHud(),
                new BmsScoreGraph
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Scale = new Vector2(1.5f),
                },
                new BmsBgaDisplay
                {
                    AutoSizeToParent = true,
                    RenderOutsideHudVisibility = true,
                    Depth = float.MaxValue,
                },
                new BarHitErrorMeter
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.CentreRight,
                    Rotation = 90,
                    Scale = new Vector2(2),
                },
                new ArgonScoreCounter
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                },
                new ArgonAccuracyCounter
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Y = 60,
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
