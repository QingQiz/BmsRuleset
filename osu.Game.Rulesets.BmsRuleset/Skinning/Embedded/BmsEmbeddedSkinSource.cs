using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;

/// <inheritdoc cref="ISkinSource" />
/// <summary>
/// The root ISkinSource used during BMS gameplay.
/// </summary>
/// <remarks>
/// Implements a three-tier fallback chain:
/// <list type="number">
///   <item><description>
///     <b>parent</b> — the full osu! skin source (user skin + beatmap skin + defaults),
///     supplied by the framework's skin manager.
///   </description></item>
///   <item><description>
///     <b>primary</b> — a BmsLegacySkinTransformer wrapping a
///     BmsEmbeddedSkin whose asset set matches the user's active skin style
///     (e.g. BmsEmbeddedSkinKind.LegacyModern when Argon is selected).
///   </description></item>
///   <item><description>
///     <b>fallback</b> — a BmsLegacySkinTransformer wrapping the
///     BmsEmbeddedSkinKind.LegacyOld embedded skin, used as the
///     last-resort when both the parent chain and the primary embedded skin fail.
///   </description></item>
/// </list>
/// </remarks>
public sealed class BmsEmbeddedSkinSource : ISkinSource, IDisposable, IBmsGameplaySkinDrawableSource
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

            if (embeddedFallbacks != null)
            {
                foreach (var source in embeddedFallbacks.AllSources)
                    yield return source;
            }
        }
    }

    private ISkinSource? parent;
    private BmsEmbeddedSkinFallbackChain? embeddedFallbacks;

    #region Disposal

    /// <inheritdoc/>
    public void Dispose() => DisposeEmbeddedSkins();

    #endregion

    /// <summary>
    /// Replaces the current skin sources with new ones, disposing any previously held
    /// embedded skins before taking ownership of the new ones.
    /// </summary>
    /// <param name="parent">The osu! skin source chain (must not be null).</param>
    /// <param name="embeddedFallbacks">The BMS embedded fallback chain to consult after <paramref name="parent"/> misses.</param>
    public void SetSources(ISkinSource parent, BmsEmbeddedSkinFallbackChain? embeddedFallbacks)
    {
        // A source switch may replace the user skin, embedded fallback kind, renderer-backed texture
        // stores, or all of the above. Invalidate raw LN body slices once per source switch rather
        // than from every active drawable's SourceChanged handler.
        BmsLongNoteBodySource.ClearCache();
        DisposeEmbeddedSkins();

        this.parent = parent;
        this.embeddedFallbacks = embeddedFallbacks;
        SourceChanged?.Invoke();
    }

    /// <inheritdoc />
    /// <summary>
    /// parent → fallbackChain(primary → fallback)
    /// when set to buildin skin, parent lookup will be null for all, auto fallback to fallback chain
    /// </summary>
    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        var drawable = lookup is
            BmsSkinComponentLookup or
            SkinComponentLookup<HitResult> or
            GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null }
            ? parent?.GetDrawableComponent(lookup)
              ?? embeddedFallbacks?.GetDrawableComponent(lookup)
            : parent?.GetDrawableComponent(lookup);

        return lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents }
            ? BmsDefaultHud.GetDrawableComponent(lookup)
            : drawable;
    }

    /// <summary>Looks up a texture, falling through parent → primary → fallback.</summary>
    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        parent?.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? embeddedFallbacks?.GetTexture(componentName, wrapModeS, wrapModeT);

    /// <summary>Looks up a sample, falling through parent → primary → fallback.</summary>
    public ISample? GetSample(ISampleInfo sampleInfo) =>
        parent?.GetSample(sampleInfo) ?? embeddedFallbacks?.GetSample(sampleInfo);

    /// <summary>
    /// Routes configuration lookups through the three-tier chain.
    /// </summary>
    /// <remarks>
    /// BmsSkinConfigurationLookup falls through parent → primary → fallback.
    /// All other lookups are forwarded to the parent only.
    /// </remarks>
    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => lookup is BmsSkinConfigurationLookup
            ? parent?.GetConfig<TLookup, TValue>(lookup)
              ?? embeddedFallbacks?.GetConfig<TLookup, TValue>(lookup)
            : parent?.GetConfig<TLookup, TValue>(lookup);

    /// <inheritdoc/>
    public ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (parent?.FindProvider(lookupFunction) is { } provider)
            return provider;

        return embeddedFallbacks?.FindProvider(lookupFunction);
    }

    /// <summary>
    /// Disposes and nulls the currently held embedded skin transformers
    /// without affecting the parent source.
    /// </summary>
    /// <remarks>
    /// Each transformer's underlying BmsEmbeddedSkin is disposed
    /// (via IDisposable) to release DLL store and texture/sample resources.
    /// </remarks>
    public void DisposeEmbeddedSkins()
    {
        embeddedFallbacks?.Dispose();
        embeddedFallbacks = null;
    }

    BmsResolvedDrawableFactory? IBmsGameplaySkinDrawableSource.GetDrawableFactory(BmsSkinComponentLookup lookup)
    {
        if (parent != null)
        {
            foreach (var source in parent.AllSources)
            {
                if (source is IBmsGameplaySkinDrawableSource factorySource
                    && factorySource.GetDrawableFactory(lookup) is { } factory)
                {
                    return factory;
                }
            }

            var embeddedFactory = embeddedFallbacks?.GetDrawableFactory(lookup);

            return new BmsResolvedDrawableFactory(() =>
                parent.GetDrawableComponent(lookup)
                ?? embeddedFactory?.Create());
        }

        return embeddedFallbacks?.GetDrawableFactory(lookup);
    }

    public event Action? SourceChanged;
}
