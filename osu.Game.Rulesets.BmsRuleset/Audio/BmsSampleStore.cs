using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Resolves and caches every chart-declared sample (key-sounds, landmine, BGM) up-front,
///     during the gameplay loading phase, so the gameplay hot path never touches disk or decodes
///     audio: callers only ask for an already-resolved <see cref="ISample" /> and obtain a
///     lightweight channel from it via <see cref="ISample.GetChannel" />.
/// </summary>
/// <remarks>
///     Samples are resolved through the beatmap skin's own (long-lived) sample store rather than a
///     store owned by this component. That store is created and freed by the skin itself, well
///     after gameplay tears down, which avoids freeing native BASS samples while channels created
///     from them are still playing (a use-after-free / access violation). The chart's resources —
///     not the user skin — are the source of truth, matching the original key-sound behaviour.
/// </remarks>
public partial class BmsSampleStore : Component
{
    private readonly IReadOnlyList<string> samplePaths;

    /// <summary>
    ///     Declared sample path (the first <see cref="BmsSampleInfo.LookupNames" /> entry) →
    ///     resolved sample. A cached <c>null</c> means "resolved, but absent from the beatmap
    ///     resources" so it is never re-probed.
    /// </summary>
    private readonly Dictionary<string, ISample?> cache = new(StringComparer.OrdinalIgnoreCase);

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    /// <param name="samplePaths">
    ///     Every distinct chart-declared sample filename to pre-resolve (typically
    ///     <c>BmsBeatmap.SampleDefinitions.Values</c>).
    /// </param>
    public BmsSampleStore(IEnumerable<string> samplePaths)
    {
        this.samplePaths = samplePaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Runs on the async load thread (the "click play → loading screen" phase), so the
        // disk read + decode of every sample happens off the gameplay hot path.
        foreach (var path in samplePaths)
            Get(path);
    }

    /// <summary>
    ///     Returns the resolved sample for the given chart-declared path, resolving and caching it
    ///     on first request. Returns <c>null</c> when the sample is absent from the beatmap
    ///     resources.
    /// </summary>
    public ISample? Get(string? path) => string.IsNullOrEmpty(path) ? null : Get(new BmsSampleInfo(path));

    /// <inheritdoc cref="Get(string)" />
    public ISample? Get(ISampleInfo sampleInfo)
    {
        var key = sampleInfo.LookupNames.FirstOrDefault();

        if (string.IsNullOrEmpty(key))
            return null;

        if (cache.TryGetValue(key, out var cached))
            return cached;

        var beatmapSkins = skin.AllSources
            .Select(extractBeatmapSkin)
            .Where(s => s != null)
            .ToArray();

        // Skin not ready yet (no beatmap sources): do NOT cache, so a later request can retry.
        if (beatmapSkins.Length == 0)
            return null;

        ISample? resolved = null;

        foreach (var beatmapSkin in beatmapSkins)
        {
            resolved = beatmapSkin!.GetSample(sampleInfo);

            if (resolved != null)
                break;
        }

        return cache[key] = resolved;
    }

    /// <summary>
    ///     The length (in milliseconds) of the longest resolved sample. Used to bound how far back
    ///     a seek needs to look for samples that may still be sounding at the seek target. Returns
    ///     <c>0</c> until samples have finished decoding.
    /// </summary>
    public double MaxSampleLengthMilliseconds
    {
        get
        {
            double max = 0;

            foreach (var sample in cache.Values)
            {
                if (sample != null && sample.Length > max)
                    max = sample.Length;
            }

            return max;
        }
    }

    private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
    {
        LegacyBeatmapSkin beatmapSkin => beatmapSkin,
        SkinTransformer transformer => transformer.Skin as LegacyBeatmapSkin,
        _ => null,
    };

    protected override void Dispose(bool isDisposing)
    {
        // The samples are owned by the beatmap skin, not this component, so they are intentionally
        // not freed here; clearing the cache only drops our references to them.
        cache.Clear();
        base.Dispose(isDisposing);
    }
}
