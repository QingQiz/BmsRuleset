using System;
using osu.Framework.Bindables;
using osu.Framework.Screens;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;

internal partial class BmsPracticeResultsScreen : BmsPracticeResultScreen
{
    private readonly BmsPracticeSession session;
    private readonly Action<bool> onClosed;
    private readonly Bindable<BmsPracticeAttemptResult?> selectedAttempt = new();
    private readonly ScoreInfo aggregate;
    private BmsResultStatisticsData? aggregateStatistics;
    private bool restart;

    internal BmsPracticeResultsScreen(BmsPracticeSession session, Action<bool> onClosed)
    {
        this.session = session;
        this.onClosed = onClosed;
        aggregate = BmsPracticeScoreAggregation.CreateScore(session);
    }

    protected override BmsStatisticsPanel CreateStatisticsPanel() => new BmsPracticeStatisticsPanel(
        () => selectedAttempt.Value?.Statistics ?? (aggregateStatistics ??= BmsPracticeScoreAggregation.CreateStatistics(session)),
        _ => new BmsPracticeOverviewHeader(session, aggregate, selectedAttempt));

    protected override void LoadComplete()
    {
        base.LoadComplete();
        selectedAttempt.BindValueChanged(selection => SelectedScore.Value = selection.NewValue?.Score ?? aggregate, true);
        StatisticsPanel.Show();
    }

    public override bool OnBackButton()
    {
        if (selectedAttempt.Value == null)
            return base.OnBackButton();

        selectedAttempt.Value = null;
        return true;
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e))
            return true;

        onClosed(restart);
        return false;
    }

    protected override void OnNextAttempt()
    {
        restart = true;
        this.Exit();
    }

    protected override void OnReturnToSettings() => this.Exit();

}
