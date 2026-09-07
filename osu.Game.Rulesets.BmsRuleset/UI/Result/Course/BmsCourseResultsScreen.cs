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
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Course;

internal partial class BmsCourseResultsScreen : BmsResultsScreen
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
        : base(null)
    {
        this.session = session;
        this.recordResult = recordResult;
        BackButtonVisibility.Value = false;
        AllowWatchingReplay = false;
        AllowRetry = false;
    }

    protected override BmsStatisticsPanel CreateStatisticsPanel() => new(false);

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

        var layout = courseLayout = new BmsCourseResultsLayout(session, aggregateScore, selectedStage);
        LoadComponentAsync(layout, ResultsContent.Add);
        selectedStage.BindValueChanged(selectionChanged, true);
    }

    protected override Drawable[] CreateResultControls()
    {
        BottomPanel.Name = "Course result controls";
        return
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

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e))
            return true;

        cancelStageBeatmapLoad();
        courseLayout.AggregateStatistics.CancelLoading();
        return false;
    }

    protected override void Dispose(bool isDisposing)
    {
        cancelStageBeatmapLoad();
        selectedStage.ValueChanged -= selectionChanged;
        base.Dispose(isDisposing);
    }

    private void cancelStageBeatmapLoad()
    {
        stageBeatmapLoadCancellation?.Cancel();
        stageBeatmapLoadCancellation?.Dispose();
        stageBeatmapLoadCancellation = null;
    }

    private void selectionChanged(ValueChangedEvent<int?> selection)
    {
        cancelStageBeatmapLoad();

        if (!selection.NewValue.HasValue)
        {
            SelectedScore.Value = null;
            StatisticsPanel.Hide();
            courseLayout.AggregateStatistics.Show();
            courseLayout.ShowScore(null);
            Beatmap.Value = summaryBeatmap;
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
            StatisticsPanel.Show();
        });
    }
}
