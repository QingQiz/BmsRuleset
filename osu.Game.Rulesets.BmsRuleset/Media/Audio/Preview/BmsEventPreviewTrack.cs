using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsEventPreviewTrack : BmsPreviewTrack
{
    private readonly string? basePath;
    private readonly AudioManager audioManager;
    private readonly CancellationTokenSource timelineCancellation = new();
    private readonly IReadOnlyList<Func<CancellationToken, BmsEventPreviewTimeline>> timelineSources;

    private Task<BmsEventPreviewTimeline> timelineTask;
    private BmsEventPreviewPlayback? playback;
    private double? pendingPreviewPosition;
    private bool previewStartPending;
    private int timelineSourceIndex;

    protected override bool CanComplete => playback != null;

    protected override bool StartClockImmediately => PlaybackMode != BmsPreviewTrackPlaybackMode.Preview;

    protected override bool IsLengthFinal => playback?.IsLengthFinal == true;

    /// <summary>
    /// Gameplay uses this track only as a clock; BMS audio is driven by chart events elsewhere.
    /// </summary>
    protected override void OnPlaybackModeChanged(BmsPreviewTrackPlaybackMode mode)
    {
        EnqueueAction(() =>
        {
            if (mode == BmsPreviewTrackPlaybackMode.Preview)
                playback?.EnterPreview(CurrentTime);
            else
            {
                var startClock = previewStartPending;
                previewStartPending = false;
                pendingPreviewPosition = null;
                playback?.ExitPreview();

                if (startClock)
                    StartClock();
            }
        });
    }

    internal BmsEventPreviewTrack(
        Func<CancellationToken, BmsEventPreviewTimeline> timelineFactory,
        string? basePath,
        AudioManager audioManager)
        : this([timelineFactory], basePath, audioManager)
    {
    }

    internal BmsEventPreviewTrack(
        IReadOnlyList<Func<CancellationToken, BmsEventPreviewTimeline>> timelineSources,
        string? basePath,
        AudioManager audioManager)
        : base(audioManager)
    {
        if (timelineSources.Count == 0)
            throw new ArgumentException(@"At least one preview timeline source is required.", nameof(timelineSources));

        this.basePath = basePath;
        this.audioManager = audioManager;
        this.timelineSources = timelineSources;
        Length = BmsEventPreviewTimeline.DEFAULT_LENGTH;
        timelineTask = prepareTimelineSource();
    }

    protected override void PrepareStart() => consumeTimeline();

    protected override void StartPlayback()
    {
        if (PlaybackMode != BmsPreviewTrackPlaybackMode.Preview)
            return;

        if (!IsRunning)
            previewStartPending = true;

        if (playback != null)
            playback.Start(CurrentTime);
        else
            pendingPreviewPosition = CurrentTime;
    }

    protected override void StopPlayback()
    {
        previewStartPending = false;
        pendingPreviewPosition = null;
        playback?.Stop();
    }

    protected override void SeekPlayback(double seek, bool wasRunning)
    {
        if (playback != null)
            playback.Seek(seek, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);
        else if ((wasRunning || previewStartPending) && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            pendingPreviewPosition = seek;
    }

    protected override void ResetPlayback()
    {
        previewStartPending = false;
        pendingPreviewPosition = null;
        playback?.Reset();
    }

    protected override void UpdateState()
    {
        consumeTimeline();
        base.UpdateState();

        var previewMode = PlaybackMode == BmsPreviewTrackPlaybackMode.Preview;
        var shouldUpdatePlayback = previewMode && (IsRunning || previewStartPending);
        var requireDueAudioReady = previewStartPending || IsRestoreFadePending || IsSeekFadePending;
        var startState = shouldUpdatePlayback
            ? playback?.Update(CurrentTime, requireDueAudioReady) ?? BmsPreviewPlaybackStartState.Waiting
            : BmsPreviewPlaybackStartState.Waiting;

        if (startState == BmsPreviewPlaybackStartState.InitialAudioUnavailable && advanceToNextTimelineSource())
            return;

        if (playback != null)
        {
            Length = playback.Length;

            if (TryResolvePendingRestorePosition())
                startState = playback.Update(CurrentTime, requireDueAudioReady);
        }

        if (startState != BmsPreviewPlaybackStartState.Waiting)
        {
            BeginPendingRestoreFade();
            BeginPendingSeekFade();
        }

        if (previewStartPending && startState != BmsPreviewPlaybackStartState.Waiting)
        {
            previewStartPending = false;
            // Starting the preview clock earlier would collapse every event elapsed during the first sample load.
            StartClock();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            timelineCancellation.Cancel();

            _ = timelineTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            timelineCancellation.Dispose();
            playback?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void consumeTimeline()
    {
        if (playback != null || !timelineTask.IsCompleted)
            return;

        BmsEventPreviewTimeline timeline;

        try
        {
            timeline = timelineTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            return;
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to prepare BMS event preview timeline.");
            timeline = new BmsEventPreviewTimeline([], BmsEventPreviewTimeline.DEFAULT_LENGTH);
        }

        activateTimeline(timeline);
    }

    private bool advanceToNextTimelineSource()
    {
        if (timelineSourceIndex + 1 >= timelineSources.Count)
            return false;

        playback?.Dispose();
        playback = null;
        timelineSourceIndex++;
        Length = BmsEventPreviewTimeline.DEFAULT_LENGTH;
        pendingPreviewPosition = CurrentTime;
        timelineTask = prepareTimelineSource();
        return true;
    }

    private Task<BmsEventPreviewTimeline> prepareTimelineSource() => Task.Run(
        () => timelineSources[timelineSourceIndex](timelineCancellation.Token),
        timelineCancellation.Token);

    private void activateTimeline(BmsEventPreviewTimeline timeline)
    {
        playback = new BmsEventPreviewPlayback(this, timeline, basePath, audioManager);
        Length = playback.Length;
        var restorePositionResolved = TryResolvePendingRestorePosition();
        var activationPosition = restorePositionResolved ? CurrentTime : pendingPreviewPosition ?? CurrentTime;
        pendingPreviewPosition = null;

        if (!restorePositionResolved)
            playback.Seek(activationPosition, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);

        if ((IsRunning || previewStartPending) && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            playback.Start(activationPosition);
    }
}
