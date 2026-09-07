using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongSelectLampHack
{
    [Cached]
    private readonly OverlayColourProvider leaderboardColourProvider = new(OverlayColourScheme.Aquamarine);

    [Test]
    public void TestReplayAttachmentOnBackgroundThread()
    {
        Score score = null!;
        Task attachment = null!;
        Task<Score> restored = null!;

        importLampBeatmapSet();
        AddStep("attach replay to imported score", () =>
        {
            var info = createScore(lampBeatmapSet.Beatmaps.First(), ScoreRank.A, stats((HitResult.Great, 1)));
            score = new Score { ScoreInfo = scoreManager.Import(info)!.Value.Detach() };
            score.Replay.Frames.Add(new BmsReplayFrame(1234, BmsAction.Key1));
            BmsJudgementEventStore.Set(score.ScoreInfo,
            [
                new BmsJudgementEvent(new BmsJudgementSource(1200, 0, BmsJudgementSourceKind.Note), HitResult.Great,
                    [new BmsTimingObservation(BmsTimingObservationKind.Note, 1200, 1234, 1, HitResult.Great)]),
            ]);
            BmsScoreGaugeHistoryStore.Set(score.ScoreInfo,
                [new BmsGaugeHistoryEvent(1234, BmsGaugeType.Normal, [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.8, false)])]);
            attachment = BmsReplayPatcher.AttachReplayAsync(scoreManager, Realm, score);
        });
        AddUntilStep("attachment completes", () => attachment.IsCompleted);
        AddStep("check background write", () => attachment.GetAwaiter().GetResult());
        AddAssert("detached score contains replay", () => score.ScoreInfo.Files.Single().Filename, () => Is.EqualTo(BmsReplayArchive.FILENAME));
        AddAssert("stored hash and file match detached score", () => Realm.Run(r =>
        {
            var stored = r.Find<ScoreInfo>(score.ScoreInfo.ID)!;
            return stored.Hash == score.ScoreInfo.Hash && stored.Files.Single().File.Hash == score.ScoreInfo.Files.Single().File.Hash;
        }));
        AddAssert("score hash matches archive", () => score.ScoreInfo.Hash, () => Is.EqualTo(BmsReplayArchive.ComputeHash(score)));
        AddStep("read stored replay", () => restored = Task.Run(() => scoreManager.GetScore(score.ScoreInfo)));
        AddUntilStep("replay read completes", () => restored.IsCompleted);
        AddAssert("replay and statistics survive attachment", () =>
        {
            var result = restored.GetAwaiter().GetResult();
            return result.Replay.Frames.Single().Time == 1234
                   && result.ScoreInfo.HitEvents.Single().TimeOffset == 34
                   && BmsScoreGaugeHistoryStore.TryGet(result.ScoreInfo, out var history)
                   && history.Single().States.Single().Health == 0.8;
        });
    }

    [Test]
    public void TestRankUpdatesAfterImportAndDeletion()
    {
        ScoreInfo score = null!;
        BmsPanelLocalRankDisplay display = null!;

        importLampBeatmapSet();
        loadSongSelect();
        AddUntilStep("rank panel realised", () => rankDisplayFor(lampBeatmapSet.Beatmaps.First()) != null);
        AddStep("import new score", () =>
        {
            var beatmap = lampBeatmapSet.Beatmaps.First();
            display = rankDisplayFor(beatmap);
            score = scoreManager.Import(createScore(beatmap, ScoreRank.A, stats((HitResult.Perfect, 1), (HitResult.Ok, 1))))!.Value.Detach();
        });
        AddUntilStep("rank reflects new score", () => display.ChildrenOfType<UpdateableRank>().Single().Rank, () => Is.EqualTo(ScoreRank.A));
        AddAssert("rank layout sees the loaded rank", () => display.HasRank);
        AddStep("delete score", () => scoreManager.Delete([score]));
        AddUntilStep("rank clears after deletion", () => !display.HasRank);
        AddStep("release panel while query is pending", () =>
        {
            songSelect.Mods.Value = [new BmsModHideScratch()];
            display.Beatmap = null;
        });
        AddWaitStep("allow pending query to finish", 3);
        AddAssert("released panel stays clear", () => !display.HasRank);
    }

    [Test]
    public void TestLocalLeaderboardRefreshesStoredScores()
    {
        BmsBeatmapLeaderboardWedge leaderboard = null!;
        ScoreInfo score = null!;

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("show local leaderboard", () =>
        {
            Add(leaderboard = new BmsBeatmapLeaderboardWedge
            {
                Scope = { BindTarget = new Bindable<BeatmapLeaderboardScope>(BeatmapLeaderboardScope.Local) },
            });
            leaderboard.Show();
        });
        AddUntilStep("empty leaderboard finishes loading", () => leaderboard.IsLoaded
            && leaderboard.ChildrenOfType<osu.Game.Graphics.UserInterface.LoadingLayer>().All(layer => layer.State.Value == osu.Framework.Graphics.Containers.Visibility.Hidden));
        AddStep("import score", () => score = scoreManager.Import(
            createScore(Beatmap.Value.BeatmapInfo, ScoreRank.A, stats((HitResult.Great, 1))))!.Value.Detach());
        AddUntilStep("new score appears", () => leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().Any(row => row.Score.ID == score.ID));
        AddAssert("displayed score is detached", () => leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().All(row => !row.Score.IsManaged));
        AddStep("suspend song select", () => songSelect.Push(new ResumeTestScreen()));
        AddUntilStep("child screen loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("return to song select", () => Stack.Exit());
        AddUntilStep("score remains after selection reload", () => Stack.CurrentScreen == songSelect
            && leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().Any(row => row.Score.ID == score.ID));
        AddStep("delete score", () => scoreManager.Delete([score]));
        AddUntilStep("deleted score disappears", () => !leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().Any());
        AddStep("remove test leaderboard", () => Remove(leaderboard, true));
    }

    [Test]
    public void TestLocalLeaderboardDiscardsStaleLoads()
    {
        ControlledLeaderboard leaderboard = null!;
        TaskCompletionSource<LeaderboardScores> oldRequest = null!;
        TaskCompletionSource<LeaderboardScores> newRequest = null!;
        ScoreInfo oldScore = null!;
        ScoreInfo newScore = null!;

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("load leaderboard with delayed queries", () =>
        {
            oldScore = createScore(Beatmap.Value.BeatmapInfo, ScoreRank.A, stats((HitResult.Great, 1)));
            newScore = createScore(Beatmap.Value.BeatmapInfo, ScoreRank.S, stats((HitResult.Perfect, 1)), new BmsModHideScratch());
            Add(leaderboard = new ControlledLeaderboard
            {
                RelativeSizeAxes = Axes.Both,
                Scope = { BindTarget = new Bindable<BeatmapLeaderboardScope>(BeatmapLeaderboardScope.Local) },
                FilterBySelectedMods = { BindTarget = new BindableBool(true) },
            });
            leaderboard.Show();
        });
        AddUntilStep("first query pending without blocking updates", () => leaderboard.Requests.Count > 0);
        AddStep("change mods while query is pending", () =>
        {
            oldRequest = leaderboard.Requests.Last();
            SelectedMods.Value = [new BmsModHideScratch()];
        });
        AddUntilStep("new query pending", () => leaderboard.Requests.Last() != oldRequest);
        AddStep("complete current query", () =>
        {
            newRequest = leaderboard.Requests.Last();
            newRequest.SetResult(LeaderboardScores.Success([newScore], 1, 1, null));
        });
        AddUntilStep("current score shown", () => leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().Any(row => row.Score.ID == newScore.ID));
        AddStep("complete obsolete query", () => oldRequest.SetResult(LeaderboardScores.Success([oldScore], 1, 1, null)));
        AddWaitStep("allow obsolete completion to run", 3);
        AddAssert("obsolete score is ignored", () => leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().All(row => row.Score.ID == newScore.ID));
        AddStep("start another query", () => leaderboard.RefetchScores());
        AddUntilStep("another query pending", () => leaderboard.Requests.Last() != newRequest);
        AddStep("clear selection while query is pending", () =>
        {
            newRequest = leaderboard.Requests.Last();
            Beatmap.SetDefault();
            newRequest.SetResult(LeaderboardScores.Success([oldScore], 1, 1, null));
        });
        AddWaitStep("allow cleared query to finish", 3);
        AddAssert("empty selection stays empty", () => !leaderboard.ChildrenOfType<BeatmapLeaderboardScore>().Any());
        AddStep("remove test leaderboard", () => Remove(leaderboard, true));
    }

    private partial class ControlledLeaderboard : BmsBeatmapLeaderboardWedge
    {
        internal readonly List<TaskCompletionSource<LeaderboardScores>> Requests = [];

        protected override Task<LeaderboardScores> LoadLocalScoresAsync(LeaderboardCriteria criteria, CancellationToken token)
        {
            var request = new TaskCompletionSource<LeaderboardScores>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add(request);
            return request.Task;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            foreach (var request in Requests)
                request.TrySetCanceled();
        }
    }
}
