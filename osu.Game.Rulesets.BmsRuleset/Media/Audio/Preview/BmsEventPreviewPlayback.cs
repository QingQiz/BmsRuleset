using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal enum BmsPreviewPlaybackStartState
{
    Waiting,
    Ready,
    InitialAudioUnavailable,
}

internal sealed class BmsEventPreviewPlayback : IDisposable
{
    private const double event_prefetch_time = 1_000;
    private const int event_prefetch_batch_size = 16;

    private readonly BmsPreviewTrack owner;
    private readonly List<BmsPreviewTimelineEntry> sortedEvents = [];
    private readonly BmsPreviewAudioLoader? audioLoader;
    private readonly bool deriveLengthFromTracks;
    private readonly bool retainLoadedTracks;
    private readonly List<Track> activeTracks = [];
    private readonly Dictionary<int, Task<Track?>> eventTrackLoads = [];
    private readonly Dictionary<int, Track> retainedTracks = [];
    private readonly HashSet<ushort> resumedEventKeys = [];

    private int nextEventIndex;
    private bool eventResyncRequired;
    private double derivedLength;
    private bool derivedLengthResolutionComplete;

    public double Length { get; private set; }

    public bool IsLengthFinal => !deriveLengthFromTracks || derivedLengthResolutionComplete;

    internal IReadOnlyList<BmsPreviewTimelineEntry> Events => sortedEvents;

    internal IReadOnlyList<Track> ActiveTracks => activeTracks;

    internal bool HasRetainedTracks => retainedTracks.Count > 0;

    internal BmsEventPreviewPlayback(
        BmsPreviewTrack owner,
        BmsEventPreviewTimeline timeline,
        string? basePath,
        AudioManager audioManager,
        Func<CancellationToken, Task>? beforeTrackLoad = null)
    {
        this.owner = owner;
        sortedEvents.AddRange(timeline.Entries);
        Length = timeline.Length;
        deriveLengthFromTracks = timeline.DeriveLengthFromTracks;
        retainLoadedTracks = timeline.RetainLoadedTracks;

        if (basePath != null)
            audioLoader = new BmsPreviewAudioLoader(basePath, audioManager, beforeTrackLoad);
    }

    public void Dispose()
    {
        disposePreviewPlayback();
        discardEventTrackLoads();
        audioLoader?.Dispose();
    }

    public void EnterPreview(double currentTime)
    {
        if (currentTime <= 0)
        {
            nextEventIndex = 0;
            eventResyncRequired = false;
            return;
        }

        nextEventIndex = findFirstEventAfter(currentTime);
        eventResyncRequired = true;
    }

    public void ExitPreview()
    {
        eventResyncRequired = false;
        stopAndDiscardPreviewPlayback();
    }

    public void Start(double currentTime)
    {
        if (currentTime <= 0)
        {
            nextEventIndex = 0;
            eventResyncRequired = false;
            return;
        }

        eventResyncRequired = activeTracks.Count == 0;

        if (eventResyncRequired)
            nextEventIndex = findFirstEventAfter(currentTime);
    }

    public void Stop()
    {
        stopPreviewPlayback();
        discardEventTrackLoads();
        resumedEventKeys.Clear();
    }

    public void Seek(double seek, bool previewMode)
    {
        stopPreviewPlayback();
        discardEventTrackLoads();
        resumedEventKeys.Clear();

        if (previewMode)
        {
            nextEventIndex = seek == 0 ? 0 : findFirstEventAfter(seek);
            eventResyncRequired = seek > 0;
        }
    }

    public void Reset()
    {
        nextEventIndex = 0;
        stopPreviewPlayback();
        discardEventTrackLoads();
        resumedEventKeys.Clear();
    }

    public BmsPreviewPlaybackStartState Update(double currentTime, bool requireDueAudioReady)
    {
        prefetchEventTracks(currentTime);

        if (eventResyncRequired)
        {
            var resumeState = resumeEventTracks(currentTime);

            if (resumeState != BmsPreviewPlaybackStartState.Ready)
                return resumeState;

            eventResyncRequired = false;
            cleanupTracks();
            return BmsPreviewPlaybackStartState.Ready;
        }

        if (requireDueAudioReady && !areDueEventTracksReady(currentTime))
            return BmsPreviewPlaybackStartState.Waiting;

        var initialAudioUnavailable = false;

        while (nextEventIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextEventIndex];

            if (currentTime < evt.Time)
                break;

            var result = tryPlayTrack(nextEventIndex, currentTime);

            if (result == EventPlaybackResult.Pending)
                break;

            if (nextEventIndex == 0 && result == EventPlaybackResult.Unavailable)
            {
                initialAudioUnavailable = true;
                derivedLengthResolutionComplete = true;
            }

            nextEventIndex++;
        }

        cleanupTracks();
        return initialAudioUnavailable
            ? BmsPreviewPlaybackStartState.InitialAudioUnavailable
            : BmsPreviewPlaybackStartState.Ready;
    }

    private bool areDueEventTracksReady(double currentTime)
    {
        if (audioLoader == null)
            return true;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= currentTime; i++)
        {
            if (retainedTracks.ContainsKey(i))
                continue;

            if (!eventTrackLoads.TryGetValue(i, out var loadTask) || !loadTask.IsCompleted)
                return false;
        }

        return true;
    }

    private void prefetchEventTracks(double currentTime)
    {
        if (audioLoader == null)
            return;

        var started = 0;
        var prefetchUntil = currentTime + event_prefetch_time;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= prefetchUntil; i++)
        {
            if (eventTrackLoads.ContainsKey(i) || retainedTracks.ContainsKey(i))
                continue;

            eventTrackLoads[i] = audioLoader.LoadTrackAsync(sortedEvents[i].SamplePath);

            if (++started >= event_prefetch_batch_size)
                break;
        }
    }

    private EventPlaybackResult tryPlayTrack(int eventIndex, double currentTime)
    {
        var evt = sortedEvents[eventIndex];

        if (retainedTracks.TryGetValue(eventIndex, out var retainedTrack))
        {
            retainedTrack.Seek(0);
            startTrack(evt, retainedTrack);
            return EventPlaybackResult.Played;
        }

        if (audioLoader == null)
            return EventPlaybackResult.Unavailable;

        if (!eventTrackLoads.TryGetValue(eventIndex, out var loadTask))
        {
            prefetchEventTracks(currentTime);
            return EventPlaybackResult.Pending;
        }

        if (!loadTask.IsCompleted)
            return EventPlaybackResult.Pending;

        var track = consumeCompletedTrack(eventIndex, "load");

        if (track == null)
            return EventPlaybackResult.Unavailable;

        startTrack(evt, track);

        return EventPlaybackResult.Played;
    }

    private BmsPreviewPlaybackStartState resumeEventTracks(double currentTime)
    {
        if (audioLoader == null)
        {
            if (nextEventIndex > 0 && sortedEvents[0].ResumeAfterSeek)
            {
                derivedLengthResolutionComplete = true;
                return BmsPreviewPlaybackStartState.InitialAudioUnavailable;
            }

            return BmsPreviewPlaybackStartState.Ready;
        }

        var allReady = true;
        var pendingKeys = new HashSet<ushort>(resumedEventKeys);
        List<int> eventIndices = [];

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];

            // Events are scanned newest-first, so only the latest active trigger per definition
            // should own a pending load during seek reconstruction.
            if (!evt.ResumeAfterSeek || !pendingKeys.Add(evt.SampleKey))
                continue;

            eventIndices.Add(i);

            if (retainedTracks.ContainsKey(i))
                continue;

            if (!eventTrackLoads.TryGetValue(i, out var loadTask))
            {
                eventTrackLoads[i] = audioLoader.LoadTrackAsync(evt.SamplePath);
                allReady = false;
            }
            else if (!loadTask.IsCompleted)
                allReady = false;
        }

        if (!allReady)
            return BmsPreviewPlaybackStartState.Waiting;

        var initialAudioUnavailable = false;

        foreach (var eventIndex in eventIndices)
        {
            var evt = sortedEvents[eventIndex];

            var track = retainedTracks.TryGetValue(eventIndex, out var retainedTrack)
                ? retainedTrack
                : consumeCompletedTrack(eventIndex, "resume");

            resumedEventKeys.Add(evt.SampleKey);

            if (track == null)
            {
                if (eventIndex == 0)
                    initialAudioUnavailable = true;

                continue;
            }

            var offset = currentTime - evt.Time;
            track.Seek(offset);

            if (track.Length <= 0 || offset >= track.Length)
            {
                if (!retainLoadedTracks)
                    track.Dispose();

                continue;
            }

            startTrack(evt, track);
        }

        return initialAudioUnavailable
            ? BmsPreviewPlaybackStartState.InitialAudioUnavailable
            : BmsPreviewPlaybackStartState.Ready;
    }

    private void discardEventTrackLoads()
    {
        if (audioLoader == null)
            return;

        foreach (var task in eventTrackLoads.Values)
            audioLoader.DiscardEventTrack(task);

        eventTrackLoads.Clear();
    }

    private void stopAndDiscardPreviewPlayback()
    {
        stopPreviewPlayback();
        discardEventTrackLoads();
        resumedEventKeys.Clear();
    }

    private void stopPreviewPlayback()
    {
        for (var i = 0; i < activeTracks.Count; i++)
        {
            var track = activeTracks[i];

            if (!track.IsDisposed)
            {
                track.Stop();

                if (!retainLoadedTracks)
                    track.Dispose();
            }
        }

        activeTracks.Clear();
    }

    private void disposePreviewPlayback()
    {
        stopPreviewPlayback();

        foreach (var track in retainedTracks.Values)
            track.Dispose();

        retainedTracks.Clear();
    }

    private void cleanupTracks()
    {
        for (var i = activeTracks.Count - 1; i >= 0; i--)
        {
            var track = activeTracks[i];

            if (track.IsDisposed)
            {
                activeTracks.RemoveAt(i);
                continue;
            }

            if (track.HasCompleted)
            {
                if (!retainLoadedTracks)
                    track.Dispose();

                activeTracks.RemoveAt(i);
            }
        }
    }

    private void updateLength(BmsPreviewTimelineEntry evt, Track track)
    {
        if (!deriveLengthFromTracks || track.Length <= 0)
            return;

        derivedLength = Math.Max(derivedLength, evt.Time + track.Length);
        Length = derivedLength;
        derivedLengthResolutionComplete = true;
    }

    private void startTrack(BmsPreviewTimelineEntry evt, Track track)
    {
        owner.BindPreviewAdjustments(track, evt.Volume);
        track.Start();
        activeTracks.Add(track);
    }

    private Track? consumeCompletedTrack(int eventIndex, string operation)
    {
        var evt = sortedEvents[eventIndex];
        var loadTask = eventTrackLoads[eventIndex];
        eventTrackLoads.Remove(eventIndex);
        audioLoader!.MarkEventTrackConsumed(loadTask);

        try
        {
            var track = loadTask.GetAwaiter().GetResult();

            if (track == null)
                return null;

            updateLength(evt, track);

            if (retainLoadedTracks)
                retainedTracks[eventIndex] = track;

            return track;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to {operation} BMS preview sample '{evt.SamplePath}'.");
            return null;
        }
    }

    private int findFirstEventAfter(double time)
    {
        var low = 0;
        var high = sortedEvents.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (sortedEvents[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private enum EventPlaybackResult
    {
        Pending,
        Played,
        Unavailable,
    }
}
