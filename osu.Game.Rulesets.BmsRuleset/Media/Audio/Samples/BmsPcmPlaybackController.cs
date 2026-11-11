using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

internal sealed class BmsPcmPlaybackController : IDisposable
{
    private const double preload_time = 10_000;
    private static readonly TimeSpan initial_ready_timeout = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyDictionary<int, BmsSampleDefinition> sampleDefinitions;
    private readonly IReadOnlyList<BmsSampleUsage>? sampleUsages;
    private readonly string? basePath;
    private readonly double rate;
    private readonly IBindable<double> aggregateVolume;
    private readonly Func<double> currentTime;
    private readonly BmsPcmVoiceMixer mixer;
    private readonly BmsPlaybackClockMapper clockMapper;
    private readonly Dictionary<int, string> resolvedResources = [];
    private readonly Dictionary<int, BmsPcmAssetLease> leases = [];
    private readonly Dictionary<int, double> lifetimeEnds = [];
    private readonly HashSet<int> resumableSamples = [];
    private readonly Dictionary<int, double> sampleLengths = [];
    private readonly Dictionary<int, PendingPlay> pendingPlays = [];
    private readonly List<PendingLivePlay> livePlays = [];
    private readonly Dictionary<int, int> livePlayIndices = [];

    private BmsAudioResourceStore? resourceStore;
    private BmsPcmAssetCache? assetCache;
    private SampleLifetime[] lifetimes = [];
    private int nextLifetimeIndex;
    private double nextLeaseMaintenanceTime;
    private bool leaseMaintenancePending = true;
    private int epoch;
    private float lastMasterGain;
    private bool playbackBlocked;
    private bool voicesPaused;
    private bool disposed;

    internal bool IsInitialised => assetCache != null;

    internal IEnumerable<int> PreparedSampleKeys => leases.Keys;

    internal Task WaitForPreparedSamplesAsync(CancellationToken cancellationToken, IReadOnlyDictionary<int, double>? offsets = null)
    {
        var prepared = leases.Select(pair => (pair.Value, Offset: sampleDefinitions[pair.Key].StartOffset + (offsets?.GetValueOrDefault(pair.Key) ?? 0),
            NeedLength: offsets?.ContainsKey(pair.Key) == true)).ToArray();
        return waitForPreparedSamples(prepared, cancellationToken);
    }

    private async Task waitForPreparedSamples((BmsPcmAssetLease Lease, double Offset, bool NeedLength)[] prepared, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(initial_ready_timeout);
        try
        {
            await Task.WhenAll(prepared.Select(sample => sample.Lease.Ready)).WaitAsync(timeout.Token).ConfigureAwait(false);
            while (prepared.Any(sample => sample.Lease.Asset.State is not (BmsPcmAssetState.Failed or BmsPcmAssetState.Disposed)
                                          && (!isReadyAtOffset(sample.Lease.Asset, sample.Offset)
                                              || (sample.NeedLength && getOriginalLength(sample.Lease.Asset) <= 0 && !sample.Lease.Asset.IsComplete))))
                await Task.Delay(10, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while preparing BMS sample seek buffers.");
        }
    }

    internal double MaxSampleLengthMilliseconds => Math.Max(
        sampleLengths.Values.DefaultIfEmpty(0).Max(),
        leases.Select(pair => rememberSampleLength(pair.Key, pair.Value.Asset)).DefaultIfEmpty(0).Max());

    internal BmsPcmPlaybackController(
        IReadOnlyDictionary<int, BmsSampleDefinition> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<BmsSampleUsage>? sampleUsages,
        IBindable<double> aggregateVolume,
        Func<double> currentTime,
        BmsPcmVoiceMixer mixer)
    {
        this.sampleDefinitions = sampleDefinitions;
        this.basePath = basePath;
        this.rate = rate;
        this.sampleUsages = sampleUsages?.ToArray();
        this.aggregateVolume = aggregateVolume;
        this.currentTime = currentTime;
        this.mixer = mixer;
        clockMapper = new BmsPlaybackClockMapper(rate);
        lastMasterGain = sanitiseAggregateVolume();
    }

    internal void Initialise(CancellationToken cancellationToken, double chartTime, bool waitForInitialAssets = true,
                             IReadOnlyDictionary<int, double>? knownLengths = null)
    {
        if (knownLengths != null)
        {
            foreach (var (key, length) in knownLengths)
                sampleLengths[key] = length;
        }

        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return;

        resourceStore = new BmsAudioResourceStore(basePath, cancellationToken);
        resolveResources();
        assetCache = new BmsPcmAssetCache((identity, token) => resourceStore.OpenPcmAsync(identity, rate, token));
        clockMapper.Rebase(chartTime, mixer.RenderedFrames);
        mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, mixer.RenderedFrames, epoch, lastMasterGain);

        lifetimes = createLifetimes();
        rebuildLifetimeSchedule(chartTime);

        if (sampleUsages == null)
        {
            foreach (var sampleKey in resolvedResources.Keys)
                ensureLease(sampleKey);
        }

        if (!waitForInitialAssets)
            return;

        var initialReadyTasks = leases.Values.Select(lease => lease.Ready).ToArray();
        if (initialReadyTasks.Length == 0)
            return;

        try
        {
            System.Threading.Tasks.Task.WhenAll(initialReadyTasks)
                .WaitAsync(initial_ready_timeout, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            BmsLogger.LogAudioFailure("Timed out while preparing the initial BMS PCM startup buffers.");
        }
    }

    internal bool HasSampleDefinition(int sampleKey) =>
        resolvedResources.ContainsKey(sampleKey)
        && (!leases.TryGetValue(sampleKey, out var lease) || lease.Asset.State != BmsPcmAssetState.Failed);

    internal double GetSampleLength(int sampleKey)
    {
        if (leases.TryGetValue(sampleKey, out var lease))
            rememberSampleLength(sampleKey, lease.Asset);
        return sampleLengths.GetValueOrDefault(sampleKey);
    }

    internal bool IsSampleReady(int sampleKey, double offset = 0) => tryGetReadyAsset(sampleKey, offset, out _);

    private bool isReadyAtOffset(BmsPcmAsset asset, double offset)
    {
        if (asset.IsComplete)
            return true;

        if (asset.State != BmsPcmAssetState.Ready)
            return false;

        var sourceOffset = clockMapper.MapSourceOffset(offset);
        // A voice seeking past the published frontier would be released by the real-time mixer
        // before the decoder can catch up, so retain the same startup margin used at offset zero.
        var requiredFrames = sourceOffset + BmsFixedRatePcmProcessor.DEFAULT_CHUNK_FRAMES * 2L;
        return asset.PublishedFrameCount >= requiredFrames;
    }

    internal void PrepareSample(int sampleKey) => ensureLease(sampleKey);

    internal void QueueLivePlay(int sampleKey, int volume) => QueuePlay(sampleKey, volume, 0);

    internal void QueuePlay(int sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);
        var play = new PendingLivePlay(sampleKey, volume, Math.Max(0, offset));
        // All live events in a game frame share one mixer target. Keep only the last
        // trigger per sample definition before allocating the command batch.
        if (livePlayIndices.TryGetValue(sampleKey, out var index))
            livePlays[index] = play;
        else
        {
            livePlayIndices.Add(sampleKey, livePlays.Count);
            livePlays.Add(play);
        }
    }

    internal void SubmitLivePlayBatch()
    {
        if (livePlays.Count == 0)
            return;

        if (playbackBlocked)
        {
            clearLivePlays();
            return;
        }

        var targetFrame = mixer.RenderedFrames;
        // The live index already bounds this batch, so avoid repeated list growth and copying.
        var plays = new List<BmsVoicePlay>(livePlays.Count);

        foreach (var pending in livePlays)
        {
            if (!tryGetReadyAsset(pending.SampleKey, pending.Offset, out var asset))
            {
                // Decoder contention must not turn a live key/BGM event into permanent silence.
                // Keep only the newest trigger for a definition, just as the mixer retriggers it.
                pendingPlays[pending.SampleKey] = new PendingPlay(pending.Volume, pending.Offset, currentTime());
                continue;
            }

            pendingPlays.Remove(pending.SampleKey);
            var play = createPlay(asset, pending.SampleKey, pending.Volume, targetFrame, pending.Offset);
            if (asset.TotalFrameCount >= 0 && play.SourceOffsetFrame >= asset.TotalFrameCount)
                continue;

            plays.Add(play);
        }

        clearLivePlays();

        if (plays.Count > 0)
        {
            // The mixer copies the span synchronously into its command queue, so the list buffer
            // can be submitted directly.
            mixer.SubmitPlayBatch(CollectionsMarshal.AsSpan(plays));
        }
    }

    internal void Play(int sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);

        if (!tryGetReadyAsset(sampleKey, offset, out var asset))
        {
            pendingPlays[sampleKey] = new PendingPlay(volume, Math.Max(0, offset), currentTime());
            return;
        }

        pendingPlays.Remove(sampleKey);
        submitSingle(asset, sampleKey, volume, offset);
    }

    internal bool CanSchedule(int sampleKey) =>
        !playbackBlocked && IsSampleReady(sampleKey);

    internal void SchedulePlay(int sampleKey, int volume, double targetTime)
    {
        if (playbackBlocked || !tryGetReadyAsset(sampleKey, 0, out var asset))
        {
            Play(sampleKey, volume, 0);
            return;
        }

        var targetFrame = clockMapper.Map(targetTime, mixer.RenderedFrames);
        mixer.SubmitPlayBatch([createPlay(asset, sampleKey, volume, targetFrame, 0)]);
    }

    internal void SetPlaybackBlocked(bool blocked, bool keepExistingVoices = false)
    {
        if (blocked != playbackBlocked)
        {
            playbackBlocked = blocked;
            clearLivePlays();
            if (!blocked)
                clockMapper.Rebase(currentTime(), mixer.RenderedFrames);
        }

        var pauseVoices = blocked && !keepExistingVoices;
        if (pauseVoices == voicesPaused)
            return;

        voicesPaused = pauseVoices;
        mixer.SubmitControl(pauseVoices ? BmsVoiceCommandType.Pause : BmsVoiceCommandType.Resume, mixer.RenderedFrames, epoch);
    }

    internal void ResumeAll()
    {
        if (!playbackBlocked)
        {
            clockMapper.Rebase(currentTime(), mixer.RenderedFrames);
            mixer.SubmitControl(BmsVoiceCommandType.Resume, mixer.RenderedFrames, epoch);
        }
    }

    internal void StopAll()
    {
        clearLivePlays();
        pendingPlays.Clear();
        epoch++;
        mixer.SubmitControl(BmsVoiceCommandType.ReplaceEpoch, mixer.RenderedFrames, epoch);
        var chartTime = currentTime();
        clockMapper.Rebase(chartTime, mixer.RenderedFrames);
        rebuildLifetimeSchedule(chartTime);
    }

    internal void Update(double chartTime)
    {
        var masterGain = sanitiseAggregateVolume();
        if (Math.Abs(masterGain - lastMasterGain) > 0.000001f)
        {
            lastMasterGain = masterGain;
            mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, mixer.RenderedFrames, epoch, masterGain);
        }

        while (nextLifetimeIndex < lifetimes.Length && lifetimes[nextLifetimeIndex].StartTime <= chartTime)
        {
            ensureLease(lifetimes[nextLifetimeIndex].SampleKey);
            nextLifetimeIndex++;
        }

        // Dictionary removal preserves active enumerators on .NET 8; copying these collections
        // every frame creates garbage even when no pending play or lease changes.
        foreach (var (sampleKey, pending) in pendingPlays)
        {
            // The mixer discards Play commands while paused. Keep decoder completions pending
            // until resume, or StopAll clears them when a seek replaces the playback epoch.
            if (playbackBlocked)
                break;

            var elapsed = Math.Max(0, chartTime - pending.RequestedAt);
            var offset = pending.Offset + elapsed;
            if (!tryGetReadyAsset(sampleKey, offset, out var asset))
                continue;

            pendingPlays.Remove(sampleKey);
            submitSingle(asset, sampleKey, pending.Volume, offset);
        }

        maintainLeases(chartTime);
        assetCache?.EvictUnused();
    }

    private void maintainLeases(double chartTime)
    {
        // A completed asset's duration stays fixed while its lease pins it in the cache.
        // Between acquisitions and the next expiry, rescanning those assets cannot change anything.
        if (!leaseMaintenancePending && chartTime <= nextLeaseMaintenanceTime)
            return;

        leaseMaintenancePending = false;
        nextLeaseMaintenanceTime = double.PositiveInfinity;

        foreach (var (sampleKey, lease) in leases)
        {
            var complete = lease.Asset.IsComplete;
            var length = rememberSampleLength(sampleKey, lease.Asset);

            if (!complete)
            {
                // Decoder progress can publish a length or complete before the following frame.
                leaseMaintenancePending = true;
                continue;
            }

            if (!lifetimeEnds.TryGetValue(sampleKey, out var lastTriggerTime))
                continue;

            var endTime = lastTriggerTime + length;
            if (chartTime <= endTime)
            {
                nextLeaseMaintenanceTime = Math.Min(nextLeaseMaintenanceTime, endTime);
                continue;
            }

            leases.Remove(sampleKey);
            lease.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var lease in leases.Values)
            lease.Dispose();

        leases.Clear();
        pendingPlays.Clear();
        clearLivePlays();
        assetCache?.Dispose();
        assetCache = null;
        resourceStore?.Dispose();
        resourceStore = null;
    }

    private void submitSingle(BmsPcmAsset asset, int sampleKey, int volume, double offset)
    {
        var play = createPlay(asset, sampleKey, volume, mixer.RenderedFrames, offset);
        var totalFrames = asset.TotalFrameCount;

        if (totalFrames >= 0 && play.SourceOffsetFrame >= totalFrames)
            return;

        mixer.SubmitPlayBatch([play]);
    }

    private void clearLivePlays()
    {
        livePlays.Clear();
        livePlayIndices.Clear();
    }

    private BmsVoicePlay createPlay(BmsPcmAsset asset, int sampleKey, int volume, long targetFrame, double offset)
    {
        var definition = sampleDefinitions[sampleKey];
        return new BmsVoicePlay(asset, sampleKey, targetFrame, sanitiseVolume(volume),
            clockMapper.MapSourceOffset(definition.StartOffset + offset), epoch,
            definition.EndOffset is { } end ? clockMapper.MapSourceOffset(end) : null);
    }

    private bool tryGetReadyAsset(int sampleKey, double offset, [NotNullWhen(true)] out BmsPcmAsset? asset)
    {
        if (leases.TryGetValue(sampleKey, out var lease)
            && isReadyAtOffset(lease.Asset, sampleDefinitions[sampleKey].StartOffset + offset))
        {
            asset = lease.Asset;
            return true;
        }

        asset = null;
        return false;
    }

    private void ensureLease(int sampleKey)
    {
        if (leases.ContainsKey(sampleKey) || assetCache == null || !resolvedResources.TryGetValue(sampleKey, out var identity))
            return;

        leases[sampleKey] = assetCache.Acquire(identity);
        leaseMaintenancePending = true;
    }

    private void resolveResources()
    {
        if (resourceStore == null)
            return;

        var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (sampleKey, definition) in sampleDefinitions)
        {
            if (string.IsNullOrEmpty(definition.Path))
                continue;

            if (!paths.TryGetValue(definition.Path, out var identity))
                paths[definition.Path] = identity = resourceStore.ResolveSamplePath(definition.Path);
            if (identity != null)
                resolvedResources[sampleKey] = identity;
        }
    }

    private SampleLifetime[] createLifetimes()
    {
        if (sampleUsages == null)
            return [];

        var groups = sampleUsages
            .Where(usage => resolvedResources.ContainsKey(usage.SampleKey))
            .GroupBy(usage => usage.SampleKey)
            .ToArray();

        foreach (var group in groups)
        {
            lifetimeEnds[group.Key] = group.Max(usage => usage.LatestTriggerTime);

            if (group.Any(usage => usage.ResumeAfterSeek))
                resumableSamples.Add(group.Key);
        }

        return groups
            .Select(group => new SampleLifetime(group.Key, group.Min(usage => usage.EarliestTriggerTime) - preload_time))
            .OrderBy(lifetime => lifetime.StartTime)
            .ToArray();
    }

    private double getOriginalLength(BmsPcmAsset asset) =>
        asset.OriginalDurationMilliseconds
        ?? (asset.TotalFrameCount < 0 ? 0 : asset.TotalFrameCount * 1000d / asset.SampleRate * rate);

    private double rememberSampleLength(int sampleKey, BmsPcmAsset asset)
    {
        var sourceLength = getOriginalLength(asset);
        var length = sampleDefinitions[sampleKey].GetLength(sourceLength);
        if (sourceLength > 0 || asset.IsComplete)
            sampleLengths[sampleKey] = length;

        return length;
    }

    private void rebuildLifetimeSchedule(double chartTime)
    {
        leaseMaintenancePending = true;
        nextLifetimeIndex = 0;

        while (nextLifetimeIndex < lifetimes.Length && lifetimes[nextLifetimeIndex].StartTime <= chartTime)
        {
            var sampleKey = lifetimes[nextLifetimeIndex].SampleKey;
            var hasKnownLength = sampleLengths.TryGetValue(sampleKey, out var knownLength);
            var mayStillBePlaying = resumableSamples.Contains(sampleKey) && (!hasKnownLength || lifetimeEnds[sampleKey] + knownLength >= chartTime);

            // Only background samples need their duration resolved after a seek. Historical
            // keysounds must stay skipped even though their duration is not known yet.
            if (lifetimeEnds[sampleKey] >= chartTime || mayStillBePlaying)
                ensureLease(sampleKey);

            nextLifetimeIndex++;
        }
    }

    private float sanitiseAggregateVolume()
    {
        var value = aggregateVolume.Value;
        return double.IsFinite(value) ? (float)Math.Max(0, value) : 0;
    }

    private static float sanitiseVolume(int volume) => Math.Max(0, volume) / 100f;

    private readonly record struct SampleLifetime(int SampleKey, double StartTime);

    private readonly record struct PendingPlay(int Volume, double Offset, double RequestedAt);

    private readonly record struct PendingLivePlay(int SampleKey, int Volume, double Offset);
}
