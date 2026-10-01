using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

internal sealed partial class BmsNoteVisual : Container
{
    private readonly BmsCachedSkinnableDrawable skin;

    internal static void ApplyResultEffects(Drawable drawable, ArmedState state, double time)
    {
        switch (state)
        {
            case ArmedState.Hit:
                drawable.FadeOut();
                drawable.LifetimeEnd = time;
                break;

            case ArmedState.Miss:
                drawable.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                drawable.LifetimeEnd = time + 300;
                break;
        }
    }

    internal BmsNoteVisual(BmsSkinComponents component, BmsLayoutVariant layout, int column)
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        RelativeSizeAxes = Axes.X;
        Child = skin = new BmsCachedSkinnableDrawable(new BmsSkinComponentLookup(component, layout, column))
        {
            ComponentAnchor = Anchor.BottomCentre,
        };
    }

    internal float VisualHeight
    {
        get
        {
            var drawable = skin.Drawable;
            if (drawable == null) return 0;
            // Absolute skin heights avoid waking width-dependent layout on culled trees.
            return (drawable.RelativeSizeAxes & Axes.Y) == 0 ? drawable.Height : drawable.DrawHeight;
        }
    }
}
