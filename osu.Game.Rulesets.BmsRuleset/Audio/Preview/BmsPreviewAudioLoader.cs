using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.Audio.Resources;
using osu.Game.Rulesets.BmsRuleset.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.IO.Resources;

namespace osu.Game.Rulesets.BmsRuleset.Audio.Preview;

internal sealed class BmsPreviewAudioLoader : IDisposable
{
    private readonly BmsAudioResourceStore audioResourceStore;
    private readonly ITrackStore trackStore;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task<Track?>? loadTask;
    private readonly ConcurrentDictionary<Task<Track?>, byte> eventTrackLoads = new();
    private readonly object disposalLock = new();

    private Track? loadedTrack;
    private bool resultConsumed;
    private bool dedicatedLoadFinished;
    private bool storesDisposed;
    private volatile bool disposed;

    public bool IsPending => loadTask != null && !resultConsumed;

    internal static bool HasImmediateCandidate(string basePath, string? previewFile)
    {
        using var fileStore = new BmsFileResourceStore(basePath);

        return getDedicatedPreviewCandidates(previewFile).Any(candidate => hasCandidate(fileStore, candidate));
    }

    private BmsPreviewAudioLoader(string basePath, AudioManager audioManager, bool loadDedicatedPreview, string? previewFile)
    {
        audioResourceStore = new BmsAudioResourceStore(basePath, cancellation.Token);
        var fileResources = new ResourceStore<byte[]>(audioResourceStore);
        BmsAudioFormatSupport.AddExtensions(fileResources);
        trackStore = audioManager.GetTrackStore(fileResources);

        if (loadDedicatedPreview)
            loadTask = Task.Run(() => loadPreviewTrack(previewFile, cancellation.Token), cancellation.Token);
        else
            dedicatedLoadFinished = true;
    }

    internal static BmsPreviewAudioLoader ForEventPreview(string basePath, AudioManager audioManager) =>
        new(basePath, audioManager, false, null);

    internal static BmsPreviewAudioLoader ForDedicatedPreview(string basePath, string? previewFile, AudioManager audioManager) =>
        new(basePath, audioManager, true, previewFile);

    public bool TryConsume(out Track? track, out Exception? error)
    {
        track = null;
        error = null;

        if (loadTask == null || resultConsumed || !loadTask.IsCompleted)
            return false;

        resultConsumed = true;

        try
        {
            loadedTrack = loadTask.GetAwaiter().GetResult();
            track = loadedTrack;
        }
        catch (Exception exception)
        {
            error = exception;
        }

        dedicatedLoadFinished = true;

        return true;
    }

    public Task<Track?> LoadEventTrackAsync(string samplePath)
    {
        var task = loadEventTrack(samplePath, cancellation.Token);
        eventTrackLoads.TryAdd(task, 0);
        _ = task.ContinueWith(_ => disposeCompletedEventTrack(task), TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    public void MarkEventTrackConsumed(Task<Track?> task) => eventTrackLoads.TryRemove(task, out _);

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

        if (loadTask is { } task && !resultConsumed)
        {
            resultConsumed = true;
            _ = task.ContinueWith(completedTask =>
            {
                if (completedTask.IsCompletedSuccessfully)
                    completedTask.Result?.Dispose();
                else
                    _ = completedTask.Exception;

                dedicatedLoadFinished = true;
                disposeStoresIfReady();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        else
            loadedTrack?.Dispose();

        foreach (var eventTask in eventTrackLoads.Keys)
        {
            if (eventTask.IsCompleted)
                disposeCompletedEventTrack(eventTask);
            else
                _ = eventTask.ContinueWith(_ => disposeCompletedEventTrack(eventTask), TaskContinuationOptions.ExecuteSynchronously);
        }

        disposeStoresIfReady();
    }

    private async Task<Track?> loadPreviewTrack(string? previewFile, CancellationToken cancellationToken)
    {
        foreach (var candidate in getDedicatedPreviewCandidates(previewFile))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var lookup in new BmsSampleInfo(candidate).LookupNames)
            {
                var track = await trackStore.GetAsync(lookup, cancellationToken).ConfigureAwait(false);
                if (track != null && await track.SeekAsync(0).ConfigureAwait(false) && track.IsLoaded)
                    return track;

                track?.Dispose();
            }
        }

        return null;
    }

    private async Task<Track?> loadEventTrack(string samplePath, CancellationToken cancellationToken)
    {
        foreach (var lookup in new BmsSampleInfo(samplePath).LookupNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var track = await trackStore.GetAsync(lookup, cancellationToken).ConfigureAwait(false);
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

        foreach (var extension in BmsAudioFormatSupport.Extensions)
            yield return $"preview.{extension}";
    }

    private static bool hasCandidate(BmsFileResourceStore fileStore, string candidate)
    {
        foreach (var lookup in new BmsSampleInfo(candidate).LookupNames)
        {
            if (fileStore.TryResolve(lookup, out _))
                return true;

            var stem = Path.ChangeExtension(lookup, null);

            foreach (var extension in BmsAudioFormatSupport.Extensions)
            {
                if (fileStore.TryResolve($"{stem}.{extension}", out _))
                    return true;
            }
        }

        return false;
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
            if (storesDisposed || !disposed || !dedicatedLoadFinished || !eventTrackLoads.IsEmpty)
                return;

            storesDisposed = true;
        }

        trackStore.Dispose();
        audioResourceStore.Dispose();
        cancellation.Dispose();
    }
}
