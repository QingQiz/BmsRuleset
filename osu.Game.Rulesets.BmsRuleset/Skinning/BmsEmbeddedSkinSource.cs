using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public sealed class BmsEmbeddedSkinSource : ISkinSource, IDisposable
{
    private ISkinSource? parent;
    private BmsLegacySkinTransformer? primary;
    private BmsLegacySkinTransformer? fallback;

    public event Action? SourceChanged;

    public static BmsEmbeddedSkinKind GetEmbeddedSkinKind(IEnumerable<ISkin> sources)
    {
        foreach (var source in sources)
        {
            var skin = source is ISkinTransformer transformer ? transformer.Skin : source;

            switch (skin)
            {
                case LegacyBeatmapSkin:
                case BmsEmbeddedSkin:
                    continue;

                case ArgonSkin:
                case TrianglesSkin:
                    return BmsEmbeddedSkinKind.Modern;

                case DefaultLegacySkin:
                case RetroSkin:
                    return BmsEmbeddedSkinKind.Legacy;

                case Skin:
                    return BmsEmbeddedSkinKind.Legacy;
            }
        }

        return BmsEmbeddedSkinKind.Legacy;
    }

    public void SetSources(ISkinSource parent, BmsLegacySkinTransformer? primary, BmsLegacySkinTransformer? fallback)
    {
        DisposeEmbeddedSkins();

        this.parent = parent;
        this.primary = primary;
        this.fallback = fallback;
        SourceChanged?.Invoke();
    }

    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        parent?.GetDrawableComponent(lookup) ?? primary?.GetDrawableComponent(lookup) ?? fallback?.GetDrawableComponent(lookup);

    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        parent?.GetTexture(componentName, wrapModeS, wrapModeT) ?? primary?.GetTexture(componentName, wrapModeS, wrapModeT) ?? fallback?.GetTexture(componentName, wrapModeS, wrapModeT);

    public ISample? GetSample(ISampleInfo sampleInfo) =>
        parent?.GetSample(sampleInfo) ?? primary?.GetSample(sampleInfo) ?? fallback?.GetSample(sampleInfo);

    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => parent?.GetConfig<TLookup, TValue>(lookup) ?? primary?.GetConfig<TLookup, TValue>(lookup) ?? fallback?.GetConfig<TLookup, TValue>(lookup);

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

    public void DisposeEmbeddedSkins()
    {
        dispose(primary);
        dispose(fallback);

        primary = null;
        fallback = null;
    }

    public void Dispose() => DisposeEmbeddedSkins();

    private static void dispose(BmsLegacySkinTransformer? transformer)
    {
        if (transformer?.Skin is IDisposable disposable)
            disposable.Dispose();
    }
}
