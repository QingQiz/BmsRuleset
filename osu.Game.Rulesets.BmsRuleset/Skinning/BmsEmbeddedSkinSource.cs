using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

/// <inheritdoc cref="ISkinSource" />
/// <summary>
/// The root <see cref="T:osu.Game.Skinning.ISkinSource">ISkinSource</see> used during BMS gameplay.
/// </summary>
/// <remarks>
/// Implements a three-tier fallback chain:
/// <list type="number">
///   <item><description>
///     <b>parent</b> — the full osu! skin source (user skin + beatmap skin + defaults),
///     supplied by the framework's skin manager.
///   </description></item>
///   <item><description>
///     <b>primary</b> — a <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsLegacySkinTransformer">BmsLegacySkinTransformer</see> wrapping a
///     <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkin">BmsEmbeddedSkin</see> whose asset set matches the user's active skin style
///     (e.g. <see cref="F:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkinKind.LegacyModern">BmsEmbeddedSkinKind.LegacyModern</see> when Argon is selected).
///   </description></item>
///   <item><description>
///     <b>fallback</b> — a <see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsLegacySkinTransformer">BmsLegacySkinTransformer</see> wrapping the
///     <see cref="F:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkinKind.LegacyOld">BmsEmbeddedSkinKind.LegacyOld</see> embedded skin, used as the
///     last-resort when both the parent chain and the primary embedded skin fail.
///   </description></item>
/// </list>
/// Routing is selective: BMS-specific lookups (<see cref="T:osu.Game.Rulesets.BmsRuleset.Skinning.BmsSkinComponentLookup">BmsSkinComponentLookup</see>,
/// hit results, and the in-ruleset HUD container) traverse all three tiers;
/// generic osu! lookups are forwarded to the <b>parent only</b>, preventing
/// the embedded skins from leaking BMS-specific resources into general osu! UI.
/// </remarks>
public sealed class BmsEmbeddedSkinSource : ISkinSource, IDisposable
{

    /// <inheritdoc/>
    public IEnumerable<ISkin> AllSources
    {
        get
        {
            if (parent != null)
            {
                foreach (var source in parent.AllSources)
                    yield return source;
            }

            if (primary != null)
                yield return primary;

            if (fallback != null)
                yield return fallback;
        }
    }

    private ISkinSource? parent;
    private BmsLegacySkinTransformer? primary;
    private BmsLegacySkinTransformer? fallback;

    #region Disposal

    /// <inheritdoc/>
    public void Dispose() => DisposeEmbeddedSkins();

    #endregion

    /// <summary>
    /// Determines which <see cref="BmsEmbeddedSkinKind"/> to use based on the
    /// currently active skin sources.
    /// </summary>
    /// <remarks>
    /// Iterates <paramref name="sources"/> in priority order (highest first).
    /// Each source is unwrapped through <see cref="ISkinTransformer"/> if needed.
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="LegacyBeatmapSkin"/> is skipped — it carries beatmap media, not
    ///     user aesthetic preference, and must not influence the embedded asset set.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="BmsEmbeddedSkin"/> is skipped — it is one of the embedded skins
    ///     already managed by this source and would create a circular reference.
    ///   </description></item>
    ///   <item><description>
    ///     Any skin whose exact runtime type is in <see cref="BmsEmbeddedSkinDefinition"/>
    ///     returns the mapped <see cref="BmsEmbeddedSkinKind"/>.
    ///   </description></item>
    ///   <item><description>
    ///     Any other concrete <see cref="Skin"/> subclass (unrecognised user skin) defaults
    ///     to <see cref="BmsEmbeddedSkinKind.LegacyOld"/>.
    ///   </description></item>
    /// </list>
    /// Returns <see cref="BmsEmbeddedSkinKind.LegacyOld"/> when no qualifying skin is found.
    /// </remarks>
    public static BmsEmbeddedSkinKind GetEmbeddedSkinKind(IEnumerable<ISkin> sources)
    {
        foreach (var source in sources)
        {
            var skin = source is ISkinTransformer transformer ? transformer.Skin : source;

            if (skin is LegacyBeatmapSkin or BmsEmbeddedSkin)
                continue;

            if (BmsEmbeddedSkinDefinition.TryGetKind(skin, out var kind))
                return kind;

            if (skin is Skin)
                return BmsEmbeddedSkinKind.LegacyOld;
        }

        return BmsEmbeddedSkinKind.LegacyOld;
    }

    /// <summary>
    /// Replaces the current skin sources with new ones, disposing any previously held
    /// embedded skins before taking ownership of the new ones.
    /// </summary>
    /// <param name="parent">The osu! skin source chain (must not be null).</param>
    /// <param name="primary">
    /// Transformer wrapping the style-matched embedded skin, or <c>null</c>
    /// when the style matches <see cref="BmsEmbeddedSkinKind.LegacyOld"/> (no separate primary needed).
    /// </param>
    /// <param name="fallback">
    /// Transformer wrapping the <see cref="BmsEmbeddedSkinKind.LegacyOld"/> embedded skin,
    /// always present as the last resort.
    /// </param>
    public void SetSources(ISkinSource parent, BmsLegacySkinTransformer? primary, BmsLegacySkinTransformer? fallback)
    {
        DisposeEmbeddedSkins();

        this.parent = parent;
        this.primary = primary;
        this.fallback = fallback;
        SourceChanged?.Invoke();
    }

    /// <summary>
    /// Routes drawable component lookups through the three-tier chain.
    /// </summary>
    /// <remarks>
    /// BMS-specific lookups (<see cref="BmsSkinComponentLookup"/>,
    /// <see cref="SkinComponentLookup{HitResult}"/>, and the ruleset-scoped
    /// <see cref="GlobalSkinnableContainers.MainHUDComponents"/> container) fall
    /// through <b>parent → primary → fallback</b>.
    /// All other lookups are forwarded to the <b>parent only</b>.
    /// </remarks>
    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        lookup is
            BmsSkinComponentLookup or
            SkinComponentLookup<HitResult> or
            GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null }
            ? parent?.GetDrawableComponent(lookup)
              ?? primary?.GetDrawableComponent(lookup)
              ?? fallback?.GetDrawableComponent(lookup)
            : parent?.GetDrawableComponent(lookup);

    /// <summary>Looks up a texture, falling through parent → primary → fallback.</summary>
    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        parent?.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? primary?.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? fallback?.GetTexture(componentName, wrapModeS, wrapModeT);

    /// <summary>Looks up a sample, falling through parent → primary → fallback.</summary>
    public ISample? GetSample(ISampleInfo sampleInfo) =>
        parent?.GetSample(sampleInfo) ?? primary?.GetSample(sampleInfo) ?? fallback?.GetSample(sampleInfo);

    /// <summary>
    /// Routes configuration lookups through the three-tier chain.
    /// </summary>
    /// <remarks>
    /// <see cref="BmsSkinConfigurationLookup"/> falls through parent → primary → fallback.
    /// All other lookups are forwarded to the parent only.
    /// </remarks>
    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => lookup is BmsSkinConfigurationLookup
            ? parent?.GetConfig<TLookup, TValue>(lookup)
              ?? primary?.GetConfig<TLookup, TValue>(lookup)
              ?? fallback?.GetConfig<TLookup, TValue>(lookup)
            : parent?.GetConfig<TLookup, TValue>(lookup);

    /// <inheritdoc/>
    public ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (parent?.FindProvider(lookupFunction) is { } provider)
            return provider;

        if (primary != null && lookupFunction(primary))
            return primary;

        if (fallback != null && lookupFunction(fallback))
            return fallback;

        return null;
    }

    /// <summary>
    /// Disposes and nulls the currently held embedded skin transformers
    /// without affecting the parent source.
    /// </summary>
    /// <remarks>
    /// Each transformer's underlying <see cref="BmsEmbeddedSkin"/> is disposed
    /// (via <see cref="IDisposable"/>) to release DLL store and texture/sample resources.
    /// </remarks>
    public void DisposeEmbeddedSkins()
    {
        dispose(primary);
        dispose(fallback);

        primary = null;
        fallback = null;
    }

    private static void dispose(BmsLegacySkinTransformer? transformer)
    {
        if (transformer?.Skin is IDisposable disposable)
            disposable.Dispose();
    }

    public event Action? SourceChanged;
}
