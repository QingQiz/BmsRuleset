using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsPreviewAudioLoader : IDisposable
{
    private readonly AudioManager audioManager;
    private readonly BmsAudioResourceStore audioResourceStore;
    private readonly AudioMixer? mixer;
    private readonly BmsPcmVoiceMixer? pcmMixer;
    private readonly BmsBassMixerBridge? pcmBridge;
    private readonly BmsPcmAssetCache? pcmCache;
    private readonly double rate;
    private readonly Func<CancellationToken, Task>? beforeTrackLoad;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<Task<Track?>, byte> eventTrackLoads = new();
    private readonly object disposalLock = new();

    private bool storesDisposed;
    private volatile bool disposed;
    private float pcmMasterGain = -1;

    internal bool UsesDedicatedMixer => mixer != null;

    internal static IReadOnlyList<string> GetExistingDedicatedPreviewCandidates(string basePath, string? previewFile)
    {
        using var fileStore = new BmsFileResourceStore(basePath);
        List<string> candidates = [];
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in getDedicatedPreviewCandidates(previewFile))
        {
            foreach (var resolvedPath in resolveCandidatePaths(fileStore, candidate))
            {
                var relativePath = Path.GetRelativePath(basePath, resolvedPath);

                if (seenPaths.Add(relativePath))
                    candidates.Add(relativePath);
            }
        }

        return candidates;
    }

    internal BmsPreviewAudioLoader(
        string basePath,
        AudioManager audioManager,
        Func<CancellationToken, Task>? beforeTrackLoad = null,
        double rate = 1)
    {
        this.audioManager = audioManager;
        this.beforeTrackLoad = beforeTrackLoad;
        this.rate = rate;
        audioResourceStore = new BmsAudioResourceStore(basePath, cancellation.Token);

        BmsPcmMixerPatcher.InstallOnce();

        if (BmsPcmMixerPatcher.IsInstalled)
        {
            mixer = audioManager.CreateAudioMixer(BmsPcmMixerPatcher.MIXER_IDENTIFIER);
            pcmMixer = new BmsPcmVoiceMixer();
            pcmBridge = new BmsBassMixerBridge(mixer, pcmMixer);
            pcmCache = new BmsPcmAssetCache(async (identity, token) =>
                (byte[]?)await audioResourceStore.GetAsync(identity, token).ConfigureAwait(false), rate);
            pcmBridge.EnsureAttached();
        }
    }

    public Task<Track?> LoadTrackAsync(string samplePath)
    {
        var task = loadPcmEventTrack(samplePath, cancellation.Token);
        eventTrackLoads.TryAdd(task, 0);
        _ = task.ContinueWith(_ => disposeCompletedEventTrack(task), TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    public void MarkEventTrackConsumed(Task<Track?> task) => eventTrackLoads.TryRemove(task, out _);

    public void UpdatePcmBridge()
    {
        pcmBridge?.EnsureAttached();

        if (pcmMixer == null)
            return;

        var aggregateVolume = audioManager.AggregateVolume.Value;
        var gain = double.IsFinite(aggregateVolume) ? (float)Math.Max(0, aggregateVolume) : 0;

        if (Math.Abs(gain - pcmMasterGain) <= 0.000001f)
            return;

        pcmMasterGain = gain;
        pcmMixer.SubmitControl(BmsVoiceCommandType.SetMasterGain, pcmMixer.RenderedFrames, 0, gain);
    }

    public void DiscardEventTrack(Task<Track?> task)
    {
        if (!eventTrackLoads.ContainsKey(task))
            return;

        if (task.IsCompleted)
            discardCompletedEventTrack(task);
        else
            _ = task.ContinueWith(_ => discardCompletedEventTrack(task), TaskContinuationOptions.ExecuteSynchronously);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cancellation.Cancel();

        foreach (var eventTask in eventTrackLoads.Keys)
        {
            if (eventTask.IsCompleted)
                disposeCompletedEventTrack(eventTask);
            else
                _ = eventTask.ContinueWith(_ => disposeCompletedEventTrack(eventTask), TaskContinuationOptions.ExecuteSynchronously);
        }

        disposeStoresIfReady();
    }

    private async Task<Track?> loadPcmEventTrack(string samplePath, CancellationToken cancellationToken)
    {
        if (beforeTrackLoad != null)
            await beforeTrackLoad(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);

        if (pcmCache == null || pcmMixer == null || !tryResolvePcmIdentity(samplePath, out var identity))
            return null;

        var lease = pcmCache.Acquire(identity);

        try
        {
            await lease.Ready.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (lease.Asset.State is not (BmsPcmAssetState.Ready or BmsPcmAssetState.Complete))
            {
                lease.Dispose();
                return null;
            }

            return new BmsPcmPreviewVoiceTrack(lease, pcmMixer, rate, samplePath);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private bool tryResolvePcmIdentity(string samplePath, out string identity)
    {
        foreach (var lookup in new BmsSampleInfo(samplePath).LookupNames)
        {
            if (audioResourceStore.TryResolve(lookup, out identity))
                return true;

            var stem = Path.ChangeExtension(lookup, null);
            foreach (var extension in BmsAudioResourceStore.Extensions)
            {
                if (audioResourceStore.TryResolve($"{stem}.{extension}", out identity))
                    return true;
            }
        }

        identity = null!;
        return false;
    }

    private static IEnumerable<string> getDedicatedPreviewCandidates(string? previewFile)
    {
        if (!string.IsNullOrWhiteSpace(previewFile))
            yield return previewFile;

        foreach (var extension in BmsAudioResourceStore.Extensions)
            yield return $"preview.{extension}";
    }

    private static IEnumerable<string> resolveCandidatePaths(BmsFileResourceStore fileStore, string candidate)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lookup in new BmsSampleInfo(candidate).LookupNames)
        {
            if (fileStore.TryResolve(lookup, out var resolvedPath) && seenPaths.Add(resolvedPath))
                yield return resolvedPath;

            var stem = Path.ChangeExtension(lookup, null);

            foreach (var extension in BmsAudioResourceStore.Extensions)
            {
                if (fileStore.TryResolve($"{stem}.{extension}", out resolvedPath) && seenPaths.Add(resolvedPath))
                    yield return resolvedPath;
            }
        }
    }

    private void disposeCompletedEventTrack(Task<Track?> task)
    {
        if (!disposed || !eventTrackLoads.TryRemove(task, out _))
            return;

        disposeTrackResult(task);
        disposeStoresIfReady();
    }

    private void discardCompletedEventTrack(Task<Track?> task)
    {
        if (!eventTrackLoads.TryRemove(task, out _))
            return;

        disposeTrackResult(task);
        disposeStoresIfReady();
    }

    private static void disposeTrackResult(Task<Track?> task)
    {
        if (task.IsCompletedSuccessfully)
            task.Result?.Dispose();
        else
            _ = task.Exception;
    }

    private void disposeStoresIfReady()
    {
        lock (disposalLock)
        {
            if (storesDisposed || !disposed || !eventTrackLoads.IsEmpty)
                return;

            storesDisposed = true;
        }

        pcmBridge?.Dispose();
        pcmCache?.Dispose();
        mixer?.Dispose();
        audioResourceStore.Dispose();
        cancellation.Dispose();
    }
}
