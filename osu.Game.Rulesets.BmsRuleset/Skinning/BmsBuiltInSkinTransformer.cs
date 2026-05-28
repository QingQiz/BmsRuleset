using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public partial class BmsBuiltInSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: null })
            return withoutHealthDisplay(base.GetDrawableComponent(lookup));

        if (lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult> or GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null })
            return null;

        return base.GetDrawableComponent(lookup);
    }

    private static Drawable? withoutHealthDisplay(Drawable? drawable) => drawable is Container container
        ? new HealthFilteredHudContainer(container)
        : drawable;

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    private sealed partial class HealthFilteredHudContainer : Container
    {
        private readonly Container container;

        public HealthFilteredHudContainer(Container container)
        {
            this.container = container;
            RelativeSizeAxes = Axes.Both;
            InternalChild = container;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            removeHealthDisplays(container);
        }

        private static void removeHealthDisplays(Container target)
        {
            for (var i = target.Children.Count - 1; i >= 0; i--)
            {
                var child = target.Children[i];

                if (child is HealthDisplay)
                    target.Remove(child, true);
                else if (child is Container nested)
                    removeHealthDisplays(nested);
            }
        }
    }
}
