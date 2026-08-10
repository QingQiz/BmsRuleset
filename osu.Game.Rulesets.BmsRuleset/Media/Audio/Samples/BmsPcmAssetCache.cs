using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

internal readonly record struct BmsPcmAssetKey(string ResourceIdentity, long RateKey, int PipelineVersion)
{
    internal static BmsPcmAssetKey Create(string resourceIdentity, double rate) =>
        new(resourceIdentity, (long)Math.Round(rate * 1_000_000), BmsFixedRatePcmProcessor.PIPELINE_VERSION);
}

internal readonly record struct BmsPcmAssetCacheDiagnostics(
    int LoadedAssets,
    int PreparingAssets,
    int FailedAssets,
    long ResidentPcmBytes,
    long PeakResidentPcmBytes,
    long EvictionCount);

internal sealed class BmsPcmAssetCache : IDisposable
{
    internal const long DEFAULT_SOFT_BUDGET = 512L * 1024 * 1024;
    internal const int DEFAULT_STARTUP_FRAMES = BmsFixedRatePcmProcessor.DEFAULT_CHUNK_FRAMES * 2;

    private readonly Func<string, CancellationToken, Task<byte[]?>> resourceLoader;
    private readonly double rate;
    private readonly long softBudget;
    private readonly int startupFrames;
    private readonly SemaphoreSlim processingSlots;
    private readonly CancellationTokenSource disposalCancellation = new();
    private readonly ConcurrentDictionary<BmsPcmAssetKey, CacheEntry> entries = new();
    private readonly object lifecycleLock = new();

    private long residentPcmBytes;
    private long peakResidentPcmBytes;
    private long evictionCount;
    private bool disposed;

    internal BmsPcmAssetCache(
        Func<string, CancellationToken, Task<byte[]?>> resourceLoader,
        double rate,
        int maximumConcurrentProcessors = 2,
        long softBudget = DEFAULT_SOFT_BUDGET,
        int startupFrames = DEFAULT_STARTUP_FRAMES)
    {
        ArgumentNullException.ThrowIfNull(resourceLoader);

        if (!double.IsFinite(rate) || rate < 0.05 || rate > 2)
            throw new ArgumentOutOfRangeException(nameof(rate));

        if (maximumConcurrentProcessors <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentProcessors));

        if (softBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(softBudget));

        if (startupFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(startupFrames));

        this.resourceLoader = resourceLoader;
        this.rate = rate;
        this.softBudget = softBudget;
        this.startupFrames = startupFrames;
        processingSlots = new SemaphoreSlim(maximumConcurrentProcessors, maximumConcurrentProcessors);
    }

    internal BmsPcmAssetLease Acquire(string resourceIdentity)
    {
        ArgumentException.ThrowIfNullOrEmpty(resourceIdentity);

        lock (lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var key = BmsPcmAssetKey.Create(resourceIdentity, rate);
            var entry = entries.GetOrAdd(key, createEntry);
            entry.AddReference();
            return new BmsPcmAssetLease(entry.Asset, entry.Ready, entry.Completion, () => release(entry));
        }
    }

    internal BmsPcmAssetCacheDiagnostics GetDiagnostics()
    {
        var loaded = 0;
        var preparing = 0;
        var failed = 0;

        foreach (var entry in entries.Values)
        {
            switch (entry.Asset.State)
            {
                case BmsPcmAssetState.Ready:
                case BmsPcmAssetState.Complete:
                    loaded++;
                    break;

                case BmsPcmAssetState.Preparing:
                    preparing++;
                    break;

                case BmsPcmAssetState.Failed:
                    failed++;
                    break;
            }
        }

        return new BmsPcmAssetCacheDiagnostics(
            loaded,
            preparing,
            failed,
            Interlocked.Read(ref residentPcmBytes),
            Interlocked.Read(ref peakResidentPcmBytes),
            Interlocked.Read(ref evictionCount));
    }

    internal void EvictUnused()
    {
        if (Interlocked.Read(ref residentPcmBytes) <= softBudget)
            return;

        var candidates = entries
            .Where(pair => pair.Value.ReferenceCount == 0 && pair.Value.Asset.IsComplete)
            .OrderBy(pair => pair.Value.LastReleasedTimestamp)
            .ToArray();

        foreach (var candidate in candidates)
        {
            if (Interlocked.Read(ref residentPcmBytes) <= softBudget)
                break;

            if (!entries.TryRemove(candidate.Key, out var removed) || removed.ReferenceCount != 0)
                continue;

            var bytes = removed.Asset.ResidentBytes;
            removed.Asset.DisposePublishedChunks();
            Interlocked.Add(ref residentPcmBytes, -bytes);
            Interlocked.Increment(ref evictionCount);
        }
    }

    public void Dispose()
    {
        CacheEntry[] processingEntries;

        lock (lifecycleLock)
        {
            if (disposed)
                return;

            disposed = true;
            processingEntries = entries.Values.ToArray();
        }

        disposalCancellation.Cancel();

        try
        {
            Task.WhenAll(processingEntries.Select(entry => entry.Completion)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var entry in entries.Values)
            entry.Asset.DisposePublishedChunks();

        entries.Clear();
        Interlocked.Exchange(ref residentPcmBytes, 0);
        processingSlots.Dispose();
        disposalCancellation.Dispose();
    }

    private CacheEntry createEntry(BmsPcmAssetKey key)
    {
        var asset = new BmsPcmAsset(BmsFixedRatePcmProcessor.OUTPUT_SAMPLE_RATE, BmsFixedRatePcmProcessor.OUTPUT_CHANNELS);
        var entry = new CacheEntry(asset);
        entry.Completion = Task.Run(() => processEntry(key, entry), CancellationToken.None);
        return entry;
    }

    private async Task processEntry(BmsPcmAssetKey key, CacheEntry entry)
    {
        var acquiredSlot = false;

        try
        {
            await processingSlots.WaitAsync(disposalCancellation.Token).ConfigureAwait(false);
            acquiredSlot = true;

            var data = await resourceLoader(key.ResourceIdentity, disposalCancellation.Token).ConfigureAwait(false);
            if (data == null || data.Length == 0)
                throw new InvalidOperationException($"BMS audio resource '{key.ResourceIdentity}' is unavailable.");

            using var processor = BmsFixedRatePcmProcessor.CreateFromMemory(data, rate);
            entry.Asset.SetOriginalDuration(processor.OriginalDurationMilliseconds);

            foreach (var chunk in processor.ProcessChunks(disposalCancellation.Token))
            {
                entry.Asset.Publish(chunk);
                var resident = Interlocked.Add(ref residentPcmBytes, (long)chunk.Samples.Length * sizeof(float));
                updatePeakResidentBytes(resident);

                if (entry.Asset.PublishedFrameCount >= startupFrames)
                {
                    entry.MarkReady();
                    processingSlots.Release();
                    acquiredSlot = false;
                    await Task.Yield();
                    await processingSlots.WaitAsync(disposalCancellation.Token).ConfigureAwait(false);
                    acquiredSlot = true;
                }
            }

            entry.Asset.Complete(entry.Asset.PublishedFrameCount);
            entry.MarkReady();
        }
        catch (OperationCanceledException) when (disposalCancellation.IsCancellationRequested)
        {
            entry.Asset.Fail();
            entry.MarkCancelled(disposalCancellation.Token);
        }
        catch (Exception exception)
        {
            entry.Asset.Fail();
            entry.MarkFailed(exception);
            BmsLogger.LogAudioFailure($"Failed to prepare BMS PCM resource '{key.ResourceIdentity}'.", exception);
        }
        finally
        {
            if (acquiredSlot)
                processingSlots.Release();
        }
    }

    private void release(CacheEntry entry)
    {
        entry.ReleaseReference();
        EvictUnused();
    }

    private void updatePeakResidentBytes(long resident)
    {
        while (true)
        {
            var peak = Interlocked.Read(ref peakResidentPcmBytes);
            if (resident <= peak || Interlocked.CompareExchange(ref peakResidentPcmBytes, resident, peak) == peak)
                return;
        }
    }

    private sealed class CacheEntry(BmsPcmAsset asset)
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int referenceCount;

        internal BmsPcmAsset Asset { get; } = asset;

        internal Task Ready => ready.Task;

        internal Task Completion { get; set; } = Task.CompletedTask;

        internal int ReferenceCount => Volatile.Read(ref referenceCount);

        internal long LastReleasedTimestamp { get; private set; }

        internal void AddReference() => Interlocked.Increment(ref referenceCount);

        internal void ReleaseReference()
        {
            if (Interlocked.Decrement(ref referenceCount) < 0)
                throw new InvalidOperationException("A PCM asset lease was released more than once.");

            LastReleasedTimestamp = Environment.TickCount64;
        }

        internal void MarkReady()
        {
            Asset.MarkReady();
            ready.TrySetResult();
        }

        internal void MarkCancelled(CancellationToken cancellationToken) => ready.TrySetResult();

        internal void MarkFailed(Exception exception) => ready.TrySetResult();
    }
}

internal sealed class BmsPcmAssetLease : IDisposable
{
    private Action? release;

    internal BmsPcmAsset Asset { get; }

    internal Task Ready { get; }

    internal Task Completion { get; }

    internal BmsPcmAssetLease(BmsPcmAsset asset, Task ready, Task completion, Action release)
    {
        Asset = asset;
        Ready = ready;
        Completion = completion;
        this.release = release;
    }

    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
