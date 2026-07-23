using System;
using System.Diagnostics;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

public enum BmsPreviewTrackPlaybackMode
{
    Preview,
    GameplayClockOnly,
}

public abstract class BmsPreviewTrack : Track
{
    private const double restore_fade_duration = 20;

    private readonly BindableDouble previewOutputVolume = new(1);
    private readonly BindableDouble restoreFadeVolume = new(1);

    private readonly StopwatchClock clock = new();

    private double seekOffset;

    private long restoreFadeStart;
    private bool restoreFadeInProgress;

    public override bool IsRunning
    {
        get
        {
            lock (clock) return clock.IsRunning;
        }
    }

    public override bool IsDummyDevice => false;

    public override double CurrentTime
    {
        get
        {
            lock (clock)
            {
                var time = seekOffset + clock.CurrentTime;
                return PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly ? time : Math.Min(Length, time);
            }
        }
    }

    public BmsPreviewTrackPlaybackMode PlaybackMode
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;
            previewOutputVolume.Value = value == BmsPreviewTrackPlaybackMode.Preview ? 1 : 0;
            OnPlaybackModeChanged(value);
        }
    }

    protected virtual bool CanComplete => true;

    protected BmsPreviewTrack()
        : base("bms-preview")
    {
        // Track rate adjustments do not reach the standalone clock automatically.
        AggregateFrequency.ValueChanged += _ => updateClockRate();
        AggregateTempo.ValueChanged += _ => updateClockRate();
        updateClockRate();
    }

    public override void Start() => StartAsync().WaitSafely();

    public override Task StartAsync() => EnqueueAction(startInternal);

    public override void Stop() => StopAsync().WaitSafely();

    public override Task StopAsync() => EnqueueAction(stopInternal);

    public override bool Seek(double seek) => SeekAsync(seek).GetResultSafely();

    public override async Task<bool> SeekAsync(double seek)
    {
        var clamped = PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly
            ? Math.Max(0, seek)
            : Math.Clamp(seek, 0, Length);
        var success = clamped == seek;
        await EnqueueAction(() => seekInternal(clamped, success)).ConfigureAwait(false);
        return success;
    }

    public override void Reset() => EnqueueAction(() =>
    {
        restoreFadeInProgress = false;
        restoreFadeVolume.Value = 1;
        resetInternal();
        base.Reset();
    }).WaitSafely();

    internal void RestorePreview(double? gameplayTime)
    {
        EnqueueAction(() =>
        {
            if (gameplayTime is { } time)
            {
                var clamped = Math.Clamp(time, 0, Length);
                seekInternal(clamped, clamped == time);
            }

            PlaybackMode = BmsPreviewTrackPlaybackMode.Preview;
            Volume.Value = 1;

            if (CurrentTime >= Length)
                seekInternal(0, true);

            beginRestoreFade();
            startInternal();
        }).WaitSafely();
    }

    protected override void UpdateState()
    {
        base.UpdateState();
        updateRestoreFade();

        if (PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly)
            return;

        if (!CanComplete)
            return;

        lock (clock)
        {
            if (!clock.IsRunning || CurrentTime < Length)
                return;

            if (Looping)
                Restart();
            else
            {
                Stop();
                RaiseCompleted();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            lock (clock) clock.Stop();
        }

        base.Dispose(disposing);
    }

    protected virtual void PrepareStart()
    {
    }

    protected virtual void OnPlaybackModeChanged(BmsPreviewTrackPlaybackMode mode)
    {
    }

    protected abstract void StartPlayback();

    protected abstract void StopPlayback();

    protected abstract void SeekPlayback(double seek, bool wasRunning);

    protected abstract void ResetPlayback();

    internal void BindPreviewAdjustments(IAdjustableAudioComponent component, int volume = 100)
    {
        component.RemoveAllAdjustments(AdjustableProperty.Volume);
        component.RemoveAllAdjustments(AdjustableProperty.Balance);
        component.RemoveAllAdjustments(AdjustableProperty.Frequency);
        component.RemoveAllAdjustments(AdjustableProperty.Tempo);
        component.BindAdjustments(this);
        component.AddAdjustment(AdjustableProperty.Volume, new BindableDouble(Math.Max(0, volume) / 100.0));
        component.AddAdjustment(AdjustableProperty.Volume, previewOutputVolume);
        component.AddAdjustment(AdjustableProperty.Volume, restoreFadeVolume);
    }

    private void beginRestoreFade()
    {
        restoreFadeVolume.Value = 0;
        restoreFadeStart = Stopwatch.GetTimestamp();
        restoreFadeInProgress = true;
    }

    private void updateRestoreFade()
    {
        if (!restoreFadeInProgress)
            return;

        var progress = Stopwatch.GetElapsedTime(restoreFadeStart).TotalMilliseconds / restore_fade_duration;
        restoreFadeVolume.Value = Math.Min(1, progress);
        restoreFadeInProgress = progress < 1;
    }

    private void updateClockRate()
    {
        lock (clock)
            clock.Rate = Rate;
    }

    private void startInternal()
    {
        PrepareStart();

        if (Length == 0 || (PlaybackMode == BmsPreviewTrackPlaybackMode.Preview && CurrentTime >= Length))
            return;

        StartPlayback();

        lock (clock) clock.Start();
    }

    private void stopInternal()
    {
        lock (clock) clock.Stop();
        StopPlayback();
    }

    private void seekInternal(double seek, bool success)
    {
        seekOffset = seek;
        var wasRunning = IsRunning;

        lock (clock)
        {
            if (success && wasRunning)
                clock.Restart();
            else
                clock.Reset();
        }

        SeekPlayback(seek, wasRunning);
    }

    private void resetInternal()
    {
        lock (clock) clock.Reset();
        seekOffset = 0;
        ResetPlayback();
    }
}
