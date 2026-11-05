using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Localisation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Objects;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal partial class BmsPracticePlayer : Player
{
    private readonly BmsPracticeSession session;
    private readonly List<BmsGaugeHistoryEvent> gaugeHistory = [];
    private readonly CancellationTokenSource preparationCancellation = new();
    private readonly BindableBool visualBreakTime = new();
    private bool completionPending;
    private bool attemptStarted;
    private bool exiting;
    private long previousTimestamp;
    private double practiceEndTime;
    private BmsPracticeCountInOverlay countInOverlay = null!;
    private bool instantExit;
    private bool internalExit;
    private bool retainForRepeat;
    private bool revealWhenPrepared;
    private OsuSpriteText? progress;

    protected override bool PlayExitSound => !instantExit && base.PlayExitSound;
    protected override bool ShouldBeAlive => retainForRepeat || base.ShouldBeAlive;

    internal event Action? AttemptStarted;
    internal event Action<BmsPracticeAttemptResult>? AttemptCompleted;
    internal event Action? Prepared;
    internal event Action<double>? PreparationAdvanced;
    internal event Action? PreparationPaused;
    internal event Action? PreparationResumed;
    internal event Action? PreparationRestartRequested;
    internal event Action? RetryRequested;
    internal event Action? UserExitRequested;
    internal BmsDrawableRuleset PracticeRuleset => (BmsDrawableRuleset)DrawableRuleset;
    internal bool HasAttemptStarted => attemptStarted;
    internal bool IsPracticeDisposed => IsDisposed;
    internal BmsPracticeCountInOverlay CountInOverlay => countInOverlay;
    internal Scheduler PreparationScheduler => Scheduler;
    internal bool IsPauseMenuVisible => PauseOverlay.State.Value == Visibility.Visible;

    internal void ResetPreparationTimestamp() => previousTimestamp = Stopwatch.GetTimestamp();

    internal BmsPracticePlayer(BmsPracticeSession session)
        : base(new PlayerConfiguration
        {
            ShowResults = false,
            ShowLeaderboard = false,
            ShowFailingOverlay = false,
            AllowRestart = true,
            AllowSkipping = false,
            AllowUserInteraction = false,
        })
    {
        this.session = session;
    }

    protected override void PrepareReplay() => DrawableRuleset.SetRecordTarget(null);

    protected override Task ImportScore(Score score) => Task.CompletedTask;

    protected override bool CheckModsAllowFailure() => false;

    // Attempts have separate working tracks but share one background for the entire session.
    protected override BackgroundScreen CreateBackground() => session.Settings.SkipResults
        ? new BmsPracticeBackgroundScreen(session.Source)
        : new BackgroundScreenBeatmap(session.Source);

    protected override GameplayClockContainer CreateGameplayClockContainer(WorkingBeatmap beatmap, double gameplayStart)
        => new PracticeGameplayClockContainer(beatmap, gameplayStart);

    protected override ResultsScreen CreateResults(ScoreInfo score) => throw new InvalidOperationException("Practice results belong to the practice session.");

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (!LoadedBeatmapSuccessfully)
            return;

        HUDOverlay.HoldToQuit.Action = () => Pause();

        // Keep normal HUD pause behaviour while routing every native restart through the practice controller.
        PrepareLoaderForRestart = _ =>
        {
            RetryRequested?.Invoke();
            internalExit = true;
        };
        var retry = PauseOverlay.Buttons.Single(button => button.Text.Equals(GameplayMenuOverlayStrings.Retry));
        retry.Text = BmsStrings.PracticeRetry;
        retry.Action = RequestRetry;
        PauseOverlay.Buttons.First().Action = () =>
        {
            if (attemptStarted && !completionPending)
                Resume();
            else
            {
                PauseOverlay.Hide();
                PreparationResumed?.Invoke();
            }
        };
        PauseOverlay.Buttons.Last().Action = () => this.Exit();
        ScoreProcessor.HasCompleted.BindValueChanged(onCompleted);
        // Share the native overlay layer so the pause menu also covers the practice count-in.
        ((Container)PauseOverlay.Parent!).Add(PracticeRuleset.CreatePlayfieldAdjustmentContainer().With(container =>
        {
            container.Depth = 1;
            container.Child = countInOverlay = new BmsPracticeCountInOverlay();
        }));
        AddInternal(new OsuSpriteText
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = 12,
            Text = BmsStrings.PracticeAttempt(session.Attempts + 1),
            Font = OsuFont.GetFont(size: 18),
        });
        AddInternal(progress = new OsuSpriteText
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = 36,
            Font = OsuFont.GetFont(size: 14),
        });
    }

    internal void RequestRetry()
    {
        if (!attemptStarted || exiting || completionPending)
            return;

        Restart();
    }

    internal void PrepareForCountdown()
    {
        GameplayClockContainer.Stop();
        practiceEndTime = Math.Max(session.Settings.EndTime, DrawableRuleset.Objects.Select(hit => hit.GetEndTime())
            .DefaultIfEmpty(session.Settings.EndTime).Max());
        SetGameplayStartTime(computeStartTime());
        PracticeRuleset.KeyBindingInputManager.UseParentInput = false;
        GameplayClockContainer.AlwaysPresent = true;
        GameplayClockContainer.Show();
    }

    internal void ShowPreparation() => countInOverlay.ShowPreparation();

    internal void ShowCountdown(BmsPracticeCountdown countdown) => countInOverlay.Display(countdown);

    internal void ExitForNextAttempt(bool retainPresentation = false)
    {
        retainForRepeat = retainPresentation;
        instantExit = true;
        internalExit = true;
        this.Exit();
    }

    internal void WaitForPreparedPresentation()
    {
        revealWhenPrepared = true;
        AlwaysPresent = true;
    }

    internal void ReleasePresentation()
    {
        retainForRepeat = false;
        this.Hide();
    }

    private double computeStartTime()
    {
        var playfield = (BmsPlayfield)DrawableRuleset.Playfield;
        var lead = BmsDrawableRuleset.ComputeScrollTime(playfield.ScrollController.ScrollSpeed)
                   * playfield.ScrollController.ScrollRangeScale * playfield.ScrollController.PlaybackRate;
        var earliestVisualEntry = playfield.Beatmap.HitObjects.Where(hit => hit is not BmsInvisibleNote)
            .Select(hit => new BmsHitObjectLifetimeEntry(hit, playfield.ScrollController, () => playfield.VisualOffset.Value).GetPracticeVisibilityStart())
            .DefaultIfEmpty(session.Settings.StartTime).Min();
        return Math.Min(session.Settings.StartTime - Math.Max(100, lead), earliestVisualEntry);
    }

    protected override void StartGameplay()
    {
        PrepareForCountdown();
        ResetPreparationTimestamp();
        ScheduleAfterChildren(prepareGameplayFrame);
    }

    private void prepareGameplayFrame()
    {
        if (exiting || !this.IsCurrentScreen())
            return;

        if (!PracticeRuleset.UpdatePracticePreparation() || !prepareHudLayout())
        {
            ScheduleAfterChildren(prepareGameplayFrame);
            return;
        }

        // Skin loading can change the visible scrolling range and its required lead-in.
        var start = computeStartTime();
        if (Math.Abs(GameplayClockContainer.StartTime - start) >= 0.001)
        {
            SetGameplayStartTime(start);
            ScheduleAfterChildren(prepareGameplayFrame);
            return;
        }

        _ = finishPreparingSamples(start);
    }

    private async Task finishPreparingSamples(double start)
    {
        var samples = (BmsPracticeSamplePlayback)PracticeRuleset.SamplePlayback;
        try
        {
            await samples.PrepareAsync(start, session.SampleDurations, preparationCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            BmsLogger.LogAudioFailure("Failed to prepare BMS practice startup buffers.", exception);
        }

        Schedule(() =>
        {
            if (exiting || !this.IsCurrentScreen())
                return;

            if (samples.IsPreparationSuccessful)
                samples.ApplyPreparedSamples();

            ScheduleAfterChildren(() => completePreparation(start));
        });
    }

    private void completePreparation(double preparedStart)
    {
        if (exiting || !this.IsCurrentScreen())
            return;

        if (!PracticeRuleset.UpdatePracticePreparation() || !prepareHudLayout())
        {
            ScheduleAfterChildren(() => completePreparation(preparedStart));
            return;
        }

        // A skin or scrolling change during preparation can move the actual audio start.
        if (Math.Abs(computeStartTime() - preparedStart) >= 0.001)
        {
            SetGameplayStartTime(computeStartTime());
            ScheduleAfterChildren(prepareGameplayFrame);
            return;
        }
        if (revealWhenPrepared)
        {
            revealWhenPrepared = false;
            Alpha = 1;
            AlwaysPresent = false;
        }
        previousPracticeLayout = [];
        Prepared?.Invoke();
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        if (session.Settings.SkipResults)
        {
            ((Bindable<bool>)DimmableStoryboard.IsBreakTime).UnbindBindings();
            DimmableStoryboard.IsBreakTime.BindTo(visualBreakTime);
            IsBreakTime.BindValueChanged(_ => updateVisualBreakTime());
            ApplyToBackground(background => ((BmsPracticeBackgroundScreen)background).BindVisualBreakTime(visualBreakTime));
        }
        this.ClearTransforms();
        Alpha = revealWhenPrepared ? 0 : 1;
        Scale = Vector2.One;
        GameplayClockContainer.ClearTransforms();
        GameplayClockContainer.Alpha = 1;
        ShowPreparation();
    }

    internal void BeginPreparedAttempt()
    {
        if (attemptStarted || exiting || !this.IsCurrentScreen())
            return;
        attemptStarted = true;
        SetGameplayStartTime(computeStartTime());
        countInOverlay.Hide();
        GameplayClockContainer.Show();

        session.BeginAttempt();
        recordGaugeState();
        AttemptStarted?.Invoke();
        PracticeRuleset.KeyBindingInputManager.UseParentInput = true;
        GameplayClockContainer.Start();
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (session.Settings.SkipResults && !exiting)
            updateVisualBreakTime();
        if ((!attemptStarted || completionPending) && !exiting)
        {
            // Count-in is real time, independent of both gameplay rate and an accelerated parent/test clock.
            var now = Stopwatch.GetTimestamp();
            var elapsed = previousTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(previousTimestamp, now).TotalMilliseconds;
            previousTimestamp = now;
            PreparationAdvanced?.Invoke(elapsed);
        }
        if (!LoadedBeatmapSuccessfully || progress == null)
            return;

        var duration = practiceEndTime - session.Settings.StartTime;
        progress.Text = BmsStrings.PracticeProgress(Math.Clamp(GameplayClockContainer.CurrentTime - session.Settings.StartTime, 0, duration), duration);
        if (!completionPending)
            recordGaugeState();
    }

    private void updateVisualBreakTime()
        => visualBreakTime.Value = attemptStarted && !completionPending
                                                && GameplayClockContainer.CurrentTime >= DrawableRuleset.GameplayStartTime
                                                && IsBreakTime.Value;

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (!internalExit)
            UserExitRequested?.Invoke();

        exiting = true;
        preparationCancellation.Cancel();
        stopPracticeAudio();
        var blocked = base.OnExiting(e);
        if (instantExit)
        {
            // Player's normal exit bypasses user dim settings; a repeat stays in gameplay throughout.
            ApplyToBackground(background => background.IgnoreUserSettings.Value = false);
            this.ClearTransforms();
            Alpha = retainForRepeat ? 1 : 0;
        }
        return blocked;
    }

    public override bool Pause()
    {
        if (attemptStarted && !completionPending)
            return base.Pause();
        PreparationPaused?.Invoke();
        GameplayClockContainer.Stop();
        PauseOverlay.Show();
        return true;
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if ((!attemptStarted || completionPending) && e.Key is Key.Enter or Key.KeypadEnter)
        {
            if (!e.Repeat)
                PreparationRestartRequested?.Invoke();
            return true;
        }
        return base.OnKeyDown(e);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (IsDisposed)
            return;

        if (isDisposing)
        {
            preparationCancellation.Cancel();
            preparationCancellation.Dispose();
        }
        if (LoadedBeatmapSuccessfully)
            ScoreProcessor.HasCompleted.ValueChanged -= onCompleted;
        base.Dispose(isDisposing);
    }

    private void onCompleted(ValueChangedEvent<bool> completed)
    {
        if (!completed.NewValue || completionPending || !attemptStarted)
            return;

        completionPending = true;
        stopPracticeAudio();
        // Switching the global beatmap for preloading can reset its old track; keep the completed field frozen.
        if (session.Settings.SkipResults)
            ((PracticeGameplayClockContainer)GameplayClockContainer).FreezeForRepeat();
        PracticeRuleset.KeyBindingInputManager.UseParentInput = false;
        ((BmsHealthProcessor)HealthProcessor).HasPassedAtEnd();
        ScoreProcessor.PopulateScore(Score.ScoreInfo);
        recordGaugeState();
        var result = BmsPracticeAttemptResult.Create(session.Attempts, Score.ScoreInfo, (BmsHealthProcessor)HealthProcessor, gaugeHistory,
            (BmsBeatmap)GameplayState.Beatmap, session.Settings, ((BmsScoreProcessor)ScoreProcessor).JudgementEvents);

        // Leave the frame-stable judgement traversal before replacing the entire Player.
        Schedule(() =>
        {
            if (!this.IsCurrentScreen())
                return;

            AttemptCompleted?.Invoke(result);
            if (!session.Settings.SkipResults)
            {
                internalExit = true;
                this.Exit();
            }
        });
    }

    private void stopPracticeAudio()
    {
        if (!LoadedBeatmapSuccessfully)
            return;

        GameplayClockContainer.Stop();
        PracticeRuleset.KeyBindingInputManager.UseParentInput = false;
        PracticeRuleset.SamplePlayback.StopAll();
    }

    private void recordGaugeState()
    {
        var health = (BmsHealthProcessor)HealthProcessor;
        var states = health.CurrentGaugeStates;
        if (gaugeHistory.LastOrDefault() is { } last && last.ActiveGaugeType == health.GaugeType && last.States.SequenceEqual(states))
            return;

        gaugeHistory.Add(new BmsGaugeHistoryEvent(GameplayClockContainer.CurrentTime, health.GaugeType, states.ToArray()));
    }

    private partial class PracticeGameplayClockContainer(WorkingBeatmap beatmap, double gameplayStart) : MasterGameplayClockContainer(beatmap, gameplayStart)
    {
        internal void FreezeForRepeat()
        {
            var time = CurrentTime;
            StopUsingBeatmapClock();
            // Source replacement uses raw track time; restore beatmap time with offsets applied once.
            GameplayClock.Seek(time);
        }
    }

}
