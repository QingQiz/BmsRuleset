using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Graphics.Rendering;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
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
    private readonly BmsLongNoteBodySource.BmsLongNoteBodyTextureCache rawLongNoteBodyTextures = new();
    private readonly object drawableLock = new();
    private readonly HashSet<BmsCachedSkinnableDrawable> cachedDrawables = [];
    private readonly Queue<BmsCachedSkinnableDrawable> pendingRefreshes = [];
    private volatile bool hasPendingRefreshes;

    public BmsGameplaySkinCache(ISkinSource skin)
    {
        this.skin = skin;
        skin.SourceChanged += clear;
    }

    #region Disposal

    public void Dispose()
    {
        skin.SourceChanged -= clear;
        clearResources();
        lock (drawableLock)
        {
            cachedDrawables.Clear();
            pendingRefreshes.Clear();
            hasPendingRefreshes = false;
        }
    }

    #endregion

    internal void Register(BmsCachedSkinnableDrawable drawable)
    {
        lock (drawableLock)
            cachedDrawables.Add(drawable);
    }

    internal void Unregister(BmsCachedSkinnableDrawable drawable)
    {
        lock (drawableLock)
            cachedDrawables.Remove(drawable);
    }

    internal void RefreshIdleSkins()
    {
        if (!hasPendingRefreshes)
            return;

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            BmsCachedSkinnableDrawable drawable;
            lock (drawableLock)
            {
                if (!pendingRefreshes.TryDequeue(out drawable!))
                {
                    hasPendingRefreshes = false;
                    return;
                }
                hasPendingRefreshes = pendingRefreshes.Count > 0;
            }
            drawable.PreparePendingSkin();
            // SkinReloadableDrawable otherwise waits until first use. A dense burst can then
            // rebuild thousands of already-preloaded trees in one update. Spread that work out.
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 0.5)
                return;
        }
    }

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
            textureSet = BmsLongNoteBodySource.Resolve(skin, lookup, renderer, rawLongNoteBodyTextures);
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
        clearResources();
        lock (drawableLock)
        {
            pendingRefreshes.Clear();
            foreach (var drawable in cachedDrawables)
                pendingRefreshes.Enqueue(drawable);
            hasPendingRefreshes = pendingRefreshes.Count > 0;
        }
    }

    private void clearResources()
    {
        drawableFactories.Clear();
        noteMetrics.Clear();
        longNoteBodyTextureSets.Clear();
        rawLongNoteBodyTextures.Clear();
    }
}
