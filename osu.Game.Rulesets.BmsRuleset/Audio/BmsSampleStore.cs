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

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     Resolves and caches every chart-declared sample (key-sounds, landmine, BGM) up-front,
///     during the gameplay loading phase, so the gameplay hot path never touches disk or decodes
///     audio: callers only ask for an already-resolved <see cref="ISample" /> and obtain a
///     lightweight channel from it via <see cref="ISample.GetChannel" />.
/// </summary>
/// <remarks>
///     Samples are resolved only from the original BMS chart directory on the filesystem
///     (via <see cref="BmsFileResourceStore" />), using the path stored in
///     <c>BeatmapInfo.Metadata.Source</c>. Charts imported in external-audio mode (BMS text
///     in Realm, audio on disk) resolve normally; charts whose audio lives only in Realm
///     (no filesystem <c>Source</c> path) will not resolve samples.
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
    ///     store used for sample resolution.  <c>null</c> when <c>basePath</c> is not a real
    ///     directory.
    /// </summary>
    internal ITrackStore? TrackStore { get; private set; }

    private readonly IReadOnlyList<string> samplePaths;
    private readonly string? basePath;
    private readonly double rate;

    /// <summary>
    ///     Declared sample path (the first <see cref="BmsSampleInfo.LookupNames" /> entry) →
    ///     resolved sample. A cached <c>null</c> means "resolved, but absent" so it is never
    ///     re-probed.
    /// </summary>
    private readonly Dictionary<string, ISample?> cache = new(StringComparer.OrdinalIgnoreCase);

    // Owned by this store (its stretched ISamples are served from cache). Disposed in Dispose.
    private BmsSampleStretcher? stretcher;

    private ISampleStore? fileSampleStore;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    /// <param name="samplePaths">
    ///     Every distinct chart-declared sample filename to pre-resolve (typically
    ///     <c>BmsBeatmap.SampleDefinitions.Values</c>).
    /// </param>
    /// <param name="basePath">
    ///     The chart directory on the real filesystem (from <c>BeatmapInfo.Metadata.Source</c>).
    ///     Audio files are resolved from here via <see cref="BmsFileResourceStore" />. When
    ///     null or non-existent, no samples resolve.
    /// </param>
    /// <param name="rate">
    ///     When not <c>1.0</c>, every resolved sample is pitch-preserving time-stretched by this
    ///     factor during <c>load()</c> (via <see cref="BmsSampleStretcher" />) and the cache entry
    ///     replaced, so runtime playback is rate-adjusted with zero per-playback overhead.
    /// </param>
    public BmsSampleStore(IEnumerable<string> samplePaths, string? basePath = null, double rate = 1.0)
    {
        this.samplePaths = samplePaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.basePath = basePath;
        this.rate = rate;
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        // File sample store is owned by this component.  Dispose it to free native BASS resources.
        if (fileSampleStore is IDisposable disposable)
            disposable.Dispose();

        // Stretcher owns the stretched ISamples now served from cache; free their native BASS
        // resources too. Must happen before cache.Clear() since the cache entries reference them.
        stretcher?.Dispose();
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

        // Filesystem (external-audio import mode). byte[] allocation (if any) happens here,
        // during preload on the async background thread.  Playback via ISample.GetChannel()
        // reuses the already-loaded native sample handle and allocates zero managed memory.
        if (fileSampleStore != null)
        {
            foreach (var lookup in sampleInfo.LookupNames)
            {
                var sample = fileSampleStore.Get(lookup);

                if (sample != null)
                    return cache[key] = sample;
            }
        }

        return cache[key] = null;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        // Runs on the async load thread (the "click play → loading screen" phase), so the
        // disk read + decode of every sample happens off the gameplay hot path.
        // Only create the filesystem-backed store when basePath is a real directory.
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

        // Pre-stretch pass: when rate != 1, replace each resolved cache entry with a pitch-preserving
        // stretch. Done after (not during) resolution so the stretcher decodes the source bytes once
        // per sample and the runtime Get() path stays untouched (callers receive stretched samples
        // transparently). Absent samples (cached null) are skipped, preserving their "absent" state.
        if (!(Math.Abs(rate - 1.0) > 0.001))
            return;

        var sourceBytes = buildSourceByteStore();

        if (sourceBytes != null)
        {
            stretcher = new BmsSampleStretcher(audioManager);

            foreach (var path in samplePaths)
            {
                var key = new BmsSampleInfo(path).LookupNames.FirstOrDefault();

                if (string.IsNullOrEmpty(key))
                    continue;

                if (!cache.TryGetValue(key, out var existing) || existing == null)
                    continue;

                var stretched = stretcher.Stretch(sourceBytes, path, rate);

                if (stretched != null)
                    cache[key] = stretched;
            }
        }
    }

    /// <summary>
    ///     Builds a byte resource store over the chart directory so the stretcher can decode
    ///     source audio. Returns null when <c>basePath</c> is not a real directory (no samples
    ///     to pre-stretch).
    /// </summary>
    private IResourceStore<byte[]>? buildSourceByteStore()
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return null;

        var fileResources = new ResourceStore<byte[]>(new BmsFileResourceStore(basePath));
        fileResources.AddExtension("wav");
        fileResources.AddExtension("mp3");
        fileResources.AddExtension("ogg");
        return fileResources;
    }
}
