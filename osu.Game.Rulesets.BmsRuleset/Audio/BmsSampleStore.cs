using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
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
///     Samples are resolved through a two-tier fallback:
///     <list type="number">
///       <item><description>
///         <b>Filesystem</b> — when <c>basePath</c> is provided, audio files are
///         loaded directly from the original BMS chart directory via <see cref="BmsFileResourceStore" />,
///         bypassing Realm file storage entirely. This is used when charts were imported in
///         external-audio mode (only BMS text files stored in Realm).
///       </description></item>
///       <item><description>
///         <b>LegacyBeatmapSkin (Realm)</b> — falls back to the beatmap skin's Realm-backed
///         resource store, matching the original fully-imported behaviour.
///       </description></item>
///     </list>
/// </remarks>
public partial class BmsSampleStore : Component
{

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

    /// <summary>
    ///     An <see cref="ITrackStore" /> for BGM seek-back tracks, backed by the same filesystem
    ///     store used for sample resolution.  <c>null</c> when <c>basePath</c> is not set
    ///     (Realm-imported mode).
    /// </summary>
    internal ITrackStore? TrackStore { get; private set; }

    private readonly IReadOnlyList<string> samplePaths;
    private readonly string? basePath;

    /// <summary>
    ///     Declared sample path (the first <see cref="BmsSampleInfo.LookupNames" /> entry) →
    ///     resolved sample. A cached <c>null</c> means "resolved, but absent" so it is never
    ///     re-probed.
    /// </summary>
    private readonly Dictionary<string, ISample?> cache = new(StringComparer.OrdinalIgnoreCase);

    private ISampleStore? fileSampleStore;

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    /// <param name="samplePaths">
    ///     Every distinct chart-declared sample filename to pre-resolve (typically
    ///     <c>BmsBeatmap.SampleDefinitions.Values</c>).
    /// </param>
    /// <param name="basePath">
    ///     When non-null, audio files are resolved from this directory on the real filesystem
    ///     instead of through the Realm-backed <see cref="LegacyBeatmapSkin" />.  Pass the
    ///     chart directory path stored in <c>BeatmapInfo.Metadata.Source</c>.
    /// </param>
    public BmsSampleStore(IEnumerable<string> samplePaths, string? basePath = null)
    {
        this.samplePaths = samplePaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.basePath = basePath;
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        // File sample store is owned by this component (unlike skin samples which are owned
        // by the skin itself).  Dispose it to free native BASS resources.
        if (fileSampleStore is IDisposable disposable)
            disposable.Dispose();

        fileSampleStore = null;
        TrackStore = null;
        cache.Clear();
        base.Dispose(isDisposing);
    }

    #endregion

    /// <summary>
    ///     Returns the resolved sample for the given chart-declared path, resolving and caching it
    ///     on first request. Returns <c>null</c> when the sample is absent.
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

        // Tier 1 — filesystem (external-audio import mode).
        // byte[] allocation (if any) happens here, during preload on the async background
        // thread.  Playback via ISample.GetChannel() reuses the already-loaded native sample
        // handle and allocates zero managed memory.
        if (fileSampleStore != null)
        {
            foreach (var lookup in sampleInfo.LookupNames)
            {
                var sample = fileSampleStore.Get(lookup);

                if (sample != null)
                    return cache[key] = sample;
            }
        }

        // Tier 2 — LegacyBeatmapSkin (Realm-backed import mode).
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

    private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
    {
        LegacyBeatmapSkin beatmapSkin => beatmapSkin,
        SkinTransformer transformer => transformer.Skin as LegacyBeatmapSkin,
        _ => null,
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        // Runs on the async load thread (the "click play → loading screen" phase), so the
        // disk read + decode of every sample happens off the gameplay hot path.
        //
        // Only create filesystem-backed stores when basePath is a real directory.
        // Old imports have basePath = "BMS" (a sentinel, not a real path) and store audio
        // in Realm — those must fall through to Tier 2 (LegacyBeatmapSkin).
        if (!string.IsNullOrEmpty(basePath) && Directory.Exists(basePath))
        {
            var fileResources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
            fileResources.AddExtension("wav");
            fileResources.AddExtension("mp3");
            fileResources.AddExtension("ogg");

            fileSampleStore = audioManager.GetSampleStore(fileResources);
            TrackStore = audioManager.GetTrackStore(fileResources);
        }

        foreach (var path in samplePaths)
            Get(path);
    }
}
