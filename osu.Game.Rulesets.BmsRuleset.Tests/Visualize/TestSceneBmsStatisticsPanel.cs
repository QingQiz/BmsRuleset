#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsStatisticsPanel : OsuTestScene
{
    [Test]
    public void TestCachedDataArrivingBeforeFirstChartUpdate()
    {
        BmsResultStatisticsGrid grid = null!;
        AddStep("load grid with cached data", () =>
        {
            grid = new BmsResultStatisticsGrid();
            grid.OnLoadComplete += _ => grid.SetData(data(3));
            Child = grid;
        });
        AddUntilStep("gauge data displayed", () => grid.ChildrenOfType<BmsGaugeHistoryGraph.GaugePath>().Any());
        AddUntilStep("scatter data displayed", () => grid.ChildrenOfType<BmsHitScatterStatistic>().Single()
            .ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "3 hits"));
        AddUntilStep("offset data displayed", () => grid.ChildrenOfType<BmsHitOffsetStatistic>().Single()
            .ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "3 hits"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestEmptyChartsStayVisibleUntilDataAnimatesIn(bool slowLoad)
    {
        DeferredReplayStatisticsPanel panel = null!;
        BmsResultStatisticsGrid grid = null!;
        Drawable[] charts = null!;
        var clock = new ManualClock();
        var readyTime = slowLoad ? 1000 : 100;

        AddStep("load historical score", () => Child = panel = new DeferredReplayStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(clock),
            Score = { Value = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("empty charts visible while replay is pending", () => panel.Replays.Count == 1 && chartsVisible(panel));
        AddAssert("no global spinner", () => panel.ChildrenOfType<LoadingSpinner>(), () => Is.Empty);
        AddAssert("no premature replay prompt", () => panel.ChildrenOfType<ReplayDownloadButton>(), () => Is.Empty);
        AddAssert("gauge has no data", () => panel.ChildrenOfType<BmsGaugeHistoryGraph.GaugePath>(), () => Is.Empty);
        AddAssert("scatter has no points", () => scatterPoints(panel), () => Is.Empty);
        AddStep("remember containers and advance loading", () =>
        {
            grid = panel.ChildrenOfType<BmsResultStatisticsGrid>().Single();
            charts = chartDrawables(panel);
            clock.CurrentTime = readyTime;
        });
        AddAssert("charts still visible", () => chartsVisible(panel));
        AddAssert("data waits for replay restoration", () => panel.Requests, () => Is.Empty);
        AddStep("finish replay restoration", () => panel.Replays[0].Completion.SetResult());
        AddUntilStep("data load pending", () => panel.Requests.Count == 1);
        AddAssert("empty charts stay visible during data load", () => chartsVisible(panel));
        AddStep("finish data load", () => panel.Requests[0].Completion.SetResult(data(3)));
        AddUntilStep("data attached", () => panel.ChildrenOfType<BmsGaugeHistoryGraph.GaugePath>().Any() && scatterPoints(panel).Any());
        AddAssert("same grid and chart instances", () => ReferenceEquals(contentFor(panel).Child, grid)
            && chartDrawables(panel).SequenceEqual(charts));
        AddAssert("containers stay opaque", () => grid.Alpha == 1 && charts.All(chart => chart.Alpha == 1));
        AddAssert("data starts from empty", () => dataLayers(panel).All(layer => layer.Name is "Timeline data" or "Hit offset data"
            ? layer.Scale.Y == 0 : layer.Alpha == 0));
        AddStep("advance data entrance", () => clock.CurrentTime = readyTime + 100);
        AddUntilStep("only data animates", () => dataLayers(panel).All(layer => layer.Name is "Timeline data" or "Hit offset data"
            ? layer.Scale.Y is > 0 and < 1 : layer.Alpha is > 0 and < 1));
        AddAssert("chart frames remain visible", () => chartsVisible(panel));
        AddStep("finish data entrance", () => clock.CurrentTime = readyTime + 450);
        AddUntilStep("data fully visible", () => dataLayers(panel).All(layer => layer.Scale.Y == 1 && layer.Alpha == 1));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void TestMissingReplayOrFailureFadesChartsIntoPrompt(bool failure, bool slowLoad)
    {
        DeferredReplayStatisticsPanel panel = null!;
        BmsResultStatisticsGrid grid = null!;
        var clock = new ManualClock();
        var exitStart = slowLoad ? 1000 : 200;

        AddStep("load historical score", () => Child = panel = new DeferredReplayStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(clock),
            Score = { Value = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("empty charts visible", () => panel.Replays.Count == 1 && chartsVisible(panel));
        AddStep("advance replay loading", () => clock.CurrentTime = slowLoad ? 1000 : 0);
        AddStep("complete replay restoration", () =>
        {
            grid = panel.ChildrenOfType<BmsResultStatisticsGrid>().Single();
            if (failure)
                panel.Replays[0].Completion.SetException(new InvalidOperationException("Test replay failure"));
            else
                panel.Replays[0].Completion.SetResult();
        });
        if (!failure)
        {
            AddUntilStep("data requested", () => panel.Requests.Count == 1);
            AddStep("no replay data available", () => panel.Requests[0].Completion.SetResult(null));
        }

        if (!slowLoad)
        {
            AddStep("before minimum chart visibility", () => clock.CurrentTime = 199);
            AddAssert("fast failure keeps charts visible", () => grid.Alpha == 1 && !grid.Transforms.Any());
            AddStep("finish minimum chart visibility", () => clock.CurrentTime = exitStart);
        }

        AddUntilStep("chart exit scheduled", () => grid.Transforms.Any());
        AddAssert("prompt waits for charts", () => ReferenceEquals(contentFor(panel).Child, grid));
        AddStep("advance chart exit", () => clock.CurrentTime = exitStart + 100);
        AddUntilStep("charts remain clearly visible early in exit", () => grid.Alpha is > 0.8f and < 1);
        AddStep("reach exit midpoint", () => clock.CurrentTime = exitStart + 225);
        AddUntilStep("charts half visible at exit midpoint", () => grid.Alpha, () => Is.EqualTo(0.5f).Within(0.01));
        AddStep("finish chart exit", () => clock.CurrentTime = exitStart + 450);
        AddUntilStep("prompt replaces charts", () => contentFor(panel).Child is not BmsResultStatisticsGrid);
        AddStep("advance prompt entrance", () => clock.CurrentTime = exitStart + 550);
        AddUntilStep("prompt fades in", () => contentFor(panel).Child.Alpha is > 0 and < 1);
        AddStep("finish prompt entrance", () => clock.CurrentTime = exitStart + 900);
        AddUntilStep("prompt fully visible", () => contentFor(panel).Child.Alpha == 1);
        AddAssert("correct prompt", () => failure
            ? contentFor(panel).Child is OsuTextFlowContainer && panel.Requests.Count == 0
            : panel.ChildrenOfType<ReplayDownloadButton>().Any());
        AddAssert("charts removed", () => panel.ChildrenOfType<BmsResultStatisticsGrid>(), () => Is.Empty);
    }

    [Test]
    public void TestImmediateFailureWaitsForScreenEntrance()
    {
        DeferredReplayStatisticsPanel panel = null!;
        Container screen = null!;
        BmsResultStatisticsGrid grid = null!;
        var clock = new ManualClock();

        AddStep("load results before screen entrance", () => Child = screen = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(clock),
            Alpha = 0,
            AlwaysPresent = true,
            Child = panel = new DeferredReplayStatisticsPanel
            {
                RelativeSizeAxes = Axes.Both,
                Score = { Value = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo } },
            },
        });
        AddUntilStep("empty charts loaded", () => panel.Replays.Count == 1 && chartsVisible(panel));
        AddStep("fail replay before screen appears", () =>
        {
            grid = panel.ChildrenOfType<BmsResultStatisticsGrid>().Single();
            panel.Replays[0].Completion.SetException(new InvalidOperationException("Immediate replay failure"));
        });
        AddStep("advance while screen is invisible", () => clock.CurrentTime = 1000);
        AddAssert("hidden time does not dismiss charts", () => grid.Alpha == 1 && !grid.Transforms.Any());
        AddStep("start screen entrance", () => screen.FadeIn(350));
        AddStep("advance screen entrance", () => clock.CurrentTime = 1100);
        AddUntilStep("screen partially visible", () => screen.Alpha is > 0 and < 1);
        AddAssert("chart exit does not overlap entrance", () => grid.Alpha == 1 && !grid.Transforms.Any());
        AddStep("finish screen entrance", () => clock.CurrentTime = 1350);
        AddUntilStep("screen fully visible", () => screen.Alpha == 1);
        AddStep("before visible chart dwell completes", () => clock.CurrentTime = 1549);
        AddAssert("charts remain opaque after entrance", () => grid.Alpha == 1 && !grid.Transforms.Any());
        AddStep("finish visible chart dwell", () => clock.CurrentTime = 1550);
        AddUntilStep("chart exit begins", () => grid.Transforms.Any());
        AddStep("reach chart exit midpoint", () => clock.CurrentTime = 1775);
        AddUntilStep("exit remains visible", () => grid.Alpha, () => Is.EqualTo(0.5f).Within(0.01));
        AddStep("finish chart exit", () => clock.CurrentTime = 2000);
        AddUntilStep("prompt attached", () => contentFor(panel).Child is OsuTextFlowContainer);
        AddStep("finish prompt entrance", () => clock.CurrentTime = 2450);
        AddUntilStep("prompt fully visible", () => contentFor(panel).Child.Alpha == 1);
    }

    [Test]
    public void TestScoreChangesCancelReplayRestorationAndPendingTransitions()
    {
        DeferredReplayStatisticsPanel panel = null!;
        BmsResultStatisticsGrid grid = null!;
        var clock = new ManualClock();
        AddStep("load first score", () => Child = panel = new DeferredReplayStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(clock),
            Score = { Value = new ScoreInfo { ID = Guid.NewGuid(), Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("first replay pending", () => panel.Replays.Count == 1);
        AddStep("select second score", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("second replay pending", () => panel.Replays.Count == 2);
        AddAssert("first replay cancelled", () => panel.Replays[0].Cancellation.IsCancellationRequested);
        AddStep("finish obsolete replay", () => panel.Replays[0].Completion.SetResult());
        AddWaitStep("allow late completion", 2);
        AddAssert("obsolete replay does not load data", () => panel.Requests, () => Is.Empty);
        AddStep("finish current replay", () => panel.Replays[1].Completion.SetResult());
        AddUntilStep("current data requested", () => panel.Requests.Count == 1);
        AddStep("finish without replay data", () => panel.Requests[0].Completion.SetResult(null));
        AddStep("finish visible chart dwell", () => clock.CurrentTime = 200);
        AddUntilStep("chart exit scheduled", () => panel.ChildrenOfType<BmsResultStatisticsGrid>().Single().Transforms.Any());
        AddStep("advance exit transition", () => clock.CurrentTime = 300);
        AddUntilStep("charts exiting", () => panel.ChildrenOfType<BmsResultStatisticsGrid>().Single().Alpha is > 0 and < 1);
        AddStep("select third score during transition", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("third replay pending with empty charts", () => panel.Replays.Count == 3 && chartsVisible(panel));
        AddStep("advance past obsolete transition", () =>
        {
            grid = panel.ChildrenOfType<BmsResultStatisticsGrid>().Single();
            clock.CurrentTime = 1500;
        });
        AddAssert("obsolete transition cannot replace charts", () => ReferenceEquals(contentFor(panel).Child, grid) && chartsVisible(panel));
        AddStep("remove panel", Clear);
        AddUntilStep("replay cancelled on disposal", () => panel.Replays[2].Cancellation.IsCancellationRequested);
        AddStep("complete disposed replay", () => panel.Replays[2].Completion.SetResult());
    }

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
        AddStep("complete second load first", () => panel.Requests[1].Completion.SetResult(data(3)));
        AddUntilStep("current data displayed", () => panel.ChildrenOfType<BmsHitOffsetStatistic>().Single()
            .ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "3 hits"));
        AddStep("complete obsolete first load", () => panel.Requests[0].Completion.SetResult(data(7)));
        AddWaitStep("allow late completion", 2);
        AddAssert("late data cannot replace current chart", () => panel.ChildrenOfType<BmsHitOffsetStatistic>().Single()
            .ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "3 hits"));
        AddStep("start another load", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("third load pending", () => panel.Requests.Count == 3);
        AddStep("remove panel", Clear);
        AddUntilStep("pending load cancelled on disposal", () => panel.Requests[2].Cancellation.IsCancellationRequested);
        AddStep("dispose again and complete late load", () =>
        {
            panel.Dispose();
            panel.Requests[2].Completion.SetResult(data(5));
        });
    }

    private static Container contentFor(BmsStatisticsPanel panel) =>
        panel.ChildrenOfType<Container>().Single(container => container.Name == "Result statistics content");

    private static Drawable[] chartDrawables(BmsStatisticsPanel panel) =>
    [
        panel.ChildrenOfType<BmsGaugeHistoryGraph>().Single(),
        panel.ChildrenOfType<BmsTimelineStatistic>().Single(),
        panel.ChildrenOfType<BmsHitScatterStatistic>().Single(),
        panel.ChildrenOfType<BmsHitOffsetStatistic>().Single(),
    ];

    private static bool chartsVisible(BmsStatisticsPanel panel) => panel.ChildrenOfType<BmsResultStatisticsGrid>().Any()
        && chartDrawables(panel).All(chart => chart.IsLoaded && chart.IsPresent && chart.DrawHeight > 0 && chart.Alpha == 1)
        && contentFor(panel).Alpha == 1 && contentFor(panel).Child.Alpha == 1;

    private static IEnumerable<Circle> scatterPoints(BmsStatisticsPanel panel) => panel.ChildrenOfType<BmsHitScatterStatistic>().Single()
        .ChildrenOfType<Circle>().Where(circle => Math.Abs(circle.Width - 4.4f) < 0.01f);

    private static IEnumerable<Drawable> dataLayers(BmsStatisticsPanel panel) => panel.ChildrenOfType<Drawable>()
        .Where(drawable => drawable.Name is "Gauge data" or "Timeline data" or "Hit scatter data" or "Hit offset data");

    private static BmsResultStatisticsData data(int hits)
    {
        var beatmap = new BmsBeatmap();
        var score = new ScoreInfo();
        for (var i = 0; i < hits; i++)
        {
            var note = new BmsNote { StartTime = 1000 + i * 1000 };
            beatmap.HitObjects.Add(note);
            score.HitEvents.Add(new HitEvent(10, 1, HitResult.Perfect, note, null, null));
        }

        return BmsResultStatisticsData.Create(score, beatmap);
    }

    private partial class DeferredStatisticsPanel : BmsStatisticsPanel
    {
        internal readonly List<(TaskCompletionSource<BmsResultStatisticsData?> Completion, CancellationToken Cancellation)> Requests = [];

        protected override bool StartHidden => false;

        internal DeferredStatisticsPanel()
            : base(false)
        {
            Show();
        }

        protected override Task RestoreReplayDataAsync(ScoreInfo score, CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<BmsResultStatisticsData?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((completion, cancellationToken));
            return completion.Task;
        }
    }

    private partial class DeferredReplayStatisticsPanel : DeferredStatisticsPanel
    {
        internal readonly List<(TaskCompletionSource Completion, CancellationToken Cancellation)> Replays = [];

        protected override Task RestoreReplayDataAsync(ScoreInfo score, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Replays.Add((completion, cancellationToken));
            return completion.Task;
        }
    }
}
