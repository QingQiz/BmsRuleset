using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsPreviewAudioLoader : IDisposable
{
    private readonly AudioManager audioManager;
    private readonly BmsAudioResourceStore audioResourceStore;
    private readonly ITrackStore trackStore;
    private readonly AudioMixer? mixer;
    private readonly Func<CancellationToken, Task>? beforeTrackLoad;
    private readonly CancellationTokenSource cancellation = new();
    private readonly ConcurrentDictionary<Task<Track?>, byte> eventTrackLoads = new();
    private readonly object disposalLock = new();

    private bool storesDisposed;
    private volatile bool disposed;

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
        Func<CancellationToken, Task>? beforeTrackLoad = null)
    {
        this.audioManager = audioManager;
        this.beforeTrackLoad = beforeTrackLoad;
        BmsKeysoundMixerPatcher.InstallOnce();
        BmsKeysoundMixerPatcher.BindGlobalMixer(audioManager);

        if (BmsKeysoundMixerPatcher.EnableReverseStreamWorkaround)
            BmsTrackAudioPatcher.InstallOnce();

        if (BmsKeysoundMixerPatcher.IsInstalled)
            mixer = audioManager.CreateAudioMixer(BmsKeysoundMixerPatcher.PREVIEW_MIXER_IDENTIFIER);

        audioResourceStore = new BmsAudioResourceStore(basePath, cancellation.Token);
        var fileResources = new ResourceStore<byte[]>(audioResourceStore);
        BmsAudioResourceStore.AddExtensions(fileResources);
        trackStore = audioManager.GetTrackStore(fileResources, mixer);
    }

    public Task<Track?> LoadTrackAsync(string samplePath)
    {
        var task = loadEventTrack(samplePath, cancellation.Token);
        eventTrackLoads.TryAdd(task, 0);
        _ = task.ContinueWith(_ => disposeCompletedEventTrack(task), TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    public void MarkEventTrackConsumed(Task<Track?> task) => eventTrackLoads.TryRemove(task, out _);

    public void ApplyTailRamp(Track track, double offset) =>
        BmsKeysoundMixerPatcher.TryApplyTailRamp(mixer, track, offset);

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

    private async Task<Track?> loadEventTrack(string samplePath, CancellationToken cancellationToken)
    {
        if (beforeTrackLoad != null)
            await beforeTrackLoad(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);

        foreach (var lookup in new BmsSampleInfo(samplePath).LookupNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Track? track;
            using (BmsTrackAudioPatcher.EnterBmsTrackScope())
                track = await trackStore.GetAsync(lookup, cancellationToken).ConfigureAwait(false);

            if (track != null && await track.SeekAsync(0).ConfigureAwait(false) && track.IsLoaded)
                return track;

            track?.Dispose();
        }

        return null;
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

        trackStore.Dispose();
        mixer?.Dispose();
        BmsKeysoundMixerPatcher.UnbindGlobalMixer(audioManager);
        audioResourceStore.Dispose();
        cancellation.Dispose();
    }
}
