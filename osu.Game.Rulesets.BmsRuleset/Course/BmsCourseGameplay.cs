using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Result.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.Course;

internal partial class BmsCourseSessionScreen : ScreenWithBeatmapBackground
{
    public override bool AllowUserExit => false;

    private readonly BmsCourseSession session;
    private readonly WorkingBeatmap originalBeatmap;
    private readonly IReadOnlyList<Mod> originalMods;
    private bool stageStarted;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    internal BmsCourseSessionScreen(BmsCourseSession session, WorkingBeatmap originalBeatmap, IReadOnlyList<Mod> originalMods)
    {
        this.session = session;
        this.originalBeatmap = originalBeatmap;
        this.originalMods = originalMods.Select(mod => mod.DeepClone()).ToArray();
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        if (!stageStarted)
            Schedule(startCurrentStage);
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);
        stageStarted = false;

        if (session.AdvanceRequested)
        {
            session.Advance();
            Schedule(startCurrentStage);
            return;
        }

        if (session.CanContinue && session.CurrentStage.Status == BmsCourseStageStatus.Playing)
            session.AbortCurrentStageWithoutScore();

        if (session.Status == BmsCourseStatus.InProgress)
            return;

        if (session.Status == BmsCourseStatus.Failed
            && !session.SummaryShown
            && session.HasNoFail
            && session.CurrentStage.Status == BmsCourseStageStatus.Failed
            && session.CurrentStageIndex < session.Stages.Count - 1)
        {
            this.Push(createStageResults());
            return;
        }

        if (session.SummaryShown)
        {
            this.Exit();
            return;
        }

        session.SummaryShown = true;
        this.Push(new BmsCourseResultsScreen(session));
    }

    private BmsCourseStageResultsScreen createStageResults()
    {
        var score = session.CurrentStage.Score
                    ?? throw new InvalidOperationException("A stage result requires a stage score.");

        return new BmsCourseStageResultsScreen(
            score,
            () => session.RequestAdvance(),
            () => session.AbortAfterStageResult());
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        Beatmap.Value = originalBeatmap;
        Mods.Value = originalMods;
        return base.OnExiting(e);
    }

    private void startCurrentStage()
    {
        if (!this.IsCurrentScreen() || stageStarted || !session.CanContinue)
            return;

        stageStarted = true;
        session.BeginCurrentStage();
        Beatmap.Value = beatmaps.GetWorkingBeatmap(session.CurrentStage.Stage.Beatmap, true);
        Mods.Value = session.Mods;
        this.Push(new PlayerLoader(() => new BmsCoursePlayer(session)));
    }
}

internal partial class BmsCoursePlayer : SoloPlayer
{
    private readonly BmsCourseSession session;

    internal BmsCoursePlayer(BmsCourseSession session)
        : base(new PlayerConfiguration
        {
            AllowPause = false,
            AllowRestart = false,
            ShowLeaderboard = false,
        })
    {
        this.session = session;
        Configuration.AllowRestart = false;
        Configuration.ShowLeaderboard = false;
    }

    [BackgroundDependencyLoader]
    private void loadCourseGaugeContext()
    {
        if (GameplayState.HealthProcessor is Scoring.BmsHealthProcessor healthProcessor)
            session.ConfigureHealthProcessor(healthProcessor);

        if (session.HasNoSpeedConstraint && DrawableRuleset.Playfield is UI.BmsPlayfield playfield)
            playfield.ScrollController.LockScrollSpeedMultiplier();
    }

    protected override ResultsScreen CreateResults(ScoreInfo score)
    {
        session.CompleteCurrentStage(score, currentGaugeStates);

        if (session.CurrentStageIndex < session.Stages.Count - 1 && session.CanContinue)
            return createStageResults(score);

        session.SummaryShown = true;
        return new BmsCourseResultsScreen(session);
    }

    protected override async void ConcludeFailedScore(Score score)
    {
        try
        {
            base.ConcludeFailedScore(score);

            if (session.CanContinue && session.CurrentStage.Status == BmsCourseStageStatus.Playing)
            {
                ScoreProcessor.PopulateScore(score.ScoreInfo);
                score.ScoreInfo.Date = DateTimeOffset.Now;
                DrawableRuleset.SetRecordTarget(null);
                var gaugeStates = currentGaugeStates;

                try
                {
                    await ImportScore(score).ConfigureAwait(false);
                }
                finally
                {
                    Scheduler.Add(() =>
                    {
                        session.FailCurrentStage(score.ScoreInfo, gaugeStates);
                        Schedule(this.Exit);
                    });
                }

                return;
            }

            Schedule(this.Exit);
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to conclude a failed BMS course score.");
        }
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (session.CanContinue && session.CurrentStage.Status == BmsCourseStageStatus.Playing)
        {
            ScoreProcessor.PopulateScore(Score.ScoreInfo);
            ScoreProcessor.FailScore(Score.ScoreInfo);
            Score.ScoreInfo.Date = DateTimeOffset.Now;
            session.AbortCurrentStage(Score.ScoreInfo, currentGaugeStates);
        }

        return base.OnExiting(e);
    }

    private IReadOnlyList<Scoring.Gauge.BmsGaugeStateSnapshot> currentGaugeStates =>
        ((Scoring.BmsHealthProcessor)GameplayState.HealthProcessor).CurrentGaugeStates;

    private BmsCourseStageResultsScreen createStageResults(ScoreInfo? score = null)
    {
        score ??= session.CurrentStage.Score
                  ?? throw new InvalidOperationException("A stage result requires a stage score.");

        return new BmsCourseStageResultsScreen(
            score,
            () => session.RequestAdvance(),
            () => session.AbortAfterStageResult());
    }
}
