using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

internal sealed class BmsGameplaySkinCache : IDisposable
{
    private readonly ISkinSource skin;
    private readonly Dictionary<BmsDrawableFactoryCacheKey, BmsResolvedDrawableFactory?> drawableFactories = new();
    private readonly Dictionary<BmsDrawableFactoryCacheKey, BmsResolvedNoteMetrics> noteMetrics = new();
    private readonly Dictionary<BmsLongNoteBodyCacheKey, BmsLongNoteBodyTextureSet?> longNoteBodyTextureSets = new();

    public BmsGameplaySkinCache(ISkinSource skin)
    {
        this.skin = skin;
        skin.SourceChanged += clear;
    }

    public BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup)
    {
        var key = BmsDrawableFactoryCacheKey.From(lookup);

        if (!drawableFactories.TryGetValue(key, out var factory))
        {
            factory = ResolveDrawableFactory(skin, lookup);
            drawableFactories[key] = factory;
        }

        return factory;
    }

    public float GetNoteHeight(BmsSkinComponentLookup lookup, float drawWidth)
    {
        var key = BmsDrawableFactoryCacheKey.From(lookup);

        if (!noteMetrics.TryGetValue(key, out var metrics))
        {
            metrics = ResolveNoteMetrics(skin, lookup);
            noteMetrics[key] = metrics;
        }

        return metrics.HeightFor(drawWidth);
    }

    public BmsLongNoteBodyTextureSet? GetLongNoteBodyTextureSet(BmsSkinComponentLookup lookup, IRenderer renderer)
    {
        var key = BmsLongNoteBodyCacheKey.From(lookup, renderer);

        if (!longNoteBodyTextureSets.TryGetValue(key, out var textureSet))
        {
            textureSet = BmsLongNoteBodySource.Resolve(skin, lookup, renderer);
            longNoteBodyTextureSets[key] = textureSet;
        }

        return textureSet;
    }

    public void Dispose() => skin.SourceChanged -= clear;

    internal static BmsResolvedDrawableFactory ResolveDrawableFactory(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        if (skin is IBmsGameplaySkinDrawableSource source
            && source.GetDrawableFactory(lookup) is { } sourceFactory)
        {
            return sourceFactory;
        }

        foreach (var provider in skin.AllSources)
        {
            if (provider is IBmsGameplaySkinDrawableSource factorySource
                && factorySource.GetDrawableFactory(lookup) is { } factory)
            {
                return factory;
            }
        }

        return new BmsResolvedDrawableFactory(() => skin.GetDrawableComponent(lookup));
    }

    internal static BmsResolvedNoteMetrics ResolveNoteMetrics(ISkinSource skin, BmsSkinComponentLookup lookup)
    {
        var configuredReferenceWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;

        foreach (var name in BmsLegacyTextureResolver.NoteImageCandidates(skin, lookup).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            var texture = skin
                .GetTextures(name!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .FirstOrDefault(t => t.DisplayWidth > 0 && t.DisplayHeight > 0);

            if (texture != null)
                return new BmsResolvedNoteMetrics(configuredReferenceWidth, texture.DisplayHeight / texture.DisplayWidth);
        }

        return new BmsResolvedNoteMetrics(configuredReferenceWidth, null);
    }

    private void clear()
    {
        drawableFactories.Clear();
        noteMetrics.Clear();
        longNoteBodyTextureSets.Clear();
    }

    private readonly record struct BmsDrawableFactoryCacheKey(
        BmsSkinComponents Component,
        BmsLayoutVariant LayoutVariant,
        int? Column,
        bool IsLongNote)
    {
        public static BmsDrawableFactoryCacheKey From(BmsSkinComponentLookup lookup) =>
            new(lookup.Component, lookup.LayoutVariant, lookup.ColumnIndex, lookup.IsLongNote);
    }

    private readonly record struct BmsLongNoteBodyCacheKey(
        BmsSkinComponents Component,
        BmsLayoutVariant LayoutVariant,
        int? Column,
        bool IsLongNote,
        IRenderer Renderer)
    {
        public static BmsLongNoteBodyCacheKey From(BmsSkinComponentLookup lookup, IRenderer renderer) =>
            new(lookup.Component, lookup.LayoutVariant, lookup.ColumnIndex, lookup.IsLongNote, renderer);
    }
}

internal interface IBmsGameplaySkinDrawableSource
{
    BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup);
}

internal sealed class BmsResolvedDrawableFactory(Func<Drawable?> create)
{
    public Drawable? Create() => create();
}

internal readonly record struct BmsResolvedNoteMetrics(float? ConfiguredReferenceWidth, float? TextureHeightAspect)
{
    public float HeightFor(float drawWidth)
    {
        var referenceWidth = ConfiguredReferenceWidth ?? drawWidth;

        if (TextureHeightAspect != null)
            return Math.Max(1, TextureHeightAspect.Value * referenceWidth);

        return ConfiguredReferenceWidth != null
            ? Math.Max(1, referenceWidth)
            : BmsNoteSizing.DEFAULT_NOTE_HEIGHT;
    }
}
