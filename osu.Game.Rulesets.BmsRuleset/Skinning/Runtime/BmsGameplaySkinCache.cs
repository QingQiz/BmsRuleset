using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Rendering;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

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

    #region Disposal

    public void Dispose() => skin.SourceChanged -= clear;

    #endregion

    public BmsResolvedDrawableFactory? GetDrawableFactory(BmsSkinComponentLookup lookup)
    {
        var key = BmsDrawableFactoryCacheKey.From(lookup);

        if (!drawableFactories.TryGetValue(key, out var factory))
        {
            factory = BmsGameplaySkinDrawableResolver.Resolve(skin, lookup);
            drawableFactories[key] = factory;
        }

        return factory;
    }

    public float GetNoteHeight(BmsSkinComponentLookup lookup, float drawWidth)
    {
        var key = BmsDrawableFactoryCacheKey.From(lookup);

        if (!noteMetrics.TryGetValue(key, out var metrics))
        {
            metrics = BmsGameplaySkinMetricsResolver.ResolveNoteMetrics(skin, lookup);
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

    public void WarmLongNoteTextures(BmsBeatmap beatmap, IRenderer renderer)
    {
        foreach (var column in beatmap.HitObjects.OfType<BmsLongNote>()
                     .Select(h => h.Column)
                     .Where(c => c >= 0 && c < beatmap.TotalColumns)
                     .Distinct())
        {
            GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteHead, beatmap.LayoutVariant, column));
            GetLongNoteBodyTextureSet(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, beatmap.LayoutVariant, column), renderer);
            GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, beatmap.LayoutVariant, column));
        }
    }

    private void clear()
    {
        drawableFactories.Clear();
        noteMetrics.Clear();
        longNoteBodyTextureSets.Clear();
    }
}
