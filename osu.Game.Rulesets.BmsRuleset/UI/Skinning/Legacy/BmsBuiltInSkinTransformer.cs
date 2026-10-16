using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;

/// <inheritdoc />
/// <summary>
/// Skin transformer applied over osu! built-in skins (Argon, ArgonPro, Triangles,
/// DefaultLegacy, Retro) during BMS gameplay.
/// </summary>
public partial class BmsBuiltInSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    /// <inheritdoc/>
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        if (BmsDefaultHud.TryGetMainHudWithStage(lookup, () => base.GetDrawableComponent(lookup), out var mainHud))
            return mainHud;

        var component = lookup is BmsSkinComponentLookup or SkinComponentLookup<HitResult>
            ? null
            : BmsDefaultHud.GetDrawableComponent(lookup) ?? base.GetDrawableComponent(lookup);

        if (lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: null }
            && component is DefaultSkinComponentsContainer container)
        {
            var components = container.ToArray();
            for (var i = 0; i < components.Length; i++)
            {
                var original = components[i];
                if (original.GetType() == typeof(ArgonKeyCounterDisplay))
                    components[i] = new BmsKeyCounterDisplay();
                else if (original.GetType() == typeof(ArgonScoreCounter))
                {
                    var counter = (ArgonScoreCounter)original;
                    components[i] = new BmsScoreCounter
                    {
                        ShowLabel = { Value = counter.ShowLabel.Value },
                        WireframeOpacity = { Value = counter.WireframeOpacity.Value },
                    };
                }
                else
                    continue;

                container.Remove(original, true);
            }

            // Keep draw order and let the skin's original layout position the replacements.
            container.Clear(false);
            container.AddRange(components);
        }

        return component;
    }

    /// <inheritdoc/>
    /// return null for every skin lookup, thus fall through to BmsEmbeddedSkinFallbackChain
    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        // Built-in skins have no skin.ini BMS/mania config; short-circuit immediately.
        if (lookup is BmsSkinConfigurationLookup)
            return null;

        return base.GetConfig<TLookup, TValue>(lookup);
    }
}
