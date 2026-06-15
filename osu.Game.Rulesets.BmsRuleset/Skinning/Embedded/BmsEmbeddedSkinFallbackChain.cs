using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;

/// <summary>
/// Owns the ruleset-embedded fallback skins used after the parent osu! skin chain misses.
/// </summary>
public sealed class BmsEmbeddedSkinFallbackChain : IDisposable
{
    private readonly BmsLegacySkinTransformer primary;
    private readonly BmsLegacySkinTransformer? fallback;

    internal BmsEmbeddedSkinFallbackChain(BmsLegacySkinTransformer primary, BmsLegacySkinTransformer? fallback)
    {
        this.primary = primary;
        this.fallback = fallback;
    }

    internal IEnumerable<ISkin> AllSources
    {
        get
        {
            yield return primary;

            if (fallback != null)
                yield return fallback;
        }
    }

    internal Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        primary.GetDrawableComponent(lookup) ?? fallback?.GetDrawableComponent(lookup);

    internal BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup)
    {
        var primaryFactory = ((IBmsGameplaySkinDrawableSource)primary).GetDrawableFactory(lookup);
        var fallbackFactory = ((IBmsGameplaySkinDrawableSource?)fallback)?.GetDrawableFactory(lookup);

        return primaryFactory ?? fallbackFactory;
    }

    internal Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        primary.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? fallback?.GetTexture(componentName, wrapModeS, wrapModeT);

    internal ISample? GetSample(ISampleInfo sampleInfo) =>
        primary.GetSample(sampleInfo) ?? fallback?.GetSample(sampleInfo);

    internal IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => primary.GetConfig<TLookup, TValue>(lookup)
           ?? fallback?.GetConfig<TLookup, TValue>(lookup);

    internal ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (lookupFunction(primary))
            return primary;

        if (fallback != null && lookupFunction(fallback))
            return fallback;

        return null;
    }

    public void Dispose()
    {
        dispose(primary);
        dispose(fallback);
    }

    private static void dispose(BmsLegacySkinTransformer? transformer)
    {
        if (transformer?.Skin is IDisposable disposable)
            disposable.Dispose();
    }
}
