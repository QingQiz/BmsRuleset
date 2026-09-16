using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestAbortFirstCourseStageSavesHistory(bool failImport)
    {
        BeatmapInfo stageBeatmap = null!;
        BmsCourseSession session = null!;
        DeferredImportCoursePlayer player = null!;
        var courseId = Guid.NewGuid().ToString();

        AddStep("configure course", () =>
        {
            var ruleset = rulesets.AvailableRulesets.Single(info => info.ShortName == Constant.SHORT_NAME);
            stageBeatmap = beatmaps.Import(createBeatmapSet(ruleset))!.Value.Beatmaps.Single().Detach();
            var stage = new BmsCourseStage("Stage", "1", BeatmapHash: stageBeatmap.Hash);
            var course = new BmsCourseDefinition(courseId, "Table", "Course", [stage, stage], []);
            session = new BmsCourseSession(course,
                [new BmsResolvedCourseStage(stage, stageBeatmap), new BmsResolvedCourseStage(stage, stageBeatmap)],
                [new BmsModClassGauge()], BmsGaugeType.Class);
            BmsRulesetRuntime.CourseCatalog.Replace([course]);
        });
        AddStep("load song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("song select loaded", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("beatmap carousel ready", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course selection ready", () => controller.SelectedCourse?.Id == courseId && !controller.CourseCarousel.IsFiltering);
        AddStep("play first course stage", () =>
        {
            var beatmap = new BmsBeatmap
            {
                BeatmapInfo = stageBeatmap,
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = 8,
                HitObjects =
                {
                    new BmsNote { StartTime = 1000, Column = 1 },
                    new BmsNote { StartTime = 60000, Column = 1 },
                },
            };
            BmsTestBeatmaps.SetupBeatmapInfo(beatmap, stageBeatmap.Ruleset);
            Beatmap.Value = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            SelectedMods.Value = session.Mods;
            session.BeginCurrentStage();
            Stack.Push(player = new DeferredImportCoursePlayer(session, failImport));
        });
        AddUntilStep("course player loaded", () => player.IsLoaded && player.LoadedBeatmapSuccessfully && player.Alpha == 1);
        AddUntilStep("partial score recorded", () => player.GameplayState.ScoreProcessor.JudgedHits > 0);
        AddStep("abort stage", () => player.Exit());
        AddUntilStep("score import started", () => player.ImportCount == 1);
        AddAssert("exit waits for import", () => Stack.CurrentScreen, () => Is.SameAs(player));
        AddAssert("course is not recorded before import", () => BmsRulesetRuntime.CourseResults!.GetHistory(courseId), () => Is.Empty);
        AddStep("repeat exit while importing", () => player.Exit());
        AddAssert("import is not duplicated", () => player.ImportCount, () => Is.EqualTo(1));
        AddStep("finish score import", () => player.AllowImport());
        AddUntilStep("player exits", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
        AddAssert("course aborted", () => session.Status, () => Is.EqualTo(BmsCourseStatus.Aborted));
        AddAssert("later stage remains unplayed", () => session.Stages[1].Status, () => Is.EqualTo(BmsCourseStageStatus.NotPlayed));
        AddAssert("stage score matches import outcome", () => session.CurrentStage.Score != null, () => Is.EqualTo(!failImport));

        if (!failImport)
        {
            AddAssert("aborted score is not passed", () => session.CurrentStage.Score!.Passed, () => Is.False);
            AddStep("restore partial score from database", () => Realm.Run(realm =>
            {
                var score = realm.Find<ScoreInfo>(session.CurrentStage.Score!.ID);
                Assert.That(score, Is.Not.Null);
                Assert.Multiple(() =>
                {
                    Assert.That(score!.Rank, Is.EqualTo(ScoreRank.F));
                    Assert.That(score.Statistics.Values.Sum(), Is.GreaterThan(0));
                });
            }));
        }

        AddStep("show course results", () => Stack.Push(new BmsCourseResultsScreen(session)));
        AddUntilStep("course result saved", () => BmsRulesetRuntime.CourseResults!.GetHistory(courseId).Count, () => Is.EqualTo(1));
        AddStep("saved course stages can be resolved", () =>
        {
            var attempt = BmsRulesetRuntime.CourseResults!.GetHistory(courseId).Single().Attempt!;
            Assert.That(attempt.Stages, Has.Length.EqualTo(session.Course.Stages.Count));
            foreach (var stage in attempt.Stages)
            {
                Assert.That(BmsCourseStagePanel.QueryBeatmap(beatmaps, stage.BeatmapHash), Is.Not.Null, stage.BeatmapHash);
                if (stage.ScoreId is { } id)
                    Assert.That(Realm.Run(realm => realm.Find<ScoreInfo>(id) is { DeletePending: false }), Is.True);
            }
        });
        AddStep("return to course select", () => Stack.CurrentScreen.Exit());
        AddUntilStep("song select resumed", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
        AddUntilStep("course lamp matches saved history", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>().Single()
            .ChildrenOfType<BmsLampDisplay>().Single().Lamp, () => Is.EqualTo(failImport ? BmsLamp.NoPlay : BmsLamp.Failed));
        AddUntilStep("leaderboard matches saved history", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BmsLeaderboardScore>().Count(), () => Is.EqualTo(failImport ? 0 : 1));
    }

    private partial class DeferredImportCoursePlayer(BmsCourseSession session, bool failImport) : BmsCoursePlayer(session)
    {
        private readonly TaskCompletionSource importAllowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int importCount;

        public int ImportCount => Volatile.Read(ref importCount);

        protected override bool PauseOnFocusLost => false;

        public void AllowImport() => importAllowed.TrySetResult();

        protected override async Task ImportScore(Score score)
        {
            Interlocked.Increment(ref importCount);
            await importAllowed.Task.ConfigureAwait(false);

            if (failImport)
                throw new InvalidOperationException("Test score import failure.");

            await base.ImportScore(score).ConfigureAwait(false);
        }

        protected override void Dispose(bool isDisposing)
        {
            importAllowed.TrySetCanceled();
            base.Dispose(isDisposing);
        }
    }
}
