using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsStatisticsPanel : OsuTestScene
{
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public void TestReplayLoadingTransitionsToStatisticsOrReplayPrompt(bool hasReplay, bool slowLoad)
    {
        DeferredReplayStatisticsPanel panel = null!;
        var clock = new ManualClock();
        var readyTime = slowLoad ? 1000 : 550;

        AddStep("load historical score", () => Child = panel = new DeferredReplayStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Clock = new FramedClock(clock),
            Score = { Value = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("replay restoration pending", () => panel.Replays.Count == 1);
        AddStep("advance loading animation", () => clock.CurrentTime = slowLoad ? 1000 : 100);
        AddUntilStep("spinner visible while replay loads", () => spinnerFor(panel).Alpha > 0);
        AddAssert("charts wait for replay restoration", () => panel.Requests, () => Is.Empty);
        AddAssert("no premature replay prompt", () => contentFor(panel).Count, () => Is.Zero);
        AddStep("finish replay restoration", () =>
        {
            if (hasReplay)
                panel.Score.Value!.HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote(), null, null)];

            panel.Replays[0].Completion.SetResult();
        });
        AddUntilStep("chart load pending", () => panel.Requests.Count == 1);
        AddAssert("spinner stays visible while charts load", () => spinnerFor(panel).State.Value, () => Is.EqualTo(Visibility.Visible));
        AddStep("finish chart load", () => panel.Requests[0].Completion.SetResult(items("Replay chart", true)));
        AddUntilStep("content prepared", () => contentFor(panel).Count == 1);
        AddAssert("correct content prepared", () => hasReplay
            ? panel.ChildrenOfType<Box>().Any(box => box.Name == "Replay chart")
            : panel.ChildrenOfType<ReplayDownloadButton>().Any());
        AddAssert("prepared content stays hidden", () => contentFor(panel).Alpha, () => Is.Zero);

        if (!slowLoad)
        {
            AddStep("before loading entrance completes", () => clock.CurrentTime = 549);
            AddAssert("fast load keeps spinner visible", () => spinnerFor(panel).State.Value, () => Is.EqualTo(Visibility.Visible));
            AddAssert("fast load does not flash content", () => contentFor(panel).Alpha, () => Is.Zero);
        }

        AddStep("finish minimum loading time", () => clock.CurrentTime = readyTime);
        AddUntilStep("spinner starts exiting", () => spinnerFor(panel).State.Value == Visibility.Hidden);
        AddStep("advance spinner exit", () => clock.CurrentTime = readyTime + 125);
        AddUntilStep("spinner fades out", () => spinnerFor(panel).Alpha is > 0 and < 1);
        AddAssert("content waits for spinner exit", () => contentFor(panel).Alpha, () => Is.Zero);
        AddStep("advance content entrance", () => clock.CurrentTime = readyTime + 300);
        AddUntilStep("content fades in", () => contentFor(panel).Alpha is > 0 and < 1);
        AddAssert("spinner has finished exiting", () => spinnerFor(panel).Alpha, () => Is.Zero);
        AddStep("finish content entrance", () => clock.CurrentTime = readyTime + 500);
        AddUntilStep("content fully visible", () => contentFor(panel).Alpha == 1);
    }

    [Test]
    public void TestScoreChangesCancelReplayRestorationAndPendingTransitions()
    {
        DeferredReplayStatisticsPanel panel = null!;
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
        AddAssert("obsolete replay does not load charts", () => panel.Requests, () => Is.Empty);
        AddStep("finish current replay", () => panel.Replays[1].Completion.SetResult());
        AddUntilStep("current charts requested", () => panel.Requests.Count == 1);
        AddStep("finish current charts", () => panel.Requests[0].Completion.SetResult(items("Current chart")));
        AddUntilStep("current content prepared", () => contentFor(panel).Count == 1);
        AddStep("start transition", () => clock.CurrentTime = 1000);
        AddUntilStep("spinner exiting", () => spinnerFor(panel).State.Value == Visibility.Hidden);
        AddStep("select third score during transition", () => panel.Score.Value = new ScoreInfo { ID = Guid.NewGuid() });
        AddUntilStep("third replay pending", () => panel.Replays.Count == 3);
        AddStep("advance past obsolete transition", () => clock.CurrentTime = 1500);
        AddAssert("obsolete transition cannot reveal content", () => contentFor(panel).Alpha, () => Is.Zero);
        AddAssert("spinner still loading", () => spinnerFor(panel).State.Value, () => Is.EqualTo(Visibility.Visible));
        AddStep("remove panel", Clear);
        AddUntilStep("replay cancelled on disposal", () => panel.Replays[2].Cancellation.IsCancellationRequested);
        AddStep("complete disposed replay", () => panel.Replays[2].Completion.SetResult());
    }

    [Test]
    public void TestReplayLoadFailureTransitionsToUnavailable()
    {
        DeferredReplayStatisticsPanel panel = null!;
        AddStep("load score", () => Child = panel = new DeferredReplayStatisticsPanel
        {
            RelativeSizeAxes = Axes.Both,
            Score = { Value = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo } },
        });
        AddUntilStep("replay pending", () => panel.Replays.Count == 1);
        AddStep("fail replay load", () => panel.Replays[0].Completion.SetException(new InvalidOperationException("Test replay failure")));
        AddUntilStep("unavailable message visible", () => contentFor(panel).Alpha == 1
            && contentFor(panel).Child is OsuTextFlowContainer { IsPresent: true });
        AddAssert("spinner hidden", () => spinnerFor(panel).Alpha, () => Is.Zero);
        AddAssert("failed replay does not load charts", () => panel.Requests, () => Is.Empty);
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

    private static Container contentFor(BmsStatisticsPanel panel) =>
        panel.ChildrenOfType<Container>().Single(container => container.Name == "Result statistics content");

    private static LoadingSpinner spinnerFor(BmsStatisticsPanel panel) => panel.ChildrenOfType<LoadingSpinner>().Single();

    private static StatisticItem[] items(string name, bool requiresHitEvents = false) =>
    [
        new(BmsStrings.Timeline, () => new Box { Name = name, RelativeSizeAxes = Axes.X, Height = 20 }, requiresHitEvents),
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

        protected override Task RestoreReplayDataAsync(ScoreInfo score, CancellationToken cancellationToken) => Task.CompletedTask;

        protected override Task<StatisticItem[]> LoadStatisticItemsAsync(ScoreInfo score, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<StatisticItem[]>();
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
