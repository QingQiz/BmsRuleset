using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

internal sealed class BmsWorkingBeatmapCache
{
    private readonly IStorageResourceProvider resources;
    private readonly Dictionary<Guid, WeakReference<BmsWorkingBeatmap>> bmsWrapperCache = new();

    // One weakly-cached texture store per external chart directory. Live wrappers keep their
    // store alive, while abandoned external directories don't accumulate for the cache lifetime.
    private readonly Dictionary<string, WeakReference<LargeTextureStore>> externalTextureStores = new();
    private readonly object externalTextureStoreLock = new();

    public BmsWorkingBeatmapCache(WorkingBeatmapCache inner)
    {
        resources = inner;
        inner.OnInvalidated += onInvalidated;
    }

    public WorkingBeatmap Wrap(WorkingBeatmap working)
    {
        lock (bmsWrapperCache)
        {
            if (bmsWrapperCache.TryGetValue(working.BeatmapInfo.ID, out var weak))
            {
                if (weak.TryGetTarget(out var cached))
                    return cached;

                // Dead weak reference: drop the tombstone before rebuilding.
                bmsWrapperCache.Remove(working.BeatmapInfo.ID);
            }
        }

        // External-audio charts keep their images in Metadata.Source on disk rather than realm
        // storage; give the wrapper a store sandboxed to that directory. Normal imports have no
        // such directory and fall back to inner.GetBackground() (realm).
        TextureStore? externalStore = null;

        if (!string.IsNullOrWhiteSpace(working.Metadata.Source) && Directory.Exists(working.Metadata.Source))
            externalStore = getOrCreateExternalTextureStore(working.Metadata.Source);

        var wrapper = new BmsWorkingBeatmap(working, resources.AudioManager!, externalStore);

        lock (bmsWrapperCache)
            bmsWrapperCache[working.BeatmapInfo.ID] = new WeakReference<BmsWorkingBeatmap>(wrapper);

        return wrapper;
    }

    private TextureStore getOrCreateExternalTextureStore(string basePath)
    {
        basePath = Path.GetFullPath(basePath);

        lock (externalTextureStoreLock)
        {
            if (externalTextureStores.TryGetValue(basePath, out var weak))
            {
                if (weak.TryGetTarget(out var cached))
                    return cached;

                externalTextureStores.Remove(basePath);
            }

            pruneDeadExternalTextureStores();

            var store = new LargeTextureStore(
                resources.Renderer,
                resources.CreateTextureLoaderStore(new BmsFileResourceStore(basePath)));

            externalTextureStores[basePath] = new WeakReference<LargeTextureStore>(store);
            return store;
        }
    }

    private void pruneDeadExternalTextureStores()
    {
        List<string>? deadPaths = null;

        foreach (var pair in externalTextureStores)
        {
            if (pair.Value.TryGetTarget(out _))
                continue;

            deadPaths ??= [];
            deadPaths.Add(pair.Key);
        }

        if (deadPaths == null)
            return;

        foreach (var path in deadPaths)
            externalTextureStores.Remove(path);
    }

    private void onInvalidated(WorkingBeatmap working)
    {
        lock (bmsWrapperCache)
            bmsWrapperCache.Remove(working.BeatmapInfo.ID);
    }
}
