using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

/// <inheritdoc />
/// <summary>
/// Skin transformer applied over osu! built-in skins (Argon, ArgonPro, Triangles,
/// DefaultLegacy, Retro) during BMS gameplay.
/// </summary>
/// <remarks>
/// Built-in skins have no <c>skin.ini</c> config and no BMS-specific textures, so this
/// transformer's job is mostly to block lookups that the built-in skin should not answer
/// and to patch the global HUD so it is compatible with BMS gameplay.
/// <para>
/// Routing rules:
/// <list type="bullet">
///   <item><description>
///     Global HUD container (<see cref="F:osu.Game.Skinning.GlobalSkinnableContainers.MainHUDComponents">GlobalSkinnableContainers.MainHUDComponents</see>
///     with no ruleset scope) — passed through to the built-in skin, then wrapped in
///     <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsBuiltInSkinTransformer.HealthFilteredHudContainer">HealthFilteredHudContainer</see> to strip <see cref="T:osu.Game.Screens.Play.HUD.HealthDisplay">HealthDisplay</see>
///     children, because BMS uses its own gauge and does not expose an osu!-compatible
///     health value.
///   </description></item>
///   <item><description>
///     <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsSkinComponentLookup">BmsSkinComponentLookup</see>, <see cref="T:osu.Game.Skinning.SkinComponentLookup`1">SkinComponentLookup{HitResult}</see>,
///     and the ruleset-scoped HUD container — return <c>null</c>. These are handled
///     exclusively by <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkinSource">BmsEmbeddedSkinSource</see>'s embedded skin chain and must
///     not be answered by the built-in skin.
///   </description></item>
///   <item><description>
///     <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsSkinConfigurationLookup">BmsSkinConfigurationLookup</see> — always returns <c>null</c>. Built-in
///     skins carry no <c>skin.ini</c> BMS or mania configuration.
///   </description></item>
///   <item><description>
///     Everything else — forwarded to the wrapped built-in skin unchanged.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public partial class BmsBuiltInSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    /// <inheritdoc/>
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        // Global (non-ruleset) HUD: pass through but strip the health bar, which BMS doesn't use.
        if (lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: null })
            return withoutHealthDisplay(base.GetDrawableComponent(lookup));

        // BMS playfield components, hit results, and the ruleset-scoped HUD are handled
        // by BmsEmbeddedSkinSource — the built-in skin must not answer these.
        if (lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult> or GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null })
            return null;

        return base.GetDrawableComponent(lookup);
    }

    /// <inheritdoc/>
    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        // Built-in skins have no skin.ini BMS/mania config; short-circuit immediately.
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    private static Drawable? withoutHealthDisplay(Drawable? drawable) => drawable is Container container
        ? new HealthFilteredHudContainer(container)
        : drawable;

    /// <summary>
    /// Wraps the built-in skin's global HUD container and removes all
    /// <see cref="HealthDisplay"/> descendants after the container has loaded.
    /// </summary>
    /// <remarks>
    /// Removal is deferred to <see cref="LoadComplete"/> because the children of the
    /// serialisable HUD container are not populated until that point.
    /// The traversal is depth-first so nested containers (e.g. a
    /// <see cref="DefaultSkinComponentsContainer"/> wrapping another container) are
    /// also checked. Removal iterates backwards to avoid index-shift bugs.
    /// </remarks>
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
