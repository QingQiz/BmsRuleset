using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal partial class BmsPracticeScreen : BmsPracticeResultScreen
{
    public override bool? AllowGlobalTrackControl => false;
    public override bool? ApplyModTrackAdjustments => false;

    public event Action? AdjustRangeRequested;

    private readonly BmsPracticeSession session;
    private readonly BmsPracticeCountdown countdown = new();
    private WorkingBeatmap? originalBeatmap;
    private IReadOnlyList<Mod> originalMods = [];
    private CancellationTokenSource? loadingCancellation;
    private BmsPracticePlayer? pendingPlayer;
    private BmsPracticePlayer? activePlayer;
    private BmsPracticeAttemptResult? lastResult;
    private int loadGeneration;
    private bool inAttempt;
    private bool exitRequested;
    private bool settingsSaved;
    private bool loadFailed;
    private bool adjustAfterPlayer;
    private bool retryRequested;
    private bool switchingAttempt;
    private bool aggregateResultsRequested;
    private bool returningFromAggregate;
    private bool restartAfterAggregate;
    private OsuSpriteText title = null!;
    private OsuSpriteText status = null!;
    private OsuSpriteText preparationMessage = null!;
    private Container transitionContent = null!;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private Storage storage { get; set; } = null!;

    internal BmsPracticePlayer? PendingPlayer => pendingPlayer;
    internal BmsPracticeAttemptResult? LastResult => lastResult;
    internal BmsPracticeCountdown Countdown => countdown;
    protected virtual bool IsWindowActive => host.IsActive.Value;
    private int countInSeconds => session.Settings.SkipResults ? session.Settings.CountIn : 0;

    internal BmsPracticeScreen(BmsPracticeSession session)
    {
        this.session = session;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(session);
        return dependencies;
    }

    protected override BmsStatisticsPanel CreateStatisticsPanel() => new BmsPracticeStatisticsPanel(() => lastResult?.Statistics);

    protected override void LoadComplete()
    {
        base.LoadComplete();
        transitionContent = (Container)InternalChildren.Single();
        ResultsContent.Padding = new MarginPadding { Bottom = 30 };
        ResultsContent.Add(new Container
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Y = 30,
            RelativeSizeAxes = Axes.X,
            Height = 26,
            Padding = new MarginPadding { Horizontal = 12 },
            Children =
            [
                title = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                    Text = BmsStrings.Practice,
                },
                status = new OsuSpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Font = OsuFont.GetFont(size: 18),
                    Text = BmsStrings.PracticePreparing,
                },
            ],
        });
        ResultsContent.Add(preparationMessage = new OsuSpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Font = OsuFont.GetFont(size: 26),
            Text = BmsStrings.PracticePreparing,
        });
        host.IsActive.BindValueChanged(onWindowActiveChanged);
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        transitionContent.Hide();
        originalBeatmap = Beatmap.Value;
        originalMods = Mods.Value.Select(mod => mod.DeepClone()).ToArray();
        Schedule(prepareAttempt);
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);
        if (switchingAttempt)
        {
            switchingAttempt = false;
            inAttempt = false;
            pushPreparedPlayer(pendingPlayer!, activePlayer?.IsPauseMenuVisible == true);
            return;
        }
        inAttempt = false;
        activePlayer = null;
        countdown.Cancel();

        if (exitRequested)
            return;

        if (returningFromAggregate)
        {
            returningFromAggregate = false;
            if (restartAfterAggregate)
                Schedule(StartNextAttempt);
            else
                Schedule(AdjustRange);
            return;
        }

        if (aggregateResultsRequested && !adjustAfterPlayer && !retryRequested)
        {
            aggregateResultsRequested = false;
            cancelPreparation();
            if (session.Settings.SkipResults)
            {
                if (session.AttemptResults.Count == 0)
                    Schedule(AdjustRange);
                else
                {
                    returningFromAggregate = true;
                    this.Push(new BmsPracticeResultsScreen(session, restart => restartAfterAggregate = restart));
                }
                return;
            }
        }
        aggregateResultsRequested = false;

        if (adjustAfterPlayer)
        {
            adjustAfterPlayer = false;
            Schedule(AdjustRange);
            return;
        }

        if (retryRequested)
        {
            retryRequested = false;
            lastResult = null;
            clearResult();
            Schedule(prepareAttempt);
            return;
        }

        if (lastResult?.Attempt != session.Attempts)
        {
            lastResult = null;
            clearResult();
        }
        showLastResult();
        showResultActions();
    }

    public override bool OnBackButton()
    {
        AdjustRange();
        return true;
    }

    protected override void OnNextAttempt() => StartNextAttempt();

    protected override void OnReturnToSettings() => AdjustRange();

    public override bool OnExiting(ScreenExitEvent e)
    {
        exitRequested = true;
        cancelPreparation();
        if (originalBeatmap != null)
            Beatmap.Value = originalBeatmap;
        Mods.Value = originalMods;
        BmsWorkingBeatmap.RestoreActivePreview(null);
        return base.OnExiting(e);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
        {
            host.IsActive.ValueChanged -= onWindowActiveChanged;
            cancelPreparation();
        }
        base.Dispose(isDisposing);
    }

    private void advanceCountdown(double elapsed)
    {
        if (exitRequested || activePlayer?.IsCurrentScreen() != true || !countdown.IsReady)
            return;

        if (!IsWindowActive || activePlayer.IsPauseMenuVisible)
            countdown.Pause();
        countdown.Advance(elapsed);
        updateCountdownDisplay();
        if (countdown.HasFinished)
            beginPreparedAttempt();
    }

    protected virtual BmsPracticePlayer CreatePlayer() => new(session);

    internal void SkipCountdown()
    {
        countdown.Skip();
        if (countdown.HasFinished)
            beginPreparedAttempt();
    }

    internal void PauseCountdown()
    {
        countdown.Pause();
        updateCountdownDisplay();
    }

    internal void ResumeCountdown()
    {
        if (!IsWindowActive)
            return;
        activePlayer?.ResetPreparationTimestamp();
        countdown.Resume();
        updateCountdownDisplay();
    }

    internal void StartNextAttempt()
    {
        if (exitRequested || inAttempt || !IsWindowActive)
            return;
        if (activePlayer is { } prepared && prepared.IsCurrentScreen() && countdown.IsReady)
        {
            if (countdown.IsPaused)
            {
                countdown.Ready(countInSeconds, false);
                prepared.ResetPreparationTimestamp();
                updateCountdownDisplay();
            }
            return;
        }
        if (loadingCancellation == null && pendingPlayer == null && this.IsCurrentScreen())
            prepareAttempt();
    }

    internal void AdjustRange()
    {
        if (exitRequested)
            return;
        if (activePlayer is { } player && player.IsCurrentScreen())
        {
            adjustAfterPlayer = true;
            cancelPreparation();
            player.Exit();
            return;
        }
        exitRequested = true;
        cancelPreparation();
        AdjustRangeRequested?.Invoke();
        this.Exit();
    }

    private void prepareAttempt() => prepareAttempt(null);

    private void prepareAttempt(BmsPracticePlayer? outgoing)
    {
        if (exitRequested || (outgoing == null ? !this.IsCurrentScreen() : !outgoing.IsCurrentScreen()) || inAttempt)
            return;

        cancelPreparation();
        var generation = ++loadGeneration;
        var cancellation = loadingCancellation = new CancellationTokenSource();
        var cancellationToken = cancellation.Token;
        loadFailed = false;
        clearResult();
        status.Text = BmsStrings.PracticePreparing;
        title.Text = BmsStrings.PracticeAttempt(session.Attempts + 1);
        preparationMessage.Text = BmsStrings.PracticePreparing;
        NextAttemptButton.Enabled.Value = false;
        outgoing?.ShowPreparation();

        BmsWorkingBeatmap.SwitchActivePreviewToGameplayClockOnly();
        Beatmap.Value = session.CreateWorkingBeatmap(audio);
        Mods.Value = session.Mods;

        var player = pendingPlayer = CreatePlayer();
        player.AttemptStarted += saveSettings;
        player.Prepared += () =>
        {
            if (exitRequested || activePlayer != player)
                return;
            countdown.Ready(countInSeconds, !IsWindowActive || player.IsPauseMenuVisible);
            player.ResetPreparationTimestamp();
            updateCountdownDisplay();
        };
        player.PreparationAdvanced += advanceCountdown;
        player.PreparationPaused += PauseCountdown;
        player.PreparationResumed += StartNextAttempt;
        player.PreparationRestartRequested += StartNextAttempt;
        player.RetryRequested += () => retryRequested = true;
        player.AttemptCompleted += result =>
        {
            session.AddAttemptResult(result);
            lastResult = result;
            inAttempt = false;
            if (session.Settings.SkipResults)
                prepareAttempt(player);
        };
        player.UserExitRequested += () => aggregateResultsRequested = true;
        // The controller is suspended during automatic repeats; its scheduler stops updating.
        _ = observeLoad(LoadComponentAsync(player, ready =>
        {
            if (exitRequested || cancellation.IsCancellationRequested || generation != loadGeneration)
                return;

            loadingCancellation?.Dispose();
            loadingCancellation = null;
            if (!ready.LoadedBeatmapSuccessfully)
            {
                failLoad();
                return;
            }

            if (outgoing != null)
            {
                switchingAttempt = true;
                outgoing.ExitForNextAttempt();
            }
            else
                pushPreparedPlayer(ready, false);
        }, cancellationToken, outgoing?.PreparationScheduler), player, generation, cancellationToken, outgoing);
    }

    private void pushPreparedPlayer(BmsPracticePlayer player, bool paused)
    {
        player.PrepareForCountdown();
        pendingPlayer = null;
        activePlayer = player;
        this.Push(player);
        if (paused)
            player.Pause();
    }

    private async Task observeLoad(Task task, BmsPracticePlayer player, int generation, CancellationToken cancellationToken, BmsPracticePlayer? outgoing)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to load a BMS practice attempt.");
            (outgoing?.PreparationScheduler ?? Scheduler).Add(() =>
            {
                if (generation == loadGeneration && !exitRequested)
                    failLoad();
            });
        }
        finally
        {
            // Framework cancellation can complete its continuation successfully without invoking onLoaded.
            // The returned task still guarantees background loading has stopped before releasing resources.
            if (cancellationToken.IsCancellationRequested && player.Parent == null && !player.IsPracticeDisposed)
                player.Dispose();
        }
    }

    private void beginPreparedAttempt()
    {
        if (activePlayer == null || exitRequested || !activePlayer.IsCurrentScreen() || !countdown.HasFinished)
            return;

        countdown.Cancel();
        inAttempt = true;
        activePlayer.BeginPreparedAttempt();
    }

    private void cancelPreparation()
    {
        loadGeneration++;
        countdown.Cancel();
        var cancellation = loadingCancellation;
        loadingCancellation = null;
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (pendingPlayer is { LoadState: >= LoadState.Ready, IsPracticeDisposed: false, Parent: null } ready)
            ready.Dispose();
        pendingPlayer = null;
    }

    private void failLoad()
    {
        cancelPreparation();
        loadFailed = true;
        status.Text = BmsStrings.PracticeLoadingFailed;
        if (activePlayer?.IsCurrentScreen() == true)
        {
            activePlayer.ExitForNextAttempt();
            return;
        }
        showResultActions();
    }

    private void showLastResult()
    {
        if (lastResult == null)
        {
            title.Text = BmsStrings.PracticeAborted;
            preparationMessage.Text = BmsStrings.PracticeAborted;
            return;
        }

        title.Text = BmsStrings.PracticeAttempt(lastResult.Attempt);
        preparationMessage.Hide();
        SelectedScore.Value = lastResult.Score;
        StatisticsPanel.Show();
    }

    private void clearResult()
    {
        transitionContent.Hide();
        SelectedScore.Value = null;
        StatisticsPanel.Hide();
        preparationMessage.Show();
    }

    private void showResultActions()
    {
        transitionContent.Show();
        if (!loadFailed)
            status.Text = lastResult == null ? BmsStrings.PracticeAborted : BmsStrings.PracticeCompleted;
        NextAttemptButton.Enabled.Value = true;
    }

    private void updateCountdownDisplay()
    {
        if (!countdown.IsReady)
            return;
        activePlayer?.ShowCountdown(countdown);
    }

    private void onWindowActiveChanged(ValueChangedEvent<bool> active)
    {
        if (!active.NewValue && countdown.IsReady)
            PauseCountdown();
    }

    private void saveSettings()
    {
        if (settingsSaved)
            return;
        settingsSaved = true;
        new BmsPracticeSettingsStore(storage).Save(session.Source.BeatmapInfo.Hash, session.Settings);
    }

}
