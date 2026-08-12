using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    private readonly Func<CancellationToken, System.Threading.Tasks.Task>? beforeAssetLoad;
    private readonly BmsPcmVoiceMixer mixer;
    private readonly BmsPlaybackClockMapper clockMapper;
    private readonly Dictionary<ushort, string> resolvedResources = [];
    private readonly Dictionary<ushort, BmsPcmAssetLease> leases = [];
    private readonly Dictionary<ushort, double> lifetimeEnds = [];
    private readonly Dictionary<ushort, double> sampleLengths = [];
    private readonly Dictionary<ushort, PendingPlay> pendingPlays = [];
    private readonly List<PendingLivePlay> livePlays = [];

    private BmsAudioResourceStore? resourceStore;
    private BmsPcmAssetCache? assetCache;
    private SampleLifetime[] lifetimes = [];
    private int nextLifetimeIndex;
    private int epoch;
    private float lastMasterGain;
    private bool playbackBlocked;
    private bool disposed;

    internal bool IsInitialised => assetCache != null;

    internal long PreloadUnderflows { get; private set; }

    internal double MaxSampleLengthMilliseconds => sampleLengths.Count == 0
        ? leases.Count == 0 ? 0 : leases.Values.Max(lease => getOriginalLength(lease.Asset))
        : sampleLengths.Values.Max();

    internal BmsPcmPlaybackController(
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        double rate,
        IEnumerable<BmsSampleUsage>? sampleUsages,
        IBindable<double> aggregateVolume,
        Func<double> currentTime,
        BmsPcmVoiceMixer mixer,
        Func<CancellationToken, System.Threading.Tasks.Task>? beforeAssetLoad = null)
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
        this.beforeAssetLoad = beforeAssetLoad;
        clockMapper = new BmsPlaybackClockMapper(rate);
        lastMasterGain = sanitiseAggregateVolume();
    }

    internal void Initialise(CancellationToken cancellationToken, double chartTime, bool waitForInitialAssets = true)
    {
        if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
            return;

        resourceStore = new BmsAudioResourceStore(basePath, cancellationToken);
        resolveResources();
        assetCache = new BmsPcmAssetCache(async (identity, token) =>
        {
            if (beforeAssetLoad != null)
                await beforeAssetLoad(token).WaitAsync(token).ConfigureAwait(false);

            return (byte[]?)await resourceStore.GetAsync(identity, token).ConfigureAwait(false);
        }, rate);
        clockMapper.Rebase(chartTime, mixer.RenderedFrames);
        mixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, mixer.RenderedFrames, epoch, lastMasterGain);

        lifetimes = createLifetimes();
        while (nextLifetimeIndex < lifetimes.Length && lifetimes[nextLifetimeIndex].StartTime <= chartTime)
        {
            ensureLease(lifetimes[nextLifetimeIndex].SampleKey);
            nextLifetimeIndex++;
        }

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

    internal double GetSampleLength(ushort sampleKey) =>
        leases.TryGetValue(sampleKey, out var lease)
            ? rememberSampleLength(sampleKey, lease.Asset)
            : sampleLengths.GetValueOrDefault(sampleKey);

    internal bool IsSampleReady(ushort sampleKey) =>
        leases.TryGetValue(sampleKey, out var lease)
        && lease.Asset.State is BmsPcmAssetState.Ready or BmsPcmAssetState.Complete;

    internal bool IsSampleReady(ushort sampleKey, double offset)
    {
        if (!leases.TryGetValue(sampleKey, out var lease)
            || lease.Asset.State is not (BmsPcmAssetState.Ready or BmsPcmAssetState.Complete))
            return false;

        if (lease.Asset.IsComplete)
            return true;

        var sourceOffset = clockMapper.MapSourceOffset(offset);
        // A voice seeking past the published frontier would be released by the real-time mixer
        // before the decoder can catch up, so retain the same startup margin used at offset zero.
        var requiredFrames = sourceOffset + BmsFixedRatePcmProcessor.DEFAULT_CHUNK_FRAMES * 2L;
        return lease.Asset.PublishedFrameCount >= requiredFrames;
    }

    internal void PrepareSample(ushort sampleKey) => ensureLease(sampleKey);

    internal void QueueLivePlay(ushort sampleKey, int volume) => QueuePlay(sampleKey, volume, 0);

    internal void QueuePlay(ushort sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);
        livePlays.Add(new PendingLivePlay(sampleKey, volume, Math.Max(0, offset)));
    }

    internal void SubmitLivePlayBatch()
    {
        if (livePlays.Count == 0)
            return;

        if (playbackBlocked)
        {
            livePlays.Clear();
            return;
        }

        var targetFrame = mixer.RenderedFrames;
        var plays = new List<BmsVoicePlay>();

        foreach (var pending in livePlays)
        {
            if (!tryGetReadyAsset(pending.SampleKey, out var asset))
            {
                PreloadUnderflows++;
                continue;
            }

            var sourceOffset = clockMapper.MapSourceOffset(pending.Offset);
            if (asset.TotalFrameCount >= 0 && sourceOffset >= asset.TotalFrameCount)
                continue;

            plays.Add(createPlay(asset, pending.SampleKey, pending.Volume, targetFrame, sourceOffset));
        }

        livePlays.Clear();

        if (plays.Count > 0)
            mixer.SubmitPlayBatch(plays.ToArray());
    }

    internal void Play(ushort sampleKey, int volume, double offset)
    {
        if (playbackBlocked || !HasSampleDefinition(sampleKey))
            return;

        ensureLease(sampleKey);

        if (!tryGetReadyAsset(sampleKey, out var asset))
        {
            pendingPlays[sampleKey] = new PendingPlay(volume, Math.Max(0, offset), currentTime());
            return;
        }

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

    internal void SetPlaybackBlocked(bool blocked)
    {
        if (blocked == playbackBlocked)
            return;

        playbackBlocked = blocked;
        livePlays.Clear();

        if (!blocked)
            clockMapper.Rebase(currentTime(), mixer.RenderedFrames);

        mixer.SubmitControl(blocked ? BmsVoiceCommandType.Pause : BmsVoiceCommandType.Resume, mixer.RenderedFrames, epoch);
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
        livePlays.Clear();
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

        foreach (var (sampleKey, pending) in pendingPlays.ToArray())
        {
            if (!tryGetReadyAsset(sampleKey, out var asset))
                continue;

            pendingPlays.Remove(sampleKey);
            var elapsed = Math.Max(0, chartTime - pending.RequestedAt);
            submitSingle(asset, sampleKey, pending.Volume, pending.Offset + elapsed);
        }

        foreach (var (sampleKey, lease) in leases.ToArray())
        {
            rememberSampleLength(sampleKey, lease.Asset);

            if (!lifetimeEnds.TryGetValue(sampleKey, out var lastTriggerTime) || !lease.Asset.IsComplete)
                continue;

            if (chartTime <= lastTriggerTime + getOriginalLength(lease.Asset))
                continue;

            leases.Remove(sampleKey);
            lease.Dispose();
        }

        assetCache?.EvictUnused();
    }

    internal BmsPcmAssetCacheDiagnostics GetCacheDiagnostics() => assetCache?.GetDiagnostics() ?? default;

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;

        foreach (var lease in leases.Values)
            lease.Dispose();

        leases.Clear();
        pendingPlays.Clear();
        livePlays.Clear();
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

    private BmsVoicePlay createPlay(BmsPcmAsset asset, ushort sampleKey, int volume, long targetFrame, long sourceOffset) =>
        new(
            asset,
            new BmsTerminationDomain(sampleKey),
            targetFrame,
            sanitiseVolume(volume),
            sourceOffset,
            epoch);

    private bool tryGetReadyAsset(ushort sampleKey, out BmsPcmAsset asset)
    {
        if (leases.TryGetValue(sampleKey, out var lease)
            && lease.Asset.State is BmsPcmAssetState.Ready or BmsPcmAssetState.Complete)
        {
            asset = lease.Asset;
            return true;
        }

        asset = null!;
        return false;
    }

    private void ensureLease(ushort sampleKey)
    {
        if (leases.ContainsKey(sampleKey) || assetCache == null || !resolvedResources.TryGetValue(sampleKey, out var identity))
            return;

        leases[sampleKey] = assetCache.Acquire(identity);
    }

    private void resolveResources()
    {
        if (resourceStore == null)
            return;

        foreach (var (sampleKey, definition) in sampleDefinitions)
        {
            foreach (var lookup in new BmsSampleInfo(definition).LookupNames)
            {
                if (tryResolveWithExtensions(lookup, out var identity))
                {
                    resolvedResources[sampleKey] = identity;
                    break;
                }
            }
        }
    }

    private bool tryResolveWithExtensions(string lookup, out string identity)
    {
        if (resourceStore!.TryResolve(lookup, out identity))
            return true;

        var stem = Path.ChangeExtension(lookup, null);
        foreach (var extension in BmsAudioResourceStore.Extensions)
        {
            if (resourceStore.TryResolve($"{stem}.{extension}", out identity))
                return true;
        }

        identity = null!;
        return false;
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
            lifetimeEnds[group.Key] = group.Max(usage => usage.LatestTriggerTime);

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
        nextLifetimeIndex = 0;

        while (nextLifetimeIndex < lifetimes.Length && lifetimes[nextLifetimeIndex].StartTime <= chartTime)
        {
            var sampleKey = lifetimes[nextLifetimeIndex].SampleKey;
            var knownLength = sampleLengths.GetValueOrDefault(sampleKey);

            // An unknown duration may belong to a long BGM that still spans the seek target.
            if (knownLength <= 0 || lifetimeEnds[sampleKey] + knownLength >= chartTime)
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
