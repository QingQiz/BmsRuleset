using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal sealed class BmsDedicatedPreviewTrack : BmsPreviewTrack
{
    private readonly BmsPreviewAudioLoader loader;
    private readonly Func<CancellationToken, BmsEventPreviewTimeline>? fallbackTimelineFactory;
    private readonly string basePath;
    private readonly AudioManager audioManager;
    private readonly CancellationTokenSource fallbackCancellation = new();
    private Track? previewTrack;
    private BmsEventPreviewPlayback? fallbackPlayback;
    private Task<BmsEventPreviewTimeline>? fallbackTimelineTask;
    private bool dedicatedLoadFailed;
    private double? pendingFallbackPosition;

    internal BmsDedicatedPreviewTrack(
        string basePath,
        string? previewFile,
        AudioManager audioManager,
        Func<CancellationToken, BmsEventPreviewTimeline>? fallbackTimelineFactory = null)
    {
        loader = BmsPreviewAudioLoader.ForDedicatedPreview(basePath, previewFile, audioManager);
        this.basePath = basePath;
        this.audioManager = audioManager;
        this.fallbackTimelineFactory = fallbackTimelineFactory;
        Length = 30000;
    }

    protected override void OnPlaybackModeChanged(BmsPreviewTrackPlaybackMode mode)
    {
        EnqueueAction(() =>
        {
            if (mode == BmsPreviewTrackPlaybackMode.Preview)
                fallbackPlayback?.EnterPreview(CurrentTime);
            else
            {
                pendingFallbackPosition = null;
                fallbackPlayback?.ExitPreview();
                stopDedicatedPlayback();
            }
        });
    }

    protected override bool CanComplete =>
        !loader.IsPending
        && (!dedicatedLoadFailed || fallbackTimelineFactory == null || fallbackPlayback != null);

    protected override void UpdateState()
    {
        // Consume the asynchronous file result before the base clock update so its duration is
        // visible to completion checks on the same audio frame.
        consumeDedicatedTrack();
        activateEventFallback();
        base.UpdateState();
        fallbackPlayback?.Update(CurrentTime, IsRunning, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            loader.Dispose();
            fallbackCancellation.Cancel();

            if (fallbackTimelineTask != null)
            {
                _ = fallbackTimelineTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            fallbackCancellation.Dispose();

            fallbackPlayback?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void consumeDedicatedTrack()
    {
        if (!loader.TryConsume(out var loadedTrack, out var error))
            return;

        previewTrack = loadedTrack;

        if (error != null)
            Logger.Error(error, "Failed to load dedicated BMS preview audio.");

        if (previewTrack == null)
        {
            dedicatedLoadFailed = true;
            if (fallbackTimelineFactory != null)
                fallbackTimelineTask = Task.Run(
                    () => fallbackTimelineFactory(fallbackCancellation.Token),
                    fallbackCancellation.Token);
            activateEventFallback();
            return;
        }

        pendingFallbackPosition = null;

        if (previewTrack.Length > 0)
            Length = previewTrack.Length;

        if (IsRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            startDedicatedPlayback();
    }

    protected override void PrepareStart()
    {
        consumeDedicatedTrack();
    }

    protected override void StartPlayback()
    {
        if (fallbackPlayback != null)
            fallbackPlayback.Start(CurrentTime);
        else if (PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
        {
            if (previewTrack == null)
                pendingFallbackPosition = CurrentTime;

            startDedicatedPlayback();
        }
    }

    protected override void StopPlayback()
    {
        pendingFallbackPosition = null;
        stopDedicatedPlayback();

        fallbackPlayback?.Stop();
    }

    protected override void SeekPlayback(double seek, bool wasRunning)
    {
        stopDedicatedPlayback();

        if (fallbackPlayback != null)
            fallbackPlayback.Seek(seek, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);
        else if (previewTrack != null && wasRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            startDedicatedPlayback();
        else if (wasRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            pendingFallbackPosition = seek;
    }

    protected override void ResetPlayback()
    {
        pendingFallbackPosition = null;
        stopDedicatedPlayback();

        fallbackPlayback?.Reset();
    }

    private void startDedicatedPlayback()
    {
        if (previewTrack == null || previewTrack.IsRunning)
            return;

        BindPreviewAdjustments(previewTrack);
        previewTrack.Seek(CurrentTime);
        previewTrack.Start();
    }

    private void stopDedicatedPlayback()
    {
        if (previewTrack is { IsDisposed: false })
            previewTrack.Stop();
    }

    private void activateEventFallback()
    {
        if (!dedicatedLoadFailed || fallbackPlayback != null || fallbackTimelineTask is not { IsCompleted: true })
            return;

        BmsEventPreviewTimeline timeline;

        try
        {
            timeline = fallbackTimelineTask.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to prepare BMS event preview fallback.");
            timeline = new BmsEventPreviewTimeline([], 30000);
        }

        fallbackPlayback = new BmsEventPreviewPlayback(this, timeline, basePath, audioManager);
        Length = fallbackPlayback.Length;
        var activationPosition = pendingFallbackPosition ?? CurrentTime;
        pendingFallbackPosition = null;
        fallbackPlayback.Seek(activationPosition, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);

        if (IsRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            fallbackPlayback.Start(activationPosition);
    }
}
