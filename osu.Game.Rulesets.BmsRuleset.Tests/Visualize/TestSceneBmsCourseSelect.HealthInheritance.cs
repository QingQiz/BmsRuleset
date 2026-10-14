using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase(BmsGaugeType.Class, false)]
    [TestCase(BmsGaugeType.ExClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, true)]
    public void TestHealthInheritanceThroughRealCourseLifecycle(BmsGaugeType gaugeType, bool autoGauge)
    {
        Task import = null!;
        var directory = string.Empty;
        var courseId = Guid.NewGuid().ToString();
        BeatmapInfo[] charts = null!;
        BmsCoursePlayer player = null!;
        BmsGaugeStateSnapshot[] expected = autoGauge
            ? BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN.Select(type => new BmsGaugeStateSnapshot(type, 1, false)).ToArray()
            : [new(gaugeType, 1, false)];
        var openings = new BmsGaugeStateSnapshot[3][];

        AddStep("import real three-stage BMS charts", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"dan-health-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            for (var stage = 0; stage < 3; stage++)
            {
                var notes = autoGauge ? stage < 2 ? 6 : 2 : 1;
                File.WriteAllText(Path.Combine(directory, $"stage-{stage}.bms"), $"""
                    #PLAYER 1
                    #TITLE Dan Health {courseId}
                    #SUBTITLE Stage {stage}
                    #ARTIST Test
                    #BPM 120
                    #PLAYLEVEL 1
                    #DIFFICULTY 1
                    #01011:{string.Concat(Enumerable.Repeat("01", notes))}
                    """);
            }
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("charts imported", () => import.IsCompleted);
        AddStep("configure imported dan", () =>
        {
            import.GetAwaiter().GetResult();
            charts = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable()
                .Where(chart => chart.Metadata.Source == directory)
                .OrderBy(chart => chart.DifficultyName)
                .Select(chart => chart.Detach()).ToArray());
            Assert.That(charts, Has.Length.EqualTo(3));
            BmsRulesetRuntime.CourseCatalog.Replace(
            [
                new BmsCourseDefinition(courseId, "Table", "Dan",
                    charts.Select(chart => new BmsCourseStage(chart.DifficultyName, "1", BeatmapHash: chart.Hash)).ToArray(), ["gauge_lr2"]),
            ]);
        });
        AddStep("open song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("song select ready", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded && !carousel.IsFiltering);
        AddStep("select dan", () => controller.ShowCourseMode());
        AddUntilStep("dan ready", () => controller.SelectedCourse?.Id == courseId && !controller.CourseCarousel.IsFiltering);
        AddStep("start dan through song select", () =>
        {
            songSelect.Mods.Value = autoGauge ? [new BmsModAutoGauge()] : [BmsCourseSession.CreateGaugeMod(gaugeType)];
            controller.StartCourse(songSelect);
        });

        for (var stage = 0; stage < 3; stage++)
        {
            var stageIndex = stage;
            AddUntilStep($"stage {stageIndex + 1} player entered", () => Stack.CurrentScreen is BmsCoursePlayer current
                && !ReferenceEquals(current, player) && current.IsLoaded && current.LoadedBeatmapSuccessfully && current.Alpha == 1);
            AddStep("check opening after framework load", () =>
            {
                player = (BmsCoursePlayer)Stack.CurrentScreen;
                var health = (BmsHealthProcessor)player.GameplayState.HealthProcessor;
                Assert.That(player.GameplayState.Beatmap.BeatmapInfo.Hash, Is.EqualTo(charts[stageIndex].Hash));
                Assert.That(health.CurrentGaugeStates, Is.EqualTo(expected));
                Assert.That(health.ConfiguredProfileFamily, Is.EqualTo(BmsGaugeProfileFamily.Lr2));
                openings[stageIndex] = expected;
                // Skip the empty lead-in; real note misses must drive completion and the results transition.
                player.ChildrenOfType<GameplayClockContainer>().Single().Seek(19500);
            });
            AddUntilStep("real judgements complete stage", () => player.GameplayState.ScoreProcessor.HasCompleted.Value || player.GameplayState.HasFailed);
            AddStep("capture surviving gauges before result transition", () =>
            {
                var health = (BmsHealthProcessor)player.GameplayState.HealthProcessor;
                Assert.That(player.GameplayState.HasFailed, Is.False, $"Stage {stageIndex}: {string.Join(", ", health.CurrentGaugeStates)}");
                expected = health.CurrentGaugeStates.Where(state => !state.Failed).ToArray();
                Assert.That(health.HasPassedAtEnd(), Is.True);
                Assert.That(expected, Has.Length.EqualTo(autoGauge ? 2 - Math.Min(stageIndex, 1) : 1));
                Assert.That(expected[0].Health, Is.LessThan(openings[stageIndex].Last().Health));
            });

            if (stageIndex < 2)
            {
                AddUntilStep("framework displays imported stage result", () => Stack.CurrentScreen is BmsCourseStageResultsScreen result
                    && result.IsLoaded && result.Alpha == 1);
                AddStep("start next stage from actual result button", () =>
                    ((BmsCourseStageResultsScreen)Stack.CurrentScreen).ChildrenOfType<BmsCourseResultButton>()
                        .Single(button => button.Name == "Start next course stage button").TriggerClick());
            }
        }

        AddUntilStep("framework displays final dan result", () => Stack.CurrentScreen is BmsCourseResultsScreen result
            && result.IsLoaded && result.Alpha == 1);
        AddStep("verify imported scores retain each stage opening", () =>
        {
            for (var stage = 0; stage < charts.Length; stage++)
            {
                var score = Realm.Run(realm => realm.All<ScoreInfo>().AsEnumerable()
                    .Single(score => score.BeatmapHash == charts[stage].Hash && !score.DeletePending).Detach());
                Assert.That(score.Passed, Is.True);
                using var health = new BmsHealthProcessor();
                health.ApplyBeatmap(beatmaps.GetWorkingBeatmap(charts[stage]).Beatmap);
                foreach (var mod in score.Mods.OfType<IApplicableToHealthProcessor>())
                    mod.ApplyToHealthProcessor(health);
                Assert.That(health.CurrentGaugeStates, Is.EqualTo(openings[stage]));
            }
        });
    }

    [TestCase(BmsGaugeType.Class, false)]
    [TestCase(BmsGaugeType.ExClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, true)]
    public void TestCoursePlayersCarryHealthThroughThreeLoads(BmsGaugeType gaugeType, bool autoGauge)
    {
        BeatmapInfo stageBeatmap = null!;
        BmsCourseSession session = null!;
        InheritanceCoursePlayer player = null!;
        BmsGaugeStateSnapshot[] expected = null!;
        BmsLayoutVariant[] layouts = [BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bms5K, BmsLayoutVariant.Pms9K];
        var saved = new Score[layouts.Length];
        var initial = new BmsGaugeStateSnapshot[layouts.Length][];

        AddStep("configure three-stage dan", () =>
        {
            var ruleset = rulesets.AvailableRulesets.Single(info => info.ShortName == Constant.SHORT_NAME);
            stageBeatmap = beatmaps.Import(createBeatmapSet(ruleset))!.Value.Beatmaps.Single().Detach();
            var stage = new BmsCourseStage("Stage", "1", BeatmapHash: stageBeatmap.Hash);
            var course = new BmsCourseDefinition(Guid.NewGuid().ToString(), "Table", "Dan", [stage, stage, stage], ["gauge_lr2"]);
            session = new BmsCourseSession(course,
                layouts.Select(_ => new BmsResolvedCourseStage(stage, stageBeatmap)),
                BmsCourseSession.CreateCourseMods(autoGauge ? [new BmsModAutoGauge()] : [], gaugeType, course.Constraints), gaugeType);
            expected = autoGauge
                ? BmsModAutoGauge.COURSE_AUTO_GAUGE_CHAIN.Select(type => new BmsGaugeStateSnapshot(type, 1, false)).ToArray()
                : [new(gaugeType, 1, false)];
        });
        AddStep("load song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("song select loaded", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);

        for (var index = 0; index < layouts.Length; index++)
        {
            var stageIndex = index;
            AddStep($"load stage {stageIndex + 1}", () =>
            {
                if (stageIndex > 0)
                {
                    session.RequestAdvance();
                    session.Advance();
                }
                var beatmap = new BmsBeatmap
                {
                    BeatmapInfo = stageBeatmap,
                    LayoutVariant = layouts[stageIndex],
                    TotalColumns = layouts[stageIndex] == BmsLayoutVariant.Pms9K ? 9 : 8,
                    HitObjects = [new BmsNote { StartTime = 30000, Column = 1 }, new BmsNote { StartTime = 60000, Column = 1 }],
                };
                BmsTestBeatmaps.SetupBeatmapInfo(beatmap, stageBeatmap.Ruleset);
                Beatmap.Value = new TestWorkingBeatmap(beatmap, audioManager: Audio);
                SelectedMods.Value = session.Mods;
                session.BeginCurrentStage();
                Stack.Push(player = new InheritanceCoursePlayer(session));
            });
            AddUntilStep("dan player loaded", () => player.IsLoaded && player.LoadedBeatmapSuccessfully && player.Alpha == 1);
            AddStep("check carried health after all load callbacks", () =>
            {
                player.StopClock();
                initial[stageIndex] = expected;
                Assert.Multiple(() =>
                {
                    Assert.That(player.Health.CurrentGaugeStates, Is.EqualTo(expected));
                    Assert.That(player.Health.Health.Value, Is.EqualTo(expected[0].Health));
                    Assert.That(player.Health.ConfiguredProfileFamily, Is.EqualTo(BmsGaugeProfileFamily.Lr2));
                    Assert.That(player.Health.HasFailed, Is.False);
                    Assert.That(player.Health.HasEverFailed, Is.False);
                });
            });
            AddWaitStep("allow subsequent updates", 3);
            AddStep("damage current stage and save its result", () =>
            {
                Assert.That(player.Health.CurrentGaugeStates, Is.EqualTo(expected));
                var tierAtStart = player.Health.GaugeType;
                if (autoGauge && stageIndex < 2)
                {
                    for (var bad = 0; player.Health.GaugeType == tierAtStart && bad < 200; bad++)
                        player.Health.RegisterNonConsumingJudgement(HitResult.Ok, 1000 + bad);
                    Assert.That(player.Health.GaugeType, Is.Not.EqualTo(tierAtStart));
                }
                else
                    player.Health.RegisterNonConsumingJudgement(HitResult.Ok, 1000);

                expected = player.Health.CurrentGaugeStates.Where(state => !state.Failed).ToArray();
                saved[stageIndex] = player.CompleteStage();
                Assert.That(session.CurrentHealth, Is.EqualTo(expected[0].Health));
                player.Exit();
            });
            AddUntilStep("return from stage", () => Stack.CurrentScreen == songSelect);
        }

        AddStep("verify independent saved stage openings", () =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Passed));
            for (var stage = 0; stage < saved.Length; stage++)
            {
                var archived = BmsTestReplayArchive.RoundTrip(saved[stage].DeepClone());
                var reloaded = new ScoreInfo { Ruleset = archived.ScoreInfo.Ruleset, ModsJson = archived.ScoreInfo.ModsJson };
                using var health = new BmsHealthProcessor();
                health.ApplyBeatmap(new BmsBeatmap { LayoutVariant = layouts[stage] });
                foreach (var mod in reloaded.Mods.OfType<IApplicableToHealthProcessor>())
                    mod.ApplyToHealthProcessor(health);
                Assert.That(health.CurrentGaugeStates, Is.EqualTo(initial[stage]));
            }
        });
    }

    private partial class InheritanceCoursePlayer(BmsCourseSession session) : BmsCoursePlayer(session)
    {
        public BmsHealthProcessor Health => (BmsHealthProcessor)GameplayState.HealthProcessor;

        protected override bool PauseOnFocusLost => false;

        public void StopClock() => GameplayClockContainer.Stop();

        public Score CompleteStage()
        {
            DrawableRuleset.SetRecordTarget(null);
            ScoreProcessor.PopulateScore(Score.ScoreInfo);
            Score.ScoreInfo.Passed = Health.HasPassedAtEnd();
            Assert.That(Score.ScoreInfo.Passed, Is.True);
            GameplayState.HasPassed = true;
            var score = Score.DeepClone();
            using var results = CreateResults(score.ScoreInfo);
            return score;
        }
    }
}
