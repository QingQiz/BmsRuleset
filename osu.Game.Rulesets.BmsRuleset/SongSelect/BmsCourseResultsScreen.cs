using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseResultsScreen : ResultsScreen
{
    internal int? SelectedStageIndex => selectedStage.Value;

    private readonly BmsCourseSession session;
    private readonly bool recordResult;
    private readonly Bindable<int?> selectedStage = new();

    private BmsCourseResultsLayout courseLayout = null!;
    private WorkingBeatmap summaryBeatmap = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

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

        var aggregateScore = BmsCourseResultPresentation.CreateAggregateScore(session);
        if (recordResult)
            BmsRulesetRuntime.CourseResults?.Record(session.Course.Id, session.Status, aggregateScore.Rank, aggregateScore, BmsCourseAttemptData.From(session));

        summaryBeatmap = Beatmap.Value;

        foreach (var score in session.Stages.Select(stage => stage.Score).Where(score => score != null && !score.Equals(Score)))
            ScorePanelList.AddScore(score!);

        hideNativeScorePanels();

        StatisticsPanel.Hide();
        SelectedScore.Value = null;

        AddInternal(courseLayout = new BmsCourseResultsLayout(session, selectedStage));
        AddInternal(new InputBlockingContainer
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            Height = 50,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = OsuColour.Gray(0.2f),
                },
                new RoundedButton
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Width = 260,
                    Height = 40,
                    Margin = new MarginPadding(5),
                    Text = BmsStrings.ReturnToCourseSelect,
                    Action = this.Exit,
                },
            ],
        });

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
        selectedStage.ValueChanged -= selectionChanged;
        base.Dispose(isDisposing);
    }

    private void selectionChanged(ValueChangedEvent<int?> selection)
    {
        if (!selection.NewValue.HasValue)
        {
            SelectedScore.Value = null;
            StatisticsPanel.Hide();
            courseLayout.AggregateStatistics.Show();
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
        Beatmap.Value = beatmaps.GetWorkingBeatmap(attempt.Stage.Beatmap, true);
        SelectedScore.Value = attempt.Score;
        StatisticsPanel.Show();
        Schedule(hideNativeScorePanels);
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
            return playedScore.DeepClone();

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
