using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

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
    private readonly AudioManager audioManager;
    private readonly List<BmsPreviewTimelineEntry> sortedEvents = [];
    private readonly bool deriveLengthFromSamples;
    private readonly bool extendLengthFromSamples;
    private readonly BindableDouble masterGain = new(1);
    private readonly CancellationTokenSource cancellation = new();
    private readonly HashSet<ushort> preparedSamples = [];
    private readonly Dictionary<ushort, double> sampleEndTimes = [];
    private readonly List<ushort> unresolvedSampleLengths = [];

    private BmsPcmPlaybackSession? playbackSession;
    private int nextEventIndex;
    private bool eventResyncRequired;
    private double derivedLength;
    private bool derivedLengthResolutionComplete;
    private bool disposed;

    public double Length { get; private set; }

    public bool IsLengthFinal => !deriveLengthFromSamples || derivedLengthResolutionComplete;

    internal IReadOnlyList<BmsPreviewTimelineEntry> Events => sortedEvents;

    internal int ActiveVoiceCount => playbackSession?.AudioDiagnostics.ActiveVoices ?? 0;

    internal double OutputGain => masterGain.Value;

    internal BmsEventPreviewPlayback(
        BmsPreviewTrack owner,
        BmsEventPreviewTimeline timeline,
        string? basePath,
        AudioManager audioManager,
        Func<CancellationToken, Task>? beforeAssetLoad = null)
    {
        this.owner = owner;
        this.audioManager = audioManager;
        sortedEvents.AddRange(timeline.Entries);
        Length = timeline.Length;
        deriveLengthFromSamples = timeline.DeriveLengthFromSamples;
        extendLengthFromSamples = timeline.ExtendLengthFromSamples;

        if (deriveLengthFromSamples || extendLengthFromSamples)
        {
            foreach (var group in sortedEvents.GroupBy(evt => evt.SampleKey))
                sampleEndTimes[group.Key] = group.Max(evt => evt.Time);
        }

        if (basePath == null)
            return;

        var definitions = sortedEvents
            .GroupBy(evt => evt.SampleKey)
            .ToDictionary(group => group.Key, group => group.First().SamplePath);
        var previewRate = Math.Abs(owner.AggregateTempo.Value);
        var rate = double.IsFinite(previewRate) && previewRate is >= 0.05 and <= 2 ? previewRate : 1;

        playbackSession = new BmsPcmPlaybackSession(
            definitions,
            basePath,
            rate,
            [],
            audioManager,
            () => owner.CurrentTime,
            masterGain,
            beforeAssetLoad);
        playbackSession.Initialise(cancellation.Token, owner.CurrentTime, waitForInitialAssets: false);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cancellation.Cancel();
        playbackSession?.Dispose();
        playbackSession = null;
        cancellation.Dispose();
    }

    public void EnterPreview(double currentTime)
    {
        nextEventIndex = currentTime <= 0 ? 0 : findFirstEventAfter(currentTime);
        eventResyncRequired = currentTime > 0;
    }

    public void ExitPreview()
    {
        eventResyncRequired = false;
        stopPlayback();
    }

    public void Start(double currentTime)
    {
        if (currentTime <= 0)
        {
            nextEventIndex = 0;
            eventResyncRequired = false;
            return;
        }

        eventResyncRequired = ActiveVoiceCount == 0;

        if (eventResyncRequired)
            nextEventIndex = findFirstEventAfter(currentTime);
    }

    public void Stop() => stopPlayback();

    public void Seek(double seek, bool previewMode)
    {
        stopPlayback();

        if (!previewMode)
            return;

        nextEventIndex = seek == 0 ? 0 : findFirstEventAfter(seek);
        eventResyncRequired = seek > 0;
    }

    public void Reset()
    {
        nextEventIndex = 0;
        eventResyncRequired = false;
        stopPlayback();
    }

    public BmsPreviewPlaybackStartState Update(double currentTime, bool requireDueAudioReady)
    {
        updateMasterGain();
        playbackSession?.Update(currentTime);
        prefetchSamples(currentTime);
        updateLengthFromPreparedSamples();

        if (eventResyncRequired)
        {
            var state = resumeBackgroundSamples(currentTime);
            if (state != BmsPreviewPlaybackStartState.Ready)
                return state;

            eventResyncRequired = false;
            return BmsPreviewPlaybackStartState.Ready;
        }

        if (requireDueAudioReady && !areDueSamplesResolved(currentTime))
            return BmsPreviewPlaybackStartState.Waiting;

        var controller = playbackSession?.Controller;
        var initialAudioUnavailable = false;

        while (nextEventIndex < sortedEvents.Count && sortedEvents[nextEventIndex].Time <= currentTime)
        {
            var evt = sortedEvents[nextEventIndex];

            if (controller == null || !controller.HasSampleDefinition(evt.SampleKey))
            {
                if (nextEventIndex == 0)
                {
                    initialAudioUnavailable = true;
                    derivedLengthResolutionComplete = true;
                }

                nextEventIndex++;
                continue;
            }

            if (!controller.IsSampleReady(evt.SampleKey))
                break;

            controller.QueuePlay(evt.SampleKey, evt.Volume, 0);
            nextEventIndex++;
        }

        controller?.SubmitLivePlayBatch();
        return initialAudioUnavailable
            ? BmsPreviewPlaybackStartState.InitialAudioUnavailable
            : BmsPreviewPlaybackStartState.Ready;
    }

    private void prefetchSamples(double currentTime)
    {
        var controller = playbackSession?.Controller;
        if (controller == null)
            return;

        var started = 0;
        var prefetchUntil = currentTime + event_prefetch_time;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= prefetchUntil; i++)
        {
            var sampleKey = sortedEvents[i].SampleKey;
            if (!prepareSample(controller, sampleKey))
                continue;

            if (++started >= event_prefetch_batch_size)
                break;
        }
    }

    private bool areDueSamplesResolved(double currentTime)
    {
        var controller = playbackSession?.Controller;
        if (controller == null)
            return true;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= currentTime; i++)
        {
            var key = sortedEvents[i].SampleKey;
            if (controller.HasSampleDefinition(key) && !controller.IsSampleReady(key))
                return false;
        }

        return true;
    }

    private BmsPreviewPlaybackStartState resumeBackgroundSamples(double currentTime)
    {
        var controller = playbackSession?.Controller;
        if (controller == null)
            return nextEventIndex > 0 && sortedEvents[0].ResumeAfterSeek
                ? BmsPreviewPlaybackStartState.InitialAudioUnavailable
                : BmsPreviewPlaybackStartState.Ready;

        var resumeEvents = new List<BmsPreviewTimelineEntry>();
        var seenKeys = new HashSet<ushort>();

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];
            if (!evt.ResumeAfterSeek || !seenKeys.Add(evt.SampleKey))
                continue;

            prepareSample(controller, evt.SampleKey);
            resumeEvents.Add(evt);
        }

        if (resumeEvents.Any(evt => controller.HasSampleDefinition(evt.SampleKey)
                                    && !controller.IsSampleReady(evt.SampleKey, currentTime - evt.Time)))
            return BmsPreviewPlaybackStartState.Waiting;

        var initialAudioUnavailable = false;

        foreach (var evt in resumeEvents)
        {
            if (!controller.HasSampleDefinition(evt.SampleKey))
            {
                if (evt.Equals(sortedEvents[0]))
                    initialAudioUnavailable = true;

                continue;
            }

            var offset = currentTime - evt.Time;
            if (offset < controller.GetSampleLength(evt.SampleKey))
                controller.QueuePlay(evt.SampleKey, evt.Volume, offset);
        }

        controller.SubmitLivePlayBatch();
        return initialAudioUnavailable
            ? BmsPreviewPlaybackStartState.InitialAudioUnavailable
            : BmsPreviewPlaybackStartState.Ready;
    }

    private void updateLengthFromPreparedSamples()
    {
        if (!deriveLengthFromSamples && !extendLengthFromSamples)
            return;

        var controller = playbackSession?.Controller;
        if (controller == null)
            return;

        for (var i = unresolvedSampleLengths.Count - 1; i >= 0; i--)
        {
            var sampleKey = unresolvedSampleLengths[i];
            if (!controller.IsSampleReady(sampleKey))
                continue;

            var sampleLength = controller.GetSampleLength(sampleKey);
            if (sampleLength <= 0)
                continue;

            unresolvedSampleLengths.RemoveAt(i);
            derivedLength = Math.Max(derivedLength, sampleEndTimes[sampleKey] + sampleLength);
        }

        if (derivedLength <= 0)
            return;

        Length = deriveLengthFromSamples ? derivedLength : Math.Max(Length, derivedLength);

        if (deriveLengthFromSamples)
            derivedLengthResolutionComplete = true;
    }

    private void updateMasterGain()
    {
        var global = audioManager.AggregateVolume.Value;
        var gain = owner.PreviewPlaybackGain * (double.IsFinite(global) ? Math.Max(0, global) : 0);
        masterGain.Value = double.IsFinite(gain) ? Math.Max(0, gain) : 0;
    }

    private void stopPlayback() => playbackSession?.Controller?.StopAll();

    private bool prepareSample(BmsPcmPlaybackController controller, ushort sampleKey)
    {
        if (!preparedSamples.Add(sampleKey))
            return false;

        controller.PrepareSample(sampleKey);

        if (sampleEndTimes.ContainsKey(sampleKey))
            unresolvedSampleLengths.Add(sampleKey);

        return true;
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
