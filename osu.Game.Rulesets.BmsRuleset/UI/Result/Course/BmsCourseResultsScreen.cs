using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseResultsScreen : ResultsScreen
{
    internal int? SelectedStageIndex => selectedStage.Value;

    private readonly BmsCourseSession session;
    private readonly bool recordResult;
    private readonly Bindable<int?> selectedStage = new();

    private BmsCourseResultsLayout courseLayout = null!;
    private WorkingBeatmap summaryBeatmap = null!;
    private CancellationTokenSource? stageBeatmapLoadCancellation;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    [Resolved]
    private ScoreManager scores { get; set; } = null!;

    internal BmsCourseResultsScreen(BmsCourseSession session, bool recordResult = true)
        : base(createBackingScore(session))
    {
        this.session = session;
        this.recordResult = recordResult;
        BackButtonVisibility.Value = false;
        AllowWatchingReplay = false;
        AllowRetry = false;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Both columns need restored replay timing before building their summaries.
        foreach (var stage in session.Stages)
        {
            if (stage.Score != null)
                BmsReplayPatcher.RestoreScoreData(scores, stage.Score);
        }

        var aggregateScore = BmsCourseScoreAggregation.CreateScore(session);
        if (recordResult)
        {
            var finalGaugeType = session.CurrentGaugeStates.FirstOrDefault(state => !state.Failed)?.GaugeType;
            var store = BmsRulesetRuntime.CourseResults;
            if (store != null)
                _ = store.RecordAsync(session.Course.Id, session.Status, aggregateScore.Rank, aggregateScore, BmsCourseAttemptData.From(session), finalGaugeType);
        }

        summaryBeatmap = Beatmap.Value;

        foreach (var score in session.Stages.Select(stage => stage.Score).Where(score => score != null && !score.Equals(Score)))
            ScorePanelList.AddScore(score!);

        hideNativeScorePanels();

        StatisticsPanel.Hide();
        SelectedScore.Value = null;

        var layout = courseLayout = new BmsCourseResultsLayout(session, aggregateScore, selectedStage);
        LoadComponentAsync(layout, VerticalScrollContent.Add);
        var bottomPanel = BmsResultsScreenPatcher.GetBottomPanel(this);
        bottomPanel.Name = "Course result controls";
        bottomPanel.Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = OsuColour.Gray(0.2f),
            },
            new BmsCourseResultButton
            {
                Name = "Return to course select button",
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = BmsStrings.ReturnToCourseSelect,
                Icon = OsuIcon.LeftCircle,
                BackgroundColour = colours.Pink,
                HoverColour = colours.PinkDark,
                Action = this.Exit,
            },
        ];

        selectedStage.BindValueChanged(selectionChanged, true);
    }

    public override bool OnBackButton()
    {
        if (selectedStage.Value.HasValue)
        {
            selectedStage.Value = null;
            return true;
        }

        return base.OnBackButton();
    }

    protected override Task<ScoreInfo[]> FetchScores() => Task.FromResult<ScoreInfo[]>([]);

    protected override void Dispose(bool isDisposing)
    {
        stageBeatmapLoadCancellation?.Cancel();
        stageBeatmapLoadCancellation?.Dispose();
        stageBeatmapLoadCancellation = null;
        selectedStage.ValueChanged -= selectionChanged;
        base.Dispose(isDisposing);
    }

    private void selectionChanged(ValueChangedEvent<int?> selection)
    {
        stageBeatmapLoadCancellation?.Cancel();
        stageBeatmapLoadCancellation?.Dispose();
        stageBeatmapLoadCancellation = null;

        if (!selection.NewValue.HasValue)
        {
            SelectedScore.Value = null;
            StatisticsPanel.Hide();
            courseLayout.AggregateStatistics.Show();
            courseLayout.ShowScore(null);
            Beatmap.Value = summaryBeatmap;
            Schedule(hideNativeScorePanels);
            return;
        }

        var attempt = session.Stages[selection.NewValue.Value];
        if (attempt.Score == null)
        {
            selectedStage.Value = null;
            return;
        }

        courseLayout.AggregateStatistics.Hide();
        StatisticsPanel.Hide();

        var stageIndex = selection.NewValue.Value;
        var cancellation = stageBeatmapLoadCancellation = new CancellationTokenSource();
        _ = loadStageBeatmapAsync(stageIndex, attempt, cancellation.Token);
    }

    private async Task loadStageBeatmapAsync(int stageIndex, BmsCourseStageAttempt attempt, CancellationToken cancellationToken)
    {
        WorkingBeatmap workingBeatmap;

        try
        {
            workingBeatmap = await Task.Run(() => beatmaps.GetWorkingBeatmap(attempt.Stage.Beatmap, true), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, $"Failed to load BMS course stage beatmap: {exception.Message}");
            return;
        }

        Schedule(() =>
        {
            if (IsDisposed || cancellationToken.IsCancellationRequested || selectedStage.Value != stageIndex)
                return;

            Beatmap.Value = workingBeatmap;
            SelectedScore.Value = attempt.Score;
            courseLayout.ShowScore(attempt.Score);
            // ScorePanelList is still bound to SelectedScore by ResultsScreen and may update its
            // hidden native panel during this change. Show after those bindings settle.
            Schedule(() =>
            {
                if (!IsDisposed && !cancellationToken.IsCancellationRequested && selectedStage.Value == stageIndex)
                    StatisticsPanel.Show();
            });
            Schedule(hideNativeScorePanels);
        });
    }

    private void hideNativeScorePanels()
    {
        ScorePanelList.Hide();
        ScorePanelList.HandleInput = false;

        foreach (var panel in ScorePanelList.GetScorePanels())
            panel.Hide();
    }

    private static ScoreInfo createBackingScore(BmsCourseSession session)
    {
        var playedScore = session.Stages.Select(stage => stage.Score).FirstOrDefault(score => score != null);
        if (playedScore != null)
            return BmsScoreGaugeHistoryStore.Clone(playedScore);

        var beatmap = session.Stages[0].Stage.Beatmap;
        return new ScoreInfo
        {
            User = new APIUser(),
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = beatmap.Ruleset,
            Passed = false,
            Mods = session.Mods.Select(mod => mod.DeepClone()).ToArray(),
        };
    }
}
