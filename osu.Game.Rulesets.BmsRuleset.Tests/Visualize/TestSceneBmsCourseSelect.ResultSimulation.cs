#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase(BmsGaugeType.Class, false)]
    [TestCase(BmsGaugeType.ExClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, false)]
    [TestCase(BmsGaugeType.ExHardClass, true)]
    public void TestHistoricalCourseSimulationCarriesHealth(BmsGaugeType gaugeType, bool autoGauge)
    {
        Task import = null!;
        var directory = string.Empty;
        var courseId = Guid.NewGuid().ToString();
        HistoricalCourseStatistics panel = null!;
        BmsCourseSession restored = null!;
        var openings = new BmsGaugeStateSnapshot[3][];
        Task<BmsResultStatisticsData?> detail = null!;

        AddStep("import historical course charts", () =>
        {
            directory = Path.Combine(LocalStorage.GetFullPath(string.Empty), $"dan-result-{courseId}");
            Directory.CreateDirectory(directory);
            for (var stage = 0; stage < 3; stage++)
            {
                var notes = autoGauge ? stage < 2 ? 6 : 2 : 1;
                File.WriteAllText(Path.Combine(directory, $"stage-{stage}.bms"), $"""
                    #PLAYER 1
                    #TITLE Historical Dan {courseId}
                    #SUBTITLE Stage {stage}
                    #ARTIST Test
                    #BPM 120
                    #PLAYLEVEL 1
                    #00111:{string.Concat(Enumerable.Repeat("01", notes))}
                    """);
            }
            import = new BmsFileImporter(Realm, LocalStorage).Import(directory);
        });
        AddUntilStep("historical charts imported", () => import.IsCompleted);
        AddStep("restore course without archived starting health", () =>
        {
            import.GetAwaiter().GetResult();
            var charts = Realm.Run(realm => realm.All<BeatmapInfo>().AsEnumerable()
                .Where(chart => chart.Metadata.Source == directory)
                .OrderBy(chart => chart.DifficultyName)
                .Select(chart => chart.Detach()).ToArray());
            Assert.That(charts, Has.Length.EqualTo(3));
            var definitions = charts.Select(chart => new BmsCourseStage(chart.DifficultyName, "1", BeatmapHash: chart.Hash)).ToArray();
            var course = new BmsCourseDefinition(courseId, "Table", "Dan", definitions, ["gauge_lr2"]);
            var stages = definitions.Zip(charts, (definition, chart) => new BmsResolvedCourseStage(definition, chart)).ToArray();
            var session = new BmsCourseSession(course, stages, autoGauge ? [new BmsModAutoGauge()] : [BmsCourseSession.CreateGaugeMod(gaugeType)], gaugeType);
            var replays = new Dictionary<Guid, Score>();

            for (var stage = 0; stage < stages.Length; stage++)
            {
                session.BeginCurrentStage();
                using var health = new BmsHealthProcessor();
                var beatmap = beatmaps.GetWorkingBeatmap(charts[stage]).Beatmap;
                health.ApplyBeatmap(beatmap);
                session.ConfigureHealthProcessor(health);
                openings[stage] = health.CurrentGaugeStates.ToArray();
                foreach (var note in beatmap.HitObjects)
                    health.RegisterNonConsumingJudgement(HitResult.Meh, note.StartTime);
                Assert.That(health.HasPassedAtEnd(), Is.True);

                var score = new Score
                {
                    ScoreInfo = new ScoreInfo
                    {
                        User = API.LocalUser.Value,
                        BeatmapInfo = charts[stage],
                        BeatmapHash = charts[stage].Hash,
                        Ruleset = charts[stage].Ruleset,
                        Passed = true,
                        Mods = autoGauge
                            ? [new BmsModAutoGauge(), BmsCourseSession.CreateGaugeMod(health.GaugeType)]
                            : [BmsCourseSession.CreateGaugeMod(gaugeType)],
                    },
                    Replay = { Frames = [new BmsReplayFrame(0), new BmsReplayFrame(6000)] },
                };
                replays.Add(score.ScoreInfo.ID, BmsTestReplayArchive.RoundTrip(score, legacy: true));
                session.CompleteCurrentStage(score.ScoreInfo, health.CurrentGaugeStates);
                if (stage + 1 < stages.Length)
                {
                    session.RequestAdvance();
                    session.Advance();
                }
            }

            restored = BmsCourseSession.Restore(course, stages, autoGauge ? [new BmsModAutoGauge()] : [BmsCourseSession.CreateGaugeMod(gaugeType)],
                gaugeType, session.Status, session.Stages.Select(stage => new BmsRestoredCourseStage(stage.Status, stage.Score, stage.EndingHealth)).ToArray());
            Add(panel = new HistoricalCourseStatistics(restored, replays) { RelativeSizeAxes = Axes.Both });
        });
        AddUntilStep("historical summary simulated", () => panel.LoadedStatistics != null);
        AddStep("summary carries health across every stage boundary", () =>
        {
            var gauges = panel.LoadedStatistics!.Gauges;
            Assert.That(gauges.Select(gauge => gauge.Segments.Count), Is.EqualTo(autoGauge ? new[] { 1, 2, 3 } : new[] { 3 }));
            foreach (var gauge in gauges)
            {
                for (var stage = 1; stage < gauge.Segments.Count; stage++)
                    Assert.That(gauge.Segments[stage][0].Health, Is.EqualTo(gauge.Segments[stage - 1][^1].Health));
            }
        });

        for (var stage = 1; stage < 3; stage++)
        {
            var stageIndex = stage;
            AddStep($"load historical stage {stageIndex + 1} details", () => detail = panel.LoadStageStatisticsAsync(restored.Stages[stageIndex].Score!, default));
            AddUntilStep("stage details simulated", () => detail.IsCompleted);
            AddStep("detail starts with each surviving gauge's carried health", () =>
            {
                var data = detail.GetAwaiter().GetResult()!;
                Assert.That(data.Gauges, Has.Count.EqualTo(openings[stageIndex].Length));
                for (var index = 0; index < openings[stageIndex].Length; index++)
                    Assert.That(data.Gauges[index].Points[0].Health, Is.EqualTo(openings[stageIndex][index].Health).Within(1e-6));
                Assert.That(restored.Stages[stageIndex].Score!.Mods.OfType<BmsModGauge>().Single().InitialHealth.Value, Is.Null);
            });
        }
    }

    private partial class HistoricalCourseStatistics(BmsCourseSession session, IReadOnlyDictionary<Guid, Score> replays)
        : BmsCourseAggregateStatistics(session, BmsCourseScoreAggregation.CreateScore(session))
    {
        internal BmsResultStatisticsData? LoadedStatistics { get; private set; }

        protected override async Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
            => LoadedStatistics = await base.LoadStatisticsAsync(score, cancellationToken);

        protected override Task<Score> LoadReplayAsync(ScoreInfo score, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(replays[score.ID]);
        }
    }
}
