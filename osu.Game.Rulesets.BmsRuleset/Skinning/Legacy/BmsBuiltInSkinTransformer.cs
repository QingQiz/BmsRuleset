using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;

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
///     Global HUD container (GlobalSkinnableContainers.MainHUDComponents
///     with no ruleset scope) — passed through to the built-in skin, then wrapped in
///     HealthFilteredHudContainer to strip HealthDisplay
///     children, because BMS uses its own gauge and does not expose an osu!-compatible
///     health value.
///   </description></item>
///   <item><description>
///     BmsSkinComponentLookup, SkinComponentLookup{HitResult},
///     and the ruleset-scoped HUD container — return <c>null</c>. These are handled
///     exclusively by BmsEmbeddedSkinSource's embedded skin chain and must
///     not be answered by the built-in skin.
///   </description></item>
///   <item><description>
///     BmsSkinConfigurationLookup — always returns <c>null</c>. Built-in
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
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult>
            ? null
            : BmsDefaultHud.GetDrawableComponent(lookup) ?? base.GetDrawableComponent(lookup);

    /// <inheritdoc/>
    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        // Built-in skins have no skin.ini BMS/mania config; short-circuit immediately.
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }

    internal static Drawable? WithoutHealthDisplay(Drawable? drawable) => drawable is Container container
        ? new HealthFilteredHudContainer(container)
        : drawable;

    /// <summary>
    /// Wraps the built-in skin's global HUD container and removes all
    /// HealthDisplay descendants after the container has loaded.
    /// </summary>
    /// <remarks>
    /// Removal is deferred to LoadComplete because the children of the
    /// serialisable HUD container are not populated until that point.
    /// The traversal is depth-first so nested containers (e.g. a
    /// DefaultSkinComponentsContainer wrapping another container) are
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
