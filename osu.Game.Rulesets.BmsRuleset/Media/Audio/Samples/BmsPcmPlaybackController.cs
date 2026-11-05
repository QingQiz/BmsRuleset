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

    private readonly IReadOnlyDictionary<ushort, string> sampleDefinitions;
    private readonly IReadOnlyList<BmsSampleUsage>? sampleUsages;
    private readonly string? basePath;
    private readonly double rate;
    private readonly IBindable<double> aggregateVolume;
    private readonly Func<double> currentTime;
    private readonly BmsPcmVoiceMixer mixer;
    private readonly BmsPlaybackClockMapper clockMapper;
    private readonly Dictionary<ushort, string> resolvedResources = [];
    private readonly Dictionary<ushort, BmsPcmAssetLease> leases = [];
    private readonly Dictionary<ushort, double> lifetimeEnds = [];
    private readonly HashSet<ushort> resumableSamples = [];
    private readonly Dictionary<ushort, double> sampleLengths = [];
    private readonly Dictionary<ushort, PendingPlay> pendingPlays = [];
    private readonly List<PendingLivePlay> livePlays = [];
    private readonly Dictionary<ushort, int> livePlayIndices = [];

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

    internal IEnumerable<ushort> PreparedSampleKeys => leases.Keys;

    internal Task WaitForPreparedSamplesAsync(CancellationToken cancellationToken, IReadOnlyDictionary<ushort, double>? offsets = null)
    {
        var prepared = leases.Select(pair => (pair.Value, Offset: offsets?.GetValueOrDefault(pair.Key) ?? 0,
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
        leases.Values.Select(lease => getOriginalLength(lease.Asset)).DefaultIfEmpty(0).Max());

    internal BmsPcmPlaybackController(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<BmsSampleUsage>? sampleUsages,
        IBindable<double> aggregateVolume,
        Func<double> currentTime,
        BmsPcmVoiceMixer mixer)
    {
        this.sampleDefinitions = sampleDefinitions
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
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
                             IReadOnlyDictionary<ushort, double>? knownLengths = null)
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
        assetCache = new BmsPcmAssetCache(
            async (identity, token) => (byte[]?)await resourceStore.GetAsync(identity, token).ConfigureAwait(false),
            rate);
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

    internal bool HasSampleDefinition(ushort sampleKey) =>
        resolvedResources.ContainsKey(sampleKey)
        && (!leases.TryGetValue(sampleKey, out var lease) || lease.Asset.State != BmsPcmAssetState.Failed);

    internal double GetSampleLength(ushort sampleKey)
    {
        if (leases.TryGetValue(sampleKey, out var lease))
            rememberSampleLength(sampleKey, lease.Asset);
        return sampleLengths.GetValueOrDefault(sampleKey);
    }

    internal bool IsSampleReady(ushort sampleKey) =>
        leases.TryGetValue(sampleKey, out var lease)
        && lease.Asset.State is BmsPcmAssetState.Ready or BmsPcmAssetState.Complete;

    internal bool IsSampleReady(ushort sampleKey, double offset)
    {
        if (!leases.TryGetValue(sampleKey, out var lease)
            || lease.Asset.State is not (BmsPcmAssetState.Ready or BmsPcmAssetState.Complete))
            return false;

        return isReadyAtOffset(lease.Asset, offset);
    }

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

    internal void PrepareSample(ushort sampleKey) => ensureLease(sampleKey);

    internal void QueueLivePlay(ushort sampleKey, int volume) => QueuePlay(sampleKey, volume, 0);

    internal void QueuePlay(ushort sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);
        var play = new PendingLivePlay(sampleKey, volume, Math.Max(0, offset));
        // All live events in a game frame share one mixer target. The mixer already keeps only
        // the last trigger per definition; coalesce before allocating its command batch.
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
            if (!IsSampleReady(pending.SampleKey, pending.Offset) || !tryGetReadyAsset(pending.SampleKey, out var asset))
            {
                // Decoder contention must not turn a live key/BGM event into permanent silence.
                // Keep only the newest trigger for a definition, just as the mixer retriggers it.
                pendingPlays[pending.SampleKey] = new PendingPlay(pending.Volume, pending.Offset, currentTime());
                continue;
            }

            pendingPlays.Remove(pending.SampleKey);
            var sourceOffset = clockMapper.MapSourceOffset(pending.Offset);
            if (asset.TotalFrameCount >= 0 && sourceOffset >= asset.TotalFrameCount)
                continue;

            plays.Add(createPlay(asset, pending.SampleKey, pending.Volume, targetFrame, sourceOffset));
        }

        clearLivePlays();

        if (plays.Count > 0)
        {
            // The mixer copies the span synchronously into its command queue, so the list buffer
            // can be submitted directly.
            mixer.SubmitPlayBatch(CollectionsMarshal.AsSpan(plays));
        }
    }

    internal void Play(ushort sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);

        if (!IsSampleReady(sampleKey, offset) || !tryGetReadyAsset(sampleKey, out var asset))
        {
            pendingPlays[sampleKey] = new PendingPlay(volume, Math.Max(0, offset), currentTime());
            return;
        }

        pendingPlays.Remove(sampleKey);
        submitSingle(asset, sampleKey, volume, offset);
    }

    internal bool CanSchedule(ushort sampleKey) =>
        !playbackBlocked && tryGetReadyAsset(sampleKey, out _);

    internal void SchedulePlay(ushort sampleKey, int volume, double targetTime)
    {
        if (!CanSchedule(sampleKey) || !tryGetReadyAsset(sampleKey, out var asset))
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
            if (!IsSampleReady(sampleKey, offset) || !tryGetReadyAsset(sampleKey, out var asset))
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

    private void submitSingle(BmsPcmAsset asset, ushort sampleKey, int volume, double offset)
    {
        var sourceOffset = clockMapper.MapSourceOffset(offset);
        var totalFrames = asset.TotalFrameCount;

        if (totalFrames >= 0 && sourceOffset >= totalFrames)
            return;

        var targetFrame = mixer.RenderedFrames;
        mixer.SubmitPlayBatch([createPlay(asset, sampleKey, volume, targetFrame, sourceOffset)]);
    }

    private void clearLivePlays()
    {
        livePlays.Clear();
        livePlayIndices.Clear();
    }

    private BmsVoicePlay createPlay(BmsPcmAsset asset, ushort sampleKey, int volume, long targetFrame, long sourceOffset) =>
        new(
            asset,
            new BmsTerminationDomain(sampleKey),
            targetFrame,
            sanitiseVolume(volume),
            sourceOffset,
            epoch);

    private bool tryGetReadyAsset(ushort sampleKey, [NotNullWhen(true)] out BmsPcmAsset? asset)
    {
        if (leases.TryGetValue(sampleKey, out var lease)
            && lease.Asset.State is BmsPcmAssetState.Ready or BmsPcmAssetState.Complete)
        {
            asset = lease.Asset;
            return true;
        }

        asset = null;
        return false;
    }

    private void ensureLease(ushort sampleKey)
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

        foreach (var (sampleKey, definition) in sampleDefinitions)
        {
            if (resourceStore.ResolveSamplePath(definition) is { } identity)
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

    private double rememberSampleLength(ushort sampleKey, BmsPcmAsset asset)
    {
        var length = getOriginalLength(asset);
        if (length > 0)
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

    private readonly record struct SampleLifetime(ushort SampleKey, double StartTime);

    private readonly record struct PendingPlay(int Volume, double Offset, double RequestedAt);

    private readonly record struct PendingLivePlay(ushort SampleKey, int Volume, double Offset);
}
