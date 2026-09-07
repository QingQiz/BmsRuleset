using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Statistics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongSelectLampHack
{
    [Test]
    public void TestBeatmapSelectionDiscardsStaleLoads()
    {
        ControlledSongSelect controlled = null!;
        WorkingBeatmap first = null!;
        WorkingBeatmap second = null!;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("prepare delayed selections", () =>
        {
            first = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[0]);
            second = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[1]);
            controlled.DelayLoads = true;
        });
        AddStep("select first chart", () => controlled.LoadBeatmapSelection(first.BeatmapInfo));
        AddUntilStep("first selection pending without blocking frames", () => controlled.Requests.Count == 1);
        AddStep("select second chart", () => controlled.LoadBeatmapSelection(second.BeatmapInfo));
        AddAssert("first selection cancelled", () => controlled.Requests[0].Token.IsCancellationRequested);
        AddStep("complete second selection", () => controlled.Requests[1].Completion.SetResult(second));
        AddUntilStep("second chart applied", () => ReferenceEquals(controlled.Beatmap.Value, second));
        AddStep("complete first selection late", () => controlled.Requests[0].Completion.SetResult(first));
        AddWaitStep("allow old completion", 3);
        AddAssert("new selection retained", () => ReferenceEquals(controlled.Beatmap.Value, second));
    }

    [Test]
    public void TestConfirmWaitsForFreshBeatmapAndCancelsOnSelectionChange()
    {
        ControlledSongSelect controlled = null!;
        WorkingBeatmap first = null!;
        WorkingBeatmap second = null!;
        var started = false;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("prepare delayed confirmation", () =>
        {
            first = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[0]);
            second = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[1]);
            controlled.DelayLoads = true;
            controlled.Confirm(first.BeatmapInfo, () => started = true);
        });
        AddAssert("confirmation requests fresh metadata", () => controlled.Requests.Single().Refetch);
        AddAssert("gameplay waits for query", () => !started);
        AddStep("change selection while confirming", () => controlled.LoadBeatmapSelection(second.BeatmapInfo));
        AddStep("finish obsolete confirmation", () => controlled.Requests[0].Completion.SetResult(first));
        AddWaitStep("allow obsolete confirmation", 3);
        AddAssert("obsolete chart is not started", () => !started);
        AddStep("finish selected chart", () => controlled.Requests[1].Completion.SetResult(second));
        AddUntilStep("selected chart applied", () => ReferenceEquals(controlled.Beatmap.Value, second));
        AddStep("confirm selected chart", () => controlled.Confirm(second.BeatmapInfo, () => started = true));
        AddAssert("fresh query still required", () => !started && controlled.Requests[2].Refetch);
        AddStep("finish confirmation", () => controlled.Requests[2].Completion.SetResult(second));
        AddUntilStep("selected chart starts", () => started);
    }

    [Test]
    public void TestConfirmDuringDebounceUsesQueuedChart()
    {
        ControlledSongSelect controlled = null!;
        BeatmapInfo next = null!;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("queue another chart and confirm immediately", () =>
        {
            next = lampBeatmapSet.Beatmaps.First(beatmap => beatmap.ID != controlled.Beatmap.Value.BeatmapInfo.ID);
            controlled.DelayLoads = true;
            carousel.Activate(carousel.GetCarouselItems()!.First(item => item.Model is GroupedBeatmap grouped && grouped.Beatmap.ID == next.ID));
            logo.Action();
        });
        AddUntilStep("queued chart confirmation requested", () => controlled.Requests.Count == 1);
        AddAssert("confirmation targets queued chart", () => controlled.Requests[0].Beatmap.ID, () => Is.EqualTo(next.ID));
        AddAssert("confirmation refetches metadata", () => controlled.Requests[0].Refetch);
        AddStep("finish confirmation", () => controlled.Requests[0].Completion.SetResult(beatmaps.GetWorkingBeatmap(next)));
        AddUntilStep("queued chart starts", () => controlled.StartedBeatmap?.ID, () => Is.EqualTo(next.ID));
    }

    [Test]
    public void TestResumeLoadCannotOverrideNewSelection()
    {
        ControlledSongSelect controlled = null!;
        WorkingBeatmap previous = null!;
        WorkingBeatmap next = null!;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("suspend selection", () =>
        {
            previous = controlled.Beatmap.Value;
            next = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps.First(beatmap => beatmap.ID != previous.BeatmapInfo.ID));
            controlled.DelayLoads = true;
            controlled.Push(new ResumeTestScreen());
        });
        AddUntilStep("child loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("resume", () => Stack.Exit());
        AddUntilStep("resume query pending", () => controlled.Requests.Count == 1);
        AddStep("select different chart", () => controlled.LoadBeatmapSelection(next.BeatmapInfo));
        AddStep("finish new selection", () => controlled.Requests[1].Completion.SetResult(next));
        AddUntilStep("new selection applied", () => ReferenceEquals(controlled.Beatmap.Value, next));
        AddStep("finish old resume query", () => controlled.Requests[0].Completion.SetResult(previous));
        AddWaitStep("allow old resume completion", 3);
        AddAssert("new selection survives", () => ReferenceEquals(controlled.Beatmap.Value, next));
    }

    [Test]
    public void TestResumePreservesQueuedSelection()
    {
        ControlledSongSelect controlled = null!;
        BeatmapInfo next = null!;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("queue selection and suspend before debounce", () =>
        {
            next = lampBeatmapSet.Beatmaps.First(beatmap => beatmap.ID != controlled.Beatmap.Value.BeatmapInfo.ID);
            controlled.DelayLoads = true;
            carousel.Activate(carousel.GetCarouselItems()!.First(item => item.Model is GroupedBeatmap grouped && grouped.Beatmap.ID == next.ID));
            controlled.Push(new ResumeTestScreen());
        });
        AddUntilStep("child loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("resume pending selection", () => Stack.Exit());
        AddUntilStep("resume query pending", () => controlled.Requests.Any(request => !request.Token.IsCancellationRequested));
        AddAssert("resume targets queued chart", () => controlled.Requests.Last().Beatmap.ID, () => Is.EqualTo(next.ID));
        AddStep("complete pending selection", () => controlled.Requests.Last().Completion.SetResult(beatmaps.GetWorkingBeatmap(next)));
        AddUntilStep("queued chart selected", () => controlled.Beatmap.Value.BeatmapInfo.ID, () => Is.EqualTo(next.ID));
    }

    [Test]
    public void TestPendingConfirmationCancelledOnSuspend()
    {
        ControlledSongSelect controlled = null!;
        var started = false;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("confirm and leave", () =>
        {
            controlled.DelayLoads = true;
            controlled.Confirm(controlled.Beatmap.Value.BeatmapInfo, () => started = true);
            controlled.Push(new ResumeTestScreen());
        });
        AddUntilStep("child loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddAssert("pending query cancelled", () => controlled.Requests[0].Token.IsCancellationRequested);
        AddStep("complete departed query", () => controlled.Requests[0].Completion.SetResult(controlled.Beatmap.Value));
        AddWaitStep("allow departed query completion", 3);
        AddAssert("gameplay is not started behind another screen", () => !started);
    }

    [Test]
    public void TestSelectionValidationAfterHidingCurrentChart()
    {
        BeatmapInfo hidden = null!;
        importLampBeatmapSet();
        loadSongSelect();
        AddStep("hide current chart", () =>
        {
            hidden = songSelect.Beatmap.Value.BeatmapInfo;
            beatmaps.Hide(hidden);
        });
        AddUntilStep("hidden chart replaced", () => songSelect.Beatmap.Value.BeatmapInfo.ID != hidden.ID);
        AddAssert("replacement remains selectable", () => !songSelect.Beatmap.IsDefault && !songSelect.Beatmap.Value.BeatmapInfo.Hidden);
    }

    [Test]
    public void TestConfirmationRejectsNewlyHiddenMetadata()
    {
        ControlledSongSelect controlled = null!;
        WorkingBeatmap hidden = null!;
        var started = false;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("hide chart", () => beatmaps.Hide(lampBeatmapSet.Beatmaps[0]));
        AddUntilStep("hidden chart removed from carousel", () => !controlled.IsFiltering && carousel.MatchedBeatmapsCount == all_lamps.Length - 1);
        AddStep("confirm using stale metadata", () =>
        {
            controlled.DelayLoads = true;
            controlled.Confirm(lampBeatmapSet.Beatmaps[0], () => started = true);
        });
        AddStep("return freshly hidden metadata", () =>
        {
            hidden = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[0], true);
            Assert.That(hidden.BeatmapInfo.Hidden, Is.True);
            controlled.DelayLoads = false;
            controlled.Requests.Single().Completion.SetResult(hidden);
        });
        AddUntilStep("selectable fallback loaded", () => !controlled.Beatmap.Value.BeatmapInfo.Hidden && !controlled.Beatmap.IsDefault);
        AddWaitStep("allow confirmation to settle", 3);
        AddAssert("hidden chart never starts", () => !started);
    }

    [Test]
    public void TestWorkingBeatmapQueriesRunOffUpdateThread()
    {
        ControlledSongSelect controlled = null!;
        var started = false;
        importLampBeatmapSet();
        loadSongSelect(() => controlled = new ControlledSongSelect());
        AddStep("query fresh selection", () =>
        {
            controlled.TrackQueryReads = true;
            controlled.Confirm(lampBeatmapSet.Beatmaps[0], () => started = true);
        });
        AddUntilStep("fresh selection completes", () => started);
        AddAssert("working beatmap query did not read on update thread", () => controlled.QueryUpdateReads.Count > 0
            && controlled.QueryUpdateReads.All(count => count == 0));
    }

    private partial class ControlledSongSelect : BmsSoloSongSelect
    {
        internal bool DelayLoads;
        internal bool TrackQueryReads;
        internal BeatmapInfo StartedBeatmap;
        internal readonly List<SelectionRequest> Requests = [];
        internal readonly List<int> QueryUpdateReads = [];

        internal void Confirm(BeatmapInfo beatmap, Action action) => SelectAndRun(beatmap, action);

        protected override void OnStart() => StartedBeatmap = Beatmap.Value.BeatmapInfo;

        protected override Task<WorkingBeatmap> LoadWorkingBeatmapAsync(BeatmapInfo beatmap, bool refetch, CancellationToken token)
        {
            if (DelayLoads)
            {
                var completion = new TaskCompletionSource<WorkingBeatmap>(TaskCreationOptions.RunContinuationsAsynchronously);
                Requests.Add(new SelectionRequest(beatmap, refetch, token, completion));
                return completion.Task;
            }

            var reads = GlobalStatistics.Get<int>("Realm", "Reads (Update)").Value;
            var task = base.LoadWorkingBeatmapAsync(beatmap, refetch, token);
            if (TrackQueryReads)
                QueryUpdateReads.Add(GlobalStatistics.Get<int>("Realm", "Reads (Update)").Value - reads);
            return task;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            foreach (var request in Requests)
                request.Completion.TrySetCanceled();
        }
    }

    private sealed record SelectionRequest(BeatmapInfo Beatmap, bool Refetch, CancellationToken Token, TaskCompletionSource<WorkingBeatmap> Completion);
}
