using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Audio.Preview;

internal sealed class BmsEventPreviewTrack : BmsPreviewTrack
{
    private readonly string? basePath;
    private readonly AudioManager audioManager;
    private readonly CancellationTokenSource timelineCancellation = new();
    private readonly Task<BmsEventPreviewTimeline>? timelineTask;

    private BmsEventPreviewPlayback? playback;
    private double? pendingPreviewPosition;

    protected override bool CanComplete => timelineTask == null || playback != null;

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
                pendingPreviewPosition = null;
                playback?.ExitPreview();
            }
        });
    }

    internal BmsEventPreviewTrack(
        Func<IReadOnlyList<BmsPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager)
        : this(_ => sampleEventFactory(), sampleDefinitions, basePath, audioManager)
    {
    }

    internal BmsEventPreviewTrack(
        Func<CancellationToken, IReadOnlyList<BmsPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        string? basePath,
        AudioManager audioManager)
    {
        this.basePath = basePath;
        this.audioManager = audioManager;
        Length = 30000;
        timelineTask = Task.Run(
            () => BmsEventPreviewTimeline.Create(sampleEventFactory, sampleDefinitions, timelineCancellation.Token),
            timelineCancellation.Token);
    }

    protected override void PrepareStart() => consumeTimeline();

    protected override void StartPlayback()
    {
        if (PlaybackMode != BmsPreviewTrackPlaybackMode.Preview)
            return;

        if (playback != null)
            playback.Start(CurrentTime);
        else
            pendingPreviewPosition = CurrentTime;
    }

    protected override void StopPlayback()
    {
        pendingPreviewPosition = null;
        playback?.Stop();
    }

    protected override void SeekPlayback(double seek, bool wasRunning)
    {
        if (playback != null)
            playback.Seek(seek, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);
        else if (wasRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            pendingPreviewPosition = seek;
    }

    protected override void ResetPlayback()
    {
        pendingPreviewPosition = null;
        playback?.Reset();
    }

    protected override void UpdateState()
    {
        consumeTimeline();
        base.UpdateState();
        playback?.Update(CurrentTime, IsRunning, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            timelineCancellation.Cancel();

            if (timelineTask != null)
            {
                _ = timelineTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            timelineCancellation.Dispose();
            playback?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void consumeTimeline()
    {
        if (playback != null || timelineTask is not { IsCompleted: true })
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
            Logger.Error(exception, "Failed to prepare BMS event preview timeline.");
            timeline = new BmsEventPreviewTimeline([], 30000);
        }

        activateTimeline(timeline);
    }

    private void activateTimeline(BmsEventPreviewTimeline timeline)
    {
        playback = new BmsEventPreviewPlayback(this, timeline, basePath, audioManager);
        Length = playback.Length;
        var activationPosition = pendingPreviewPosition ?? CurrentTime;
        pendingPreviewPosition = null;
        playback.Seek(activationPosition, PlaybackMode == BmsPreviewTrackPlaybackMode.Preview);

        if (IsRunning && PlaybackMode == BmsPreviewTrackPlaybackMode.Preview)
            playback.Start(activationPosition);
    }
}
