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

public abstract class BmsPreviewTrack : Track, IAdjustableAudioComponent
{
    internal const double RESTORE_FADE_DURATION = 2_500;
    internal const double SEEK_FADE_OUT_DURATION = 20;
    internal const double SEEK_FADE_IN_DURATION = 50;

    private readonly BindableDouble previewOutputVolume = new(1);
    private readonly BindableDouble restoreFadeVolume = new(1);
    private readonly BindableDouble seekFadeVolume = new(1);
    private readonly IAggregateAudioAdjustment audioManagerAdjustments;

    private readonly StopwatchClock clock = new();

    private double seekOffset;
    private double? unresolvedRestorePosition;

    private long restoreFadeStart;
    private bool restoreFadeInProgress;

    private double? pendingSeekPosition;
    private bool pendingSeekSuccess;
    private long seekFadeStart;
    private double seekFadeStartVolume = 1;
    private SeekFadePhase seekFadePhase;

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
                return PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly || !IsLengthFinal ? time : Math.Min(Length, time);
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

    internal double RestoreFadeVolume => restoreFadeVolume.Value;

    internal double SeekFadeVolume => seekFadeVolume.Value;

    protected virtual bool CanComplete => true;

    protected virtual bool StartClockImmediately => true;

    protected virtual bool IsLengthFinal => true;

    protected bool IsRestoreFadePending { get; private set; }

    protected bool IsSeekFadePending => seekFadePhase == SeekFadePhase.WaitingForAudio;

    protected BmsPreviewTrack(IAggregateAudioAdjustment audioManagerAdjustments)
        : base("bms-preview")
    {
        this.audioManagerAdjustments = audioManagerAdjustments;

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
        await EnqueueAction(() =>
        {
            unresolvedRestorePosition = null;
            requestSeek(clamped, success);
        }).ConfigureAwait(false);
        return success;
    }

    public override void Reset() => EnqueueAction(() =>
    {
        cancelSeekFade();
        IsRestoreFadePending = false;
        restoreFadeInProgress = false;
        restoreFadeVolume.Value = 1;
        unresolvedRestorePosition = null;
        resetInternal();
        base.Reset();
    }).WaitSafely();

    internal void RestorePreview(double? gameplayTime)
    {
        EnqueueAction(() =>
        {
            cancelSeekFade();

            if (gameplayTime is { } time)
            {
                var target = IsLengthFinal ? Math.Clamp(time, 0, Length) : Math.Max(0, time);

                if (!IsLengthFinal)
                    unresolvedRestorePosition = target;

                seekInternal(target, target == time);
            }
            else if (!IsLengthFinal)
                unresolvedRestorePosition = CurrentTime;

            PlaybackMode = BmsPreviewTrackPlaybackMode.Preview;
            Volume.Value = 1;

            if (IsLengthFinal && CurrentTime >= Length)
                seekInternal(0, true);

            prepareRestoreFade();
            startInternal();
        }).WaitSafely();
    }

    protected override void UpdateState()
    {
        base.UpdateState();
        updateRestoreFade();
        updateSeekFade();

        if (PlaybackMode == BmsPreviewTrackPlaybackMode.GameplayClockOnly)
            return;

        if (!CanComplete)
            return;

        if (!IsLengthFinal)
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

    protected void StartClock()
    {
        lock (clock) clock.Start();
    }

    protected void BeginPendingRestoreFade()
    {
        if (!IsRestoreFadePending)
            return;

        IsRestoreFadePending = false;
        restoreFadeStart = Stopwatch.GetTimestamp();
        restoreFadeInProgress = true;
    }

    protected void BeginPendingSeekFade()
    {
        if (!IsSeekFadePending)
            return;

        seekFadeStart = Stopwatch.GetTimestamp();
        seekFadePhase = SeekFadePhase.FadingIn;
    }

    protected bool TryResolvePendingRestorePosition()
    {
        if (!IsLengthFinal || unresolvedRestorePosition is not { } requestedPosition)
            return false;

        unresolvedRestorePosition = null;
        var target = requestedPosition >= Length ? 0 : requestedPosition;
        seekInternal(target, true);
        return true;
    }

    /// <summary>
    /// Binds non-global adjustments to this logical preview owner.
    /// </summary>
    /// <remarks>
    /// The framework base method is non-virtual. Callers that require AudioManager filtering must use
    /// the <see cref="BmsPreviewTrack"/> or <see cref="IAdjustableAudioComponent"/> static type, as the
    /// framework audio collection and drawable wrapper paths do.
    /// </remarks>
    public new void BindAdjustments(IAggregateAudioAdjustment component)
    {
        // Event tracks receive AudioManager adjustments through their TrackStore, while wrapper and mod
        // adjustments must remain on the logical owner so they can be mirrored without double-applying globals.
        if (!ReferenceEquals(component, audioManagerAdjustments))
            base.BindAdjustments(component);
    }

    /// <summary>
    /// Unbinds non-global adjustments from this logical preview owner.
    /// </summary>
    /// <remarks>
    /// This has the same static dispatch requirement as <see cref="BindAdjustments"/>.
    /// </remarks>
    public new void UnbindAdjustments(IAggregateAudioAdjustment component)
    {
        if (!ReferenceEquals(component, audioManagerAdjustments))
            base.UnbindAdjustments(component);
    }

    void IAdjustableAudioComponent.BindAdjustments(IAggregateAudioAdjustment component) => BindAdjustments(component);

    void IAdjustableAudioComponent.UnbindAdjustments(IAggregateAudioAdjustment component) => UnbindAdjustments(component);

    internal PreviewPlaybackAdjustments BindPreviewAdjustments(IAdjustableAudioComponent component, int volume = 100)
    {
        var adjustments = new PreviewPlaybackAdjustments(this, Math.Max(0, volume) / 100.0);
        component.BindAdjustments(adjustments);
        return adjustments;
    }

    private void prepareRestoreFade()
    {
        restoreFadeVolume.Value = 0;
        IsRestoreFadePending = true;
        restoreFadeInProgress = false;
    }

    private void updateRestoreFade()
    {
        if (!restoreFadeInProgress)
            return;

        var progress = Stopwatch.GetElapsedTime(restoreFadeStart).TotalMilliseconds / RESTORE_FADE_DURATION;
        restoreFadeVolume.Value = Math.Min(1, progress);
        restoreFadeInProgress = progress < 1;
    }

    private void updateClockRate()
    {
        lock (clock)
            clock.Rate = Rate;
    }

    internal sealed class PreviewPlaybackAdjustments : IAggregateAudioAdjustment
    {
        private readonly BmsPreviewTrack owner;
        private readonly double eventVolume;

        private readonly BindableDouble volume = new(1);
        private readonly BindableDouble balance = new();
        private readonly BindableDouble frequency = new(1);
        private readonly BindableDouble tempo = new(1);

        public IBindable<double> AggregateVolume => volume;

        public IBindable<double> AggregateBalance => balance;

        public IBindable<double> AggregateFrequency => frequency;

        public IBindable<double> AggregateTempo => tempo;

        internal PreviewPlaybackAdjustments(BmsPreviewTrack owner, double eventVolume)
        {
            this.owner = owner;
            this.eventVolume = eventVolume;
            Update();
        }

        internal void Update()
        {
            volume.Value = owner.AggregateVolume.Value
                           * eventVolume
                           * owner.previewOutputVolume.Value
                           * owner.restoreFadeVolume.Value
                           * owner.seekFadeVolume.Value;
            balance.Value = owner.AggregateBalance.Value;
            frequency.Value = owner.AggregateFrequency.Value;
            tempo.Value = owner.AggregateTempo.Value;
        }
    }

    private void startInternal()
    {
        PrepareStart();

        if (Length == 0 || (PlaybackMode == BmsPreviewTrackPlaybackMode.Preview && IsLengthFinal && CurrentTime >= Length))
            return;

        StartPlayback();

        if (StartClockImmediately)
            StartClock();
    }

    private void stopInternal()
    {
        if (pendingSeekPosition is { })
            performPendingSeek();

        lock (clock) clock.Stop();
        StopPlayback();
        cancelSeekFade();
    }

    private void requestSeek(double seek, bool success)
    {
        if (PlaybackMode != BmsPreviewTrackPlaybackMode.Preview || !IsRunning)
        {
            cancelSeekFade();
            cancelRestoreFade();
            seekInternal(seek, success);
            return;
        }

        pendingSeekPosition = seek;
        pendingSeekSuccess = success;

        if (seekFadePhase == SeekFadePhase.FadingOut)
            return;

        if (seekFadeVolume.Value <= 0)
        {
            performPendingSeek();
            return;
        }

        seekFadeStartVolume = seekFadeVolume.Value;
        seekFadeStart = Stopwatch.GetTimestamp();
        seekFadePhase = SeekFadePhase.FadingOut;
    }

    private void updateSeekFade()
    {
        if (PlaybackMode != BmsPreviewTrackPlaybackMode.Preview && seekFadePhase != SeekFadePhase.None)
        {
            completePendingSeek();
            return;
        }

        switch (seekFadePhase)
        {
            case SeekFadePhase.FadingOut:
            {
                var progress = Stopwatch.GetElapsedTime(seekFadeStart).TotalMilliseconds / SEEK_FADE_OUT_DURATION;
                seekFadeVolume.Value = seekFadeStartVolume * Math.Max(0, 1 - progress);

                if (progress >= 1)
                    performPendingSeek();

                break;
            }

            case SeekFadePhase.FadingIn:
            {
                var progress = Stopwatch.GetElapsedTime(seekFadeStart).TotalMilliseconds / SEEK_FADE_IN_DURATION;
                seekFadeVolume.Value = Math.Min(1, progress);

                if (progress >= 1)
                    seekFadePhase = SeekFadePhase.None;

                break;
            }
        }
    }

    private void performPendingSeek()
    {
        if (pendingSeekPosition is not { } seek)
            return;

        var success = pendingSeekSuccess;
        pendingSeekPosition = null;
        seekFadeVolume.Value = 0;
        cancelRestoreFade();
        seekInternal(seek, success);
        seekFadePhase = SeekFadePhase.WaitingForAudio;
    }

    private void completePendingSeek()
    {
        if (pendingSeekPosition is { })
            performPendingSeek();

        cancelSeekFade();
    }

    private void cancelSeekFade()
    {
        pendingSeekPosition = null;
        seekFadePhase = SeekFadePhase.None;
        seekFadeVolume.Value = 1;
    }

    private void cancelRestoreFade()
    {
        IsRestoreFadePending = false;
        restoreFadeInProgress = false;
        restoreFadeVolume.Value = 1;
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

    private enum SeekFadePhase
    {
        None,
        FadingOut,
        WaitingForAudio,
        FadingIn,
    }
}
