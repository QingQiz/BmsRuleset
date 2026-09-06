using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsStatisticsPanel : OsuTestScene
{
    [Test]
    public void TestScoreChangesDiscardLateStatisticsAndDisposalCancelsLoading()
    {
        DeferredStatisticsPanel panel = null!;
        AddStep("load first score", () => Child = panel = new DeferredStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Score = { Value = new ScoreInfo { ID = Guid.NewGuid(), Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("first load pending", () => panel.Requests.Count == 1);
        AddStep("select second score", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("second load pending", () => panel.Requests.Count == 2);
        AddAssert("first load cancelled", () => panel.Requests[0].Cancellation.IsCancellationRequested);
        AddStep("complete second load first", () => panel.Requests[1].Completion.SetResult(items("Current chart")));
        AddUntilStep("current chart displayed", () => panel.ChildrenOfType<Box>().Any(box => box.Name == "Current chart"));
        AddStep("complete obsolete first load", () => panel.Requests[0].Completion.SetResult(items("Obsolete chart")));
        AddWaitStep("allow late completion", 2);
        AddAssert("late data cannot replace current chart", () => panel.ChildrenOfType<Box>().Any(box => box.Name == "Current chart")
                                                                  && panel.ChildrenOfType<Box>().All(box => box.Name != "Obsolete chart"));
        AddStep("start another load", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("third load pending", () => panel.Requests.Count == 3);
        AddStep("remove panel", Clear);
        AddUntilStep("pending load cancelled on disposal", () => panel.Requests[2].Cancellation.IsCancellationRequested);
        AddStep("dispose again and complete late load", () =>
        {
            panel.Dispose();
            panel.Requests[2].Completion.SetResult(items("Disposed chart"));
        });
    }

    private static StatisticItem[] items(string name) =>
    [
        new(BmsStrings.Timeline, () => new Box { Name = name, RelativeSizeAxes = Axes.X, Height = 20 }),
    ];

    private partial class DeferredStatisticsPanel : BmsStatisticsPanel
    {
        internal readonly List<(TaskCompletionSource<StatisticItem[]> Completion, CancellationToken Cancellation)> Requests = [];

        protected override bool StartHidden => false;

        internal DeferredStatisticsPanel()
            : base(false)
        {
            Show();
        }

        protected override Task<StatisticItem[]> LoadStatisticItemsAsync(ScoreInfo score, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<StatisticItem[]>();
            Requests.Add((completion, cancellationToken));
            return completion.Task;
        }
    }
}
