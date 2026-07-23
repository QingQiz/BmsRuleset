using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsEventPreviewPlayback : IDisposable
{
    private const double event_prefetch_time = 10_000;
    private const int event_prefetch_batch_size = 16;

    private readonly BmsPreviewTrack owner;
    private readonly List<BmsPreviewTimelineEntry> sortedEvents = [];
    private readonly BmsPreviewAudioLoader? audioLoader;
    private readonly List<Track> activeTracks = [];
    private readonly Dictionary<int, Task<Track?>> eventTrackLoads = [];
    private readonly HashSet<ushort> resumedEventKeys = [];

    private int nextEventIndex;
    private bool eventResyncRequired;

    public double Length { get; }

    internal BmsEventPreviewPlayback(
        BmsPreviewTrack owner,
        BmsEventPreviewTimeline timeline,
        string? basePath,
        AudioManager audioManager)
    {
        this.owner = owner;
        sortedEvents.AddRange(timeline.Entries);
        Length = timeline.Length;

        if (basePath != null)
            audioLoader = BmsPreviewAudioLoader.ForEventPreview(basePath, audioManager);
    }

    public void Dispose()
    {
        stopPreviewPlayback();
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

    public void Update(double currentTime, bool isRunning, bool previewMode)
    {
        if (!isRunning || !previewMode)
            return;

        prefetchEventTracks(currentTime);

        if (eventResyncRequired)
        {
            if (!resumeEventTracks(currentTime))
                return;

            eventResyncRequired = false;
        }

        while (nextEventIndex < sortedEvents.Count)
        {
            var evt = sortedEvents[nextEventIndex];

            if (currentTime < evt.Time)
                break;

            if (!tryPlayTrack(nextEventIndex, currentTime))
                break;

            nextEventIndex++;
        }

        cleanupTracks();
    }

    private void prefetchEventTracks(double currentTime)
    {
        if (audioLoader == null)
            return;

        var started = 0;
        var prefetchUntil = currentTime + event_prefetch_time;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= prefetchUntil; i++)
        {
            if (eventTrackLoads.ContainsKey(i))
                continue;

            eventTrackLoads[i] = audioLoader.LoadEventTrackAsync(sortedEvents[i].SamplePath);

            if (++started >= event_prefetch_batch_size)
                break;
        }
    }

    private bool tryPlayTrack(int eventIndex, double currentTime)
    {
        var evt = sortedEvents[eventIndex];

        if (!eventTrackLoads.TryGetValue(eventIndex, out var loadTask))
        {
            prefetchEventTracks(currentTime);
            return false;
        }

        if (!loadTask.IsCompleted)
            return false;

        eventTrackLoads.Remove(eventIndex);
        audioLoader?.MarkEventTrackConsumed(loadTask);

        Track? track;

        try
        {
            track = loadTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"Failed to load BMS event preview sample '{evt.SamplePath}'.");
            return true;
        }

        if (track == null)
            return true;

        owner.BindPreviewAdjustments(track, evt.Volume);
        track.Start();
        activeTracks.Add(track);

        return true;
    }

    private bool resumeEventTracks(double currentTime)
    {
        if (audioLoader == null)
            return true;

        var allReady = true;
        var pendingKeys = new HashSet<ushort>(resumedEventKeys);

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];

            // Events are scanned newest-first, so only the latest active trigger per definition
            // should own a pending load during seek reconstruction.
            if (!evt.ResumeAfterSeek || !pendingKeys.Add(evt.SampleKey))
                continue;

            if (!eventTrackLoads.TryGetValue(i, out var loadTask))
            {
                eventTrackLoads[i] = audioLoader!.LoadEventTrackAsync(evt.SamplePath);
                allReady = false;
                continue;
            }

            if (!loadTask.IsCompleted)
            {
                allReady = false;
                continue;
            }

            eventTrackLoads.Remove(i);
            audioLoader?.MarkEventTrackConsumed(loadTask);

            Track? track;

            try
            {
                track = loadTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $"Failed to resume BMS event preview sample '{evt.SamplePath}'.");
                resumedEventKeys.Add(evt.SampleKey);
                continue;
            }

            resumedEventKeys.Add(evt.SampleKey);

            if (track == null)
                continue;

            var offset = currentTime - evt.Time;
            track.Seek(offset);

            if (track.Length <= 0 || offset >= track.Length)
            {
                track.Dispose();
                continue;
            }

            owner.BindPreviewAdjustments(track, evt.Volume);
            track.Start();
            activeTracks.Add(track);
        }

        return allReady;
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
                track.Dispose();
            }
        }

        activeTracks.Clear();
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
                track.Dispose();
                activeTracks.RemoveAt(i);
            }
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

}
