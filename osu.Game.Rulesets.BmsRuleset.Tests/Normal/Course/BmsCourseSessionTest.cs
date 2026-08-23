using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course;

[TestFixture]
public class BmsCourseSessionTest
{

    [SetUp]
    public void SetUp()
    {
        courseResultsDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"course-results-{Guid.NewGuid():N}");
        Directory.CreateDirectory(courseResultsDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(courseResultsDirectory))
            Directory.Delete(courseResultsDirectory, true);
    }

    private string courseResultsDirectory = null!;

    [TestCase(BmsGaugeType.Class, typeof(BmsModClassGauge))]
    [TestCase(BmsGaugeType.ExClass, typeof(BmsModExClassGauge))]
    [TestCase(BmsGaugeType.ExHardClass, typeof(BmsModExHardClassGauge))]
    public void TestCourseGaugeModCreation(BmsGaugeType gaugeType, Type expectedModType)
    {
        Assert.That(BmsCourseSession.CreateGaugeMod(gaugeType), Is.TypeOf(expectedModType));
    }

    [TestCase("gauge_5k", BmsGaugeProfileFamily.FiveKeys)]
    [TestCase("gauge_7k", BmsGaugeProfileFamily.SevenKeys)]
    [TestCase("gauge_9k", BmsGaugeProfileFamily.Pms)]
    [TestCase("gauge_24k", BmsGaugeProfileFamily.Keyboard)]
    [TestCase("gauge_lr2", BmsGaugeProfileFamily.Lr2)]
    public void TestCourseGaugeProfileFamilySelection(string constraint, BmsGaugeProfileFamily expected)
    {
        Assert.That(BmsCourseSession.ResolveCourseGaugeProfileFamily([constraint]), Is.EqualTo(expected));
    }

    private static BmsCourseSession createSession(int stageCount = 3, IReadOnlyList<Mod> mods = null, BmsGaugeType gaugeType = BmsGaugeType.Class)
    {
        var stages = Enumerable.Range(1, stageCount)
            .Select(index => new BmsResolvedCourseStage(
                new BmsCourseStage($"Stage {index}", $"Level {index}", BeatmapHash: $"hash-{index}"),
                new BeatmapInfo { Hash = $"hash-{index}" }))
            .ToArray();
        var course = new BmsCourseDefinition("course", "Table", "Course", stages.Select(stage => stage.Definition).ToArray(), "Class", []);
        return new BmsCourseSession(course, stages, mods ?? [], gaugeType);
    }

    private static BmsGaugeStateSnapshot[] gaugeStates(double health, bool failed = false) =>
        [new(BmsGaugeType.Class, health, failed)];

    private static ScoreInfo createScore(bool passed, long totalScore = 0) => new()
    {
        Passed = passed,
        TotalScore = totalScore,
    };

    private static BmsCourseAttemptData attempt(BmsGaugeType gaugeType) => new()
    {
        Status = BmsCourseStatus.Passed,
        GaugeType = gaugeType,
        Stages = [],
    };

    [Test]
    public void TestAbortDuringStageStoresPartialScore()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.AbortCurrentStage(createScore(false, 1000), gaugeStates(0.3));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Aborted));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Aborted));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(1000));
            Assert.That(session.CurrentStage.EndingHealth, Is.EqualTo(0.3));
        });
    }

    [Test]
    public void TestAbortFromStageResultsPreservesPassedScore()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true, 2000), gaugeStates(0.6));
        session.AbortAfterStageResult();

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Aborted));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(2000));
        });
    }

    [Test]
    public void TestAbortedCourseCreatesResultWhenScoreIsAvailable()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("aborted-course", BmsCourseStatus.Aborted, ScoreRank.F, createScore(false, 1000));

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet("aborted-course", out var result), Is.True);
            Assert.That(result.Lamp, Is.EqualTo(BmsLamp.Failed));
            Assert.That(result.Rank, Is.EqualTo(ScoreRank.F));
            Assert.That(result.Score?.TotalScore, Is.EqualTo(1000));
        });
    }

    [Test]
    public void TestCourseAutoGaugeDoesNotRestoreFailedGaugesWhenModIsAppliedAfterSessionConfiguration()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true),
        [
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ]);
        session.RequestAdvance();
        session.Advance();

        var healthProcessor = new BmsHealthProcessor();
        session.ConfigureHealthProcessor(healthProcessor);
        new BmsModAutoGauge().ApplyToHealthProcessor(healthProcessor);

        Assert.That(healthProcessor.CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
        [
            BmsGaugeType.ExClass,
            BmsGaugeType.Class,
        ]));
    }

    [Test]
    public void TestCourseAutoGaugeOmitsFailedGaugesFromNextStage()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true),
        [
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ]);
        session.RequestAdvance();
        session.Advance();

        var healthProcessor = new BmsHealthProcessor();
        session.ConfigureHealthProcessor(healthProcessor);

        Assert.That(healthProcessor.CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
        [
            BmsGaugeType.ExClass,
            BmsGaugeType.Class,
        ]));
    }

    [Test]
    public void TestCourseAutoGaugeReplacesRegularGaugeChain()
    {
        var healthProcessor = new BmsHealthProcessor();
        new BmsModAutoGauge().ApplyToHealthProcessor(healthProcessor);
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);

        session.ConfigureHealthProcessor(healthProcessor);

        Assert.Multiple(() =>
        {
            Assert.That(healthProcessor.IsCourseGaugeMode, Is.True);
            Assert.That(healthProcessor.CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
            [
                BmsGaugeType.ExHardClass,
                BmsGaugeType.ExClass,
                BmsGaugeType.Class,
            ]));
            Assert.That(healthProcessor.CurrentGaugeStates, Has.None.Matches<BmsGaugeStateSnapshot>(state => state.GaugeType == BmsGaugeType.Normal));
        });
    }

    [Test]
    public void TestCourseCannotAdvanceAfterEnding()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), gaugeStates(0.5));
        session.RequestAdvance();
        session.AbortAfterStageResult();

        Assert.Throws<InvalidOperationException>(session.Advance);
        Assert.That(session.CurrentStageIndex, Is.Zero);
    }

    [Test]
    public void TestCourseCarriesEveryGaugeStateThroughSameList()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        BmsGaugeStateSnapshot[] states =
        [
            new(BmsGaugeType.ExHardClass, 0, true),
            new(BmsGaugeType.ExClass, 0.42, false),
            new(BmsGaugeType.Class, 0.81, false),
        ];

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), states);

        Assert.Multiple(() =>
        {
            Assert.That(session.CurrentGaugeStates, Is.EqualTo(states));
            Assert.That(session.CurrentHealth, Is.EqualTo(0.42));
        });
    }

    [Test]
    public void TestCourseGaugeConfigurationInitializesWithCarriedHealth()
    {
        var healthProcessor = new BmsHealthProcessor();
        var session = createSession();
        var observedHealth = new List<double>();
        healthProcessor.Health.ValueChanged += change => observedHealth.Add(change.NewValue);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), gaugeStates(0.42));
        session.ConfigureHealthProcessor(healthProcessor);

        Assert.That(healthProcessor.Health.Value, Is.EqualTo(0.42).Within(0.001));

        new BmsModClassGauge().ApplyToHealthProcessor(healthProcessor);

        Assert.Multiple(() =>
        {
            Assert.That(healthProcessor.Health.Value, Is.EqualTo(0.42).Within(0.001));
            Assert.That(healthProcessor.CurrentGaugeStates.Single().Health, Is.EqualTo(0.42).Within(0.001));
            Assert.That(observedHealth, Is.EqualTo([0.42]).Within(0.001));
        });
    }

    [Test]
    public void TestCourseGaugeSelectionMapsToClassTiers()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BmsCourseSession.ResolveCourseGaugeType([]), Is.EqualTo(BmsGaugeType.Class));
            Assert.That(BmsCourseSession.ResolveCourseGaugeType([new BmsModEasyGauge()]), Is.EqualTo(BmsGaugeType.Class));
            Assert.That(BmsCourseSession.ResolveCourseGaugeType([new BmsModHardGauge()]), Is.EqualTo(BmsGaugeType.ExClass));
            Assert.That(BmsCourseSession.ResolveCourseGaugeType([new BmsModHazardGauge()]), Is.EqualTo(BmsGaugeType.ExHardClass));
            Assert.That(BmsCourseSession.ResolveCourseGaugeType([new BmsModAutoGauge()]), Is.EqualTo(BmsGaugeType.ExHardClass));
        });
    }

    [Test]
    public void TestCourseLampPersistsAndDoesNotDowngradeClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Failed);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Failed));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.F));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Failed));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.F));

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A);
        store.Record("course", BmsCourseStatus.Aborted);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Clear));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));
    }

    [TestCase(BmsGaugeType.Class, BmsLamp.Clear)]
    [TestCase(BmsGaugeType.ExClass, BmsLamp.HardClear)]
    [TestCase(BmsGaugeType.ExHardClass, BmsLamp.ExHardClear)]
    public void TestCourseLampReflectsClassTierOnPass(BmsGaugeType gaugeType, BmsLamp expectedLamp)
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(gaugeType));

        Assert.That(store.GetLamp("course"), Is.EqualTo(expectedLamp));
    }

    [Test]
    public void TestCourseLampDefaultsToClearWithoutAttemptData()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A);

        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Clear));
    }

    [TestCase(BmsGaugeType.Class)]
    [TestCase(BmsGaugeType.ExClass)]
    [TestCase(BmsGaugeType.ExHardClass)]
    public void TestCourseLampIgnoresTierOnFail(BmsGaugeType gaugeType)
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, attempt: attempt(gaugeType));

        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Failed));
    }

    [Test]
    public void TestCourseLampDoesNotDowngradeHardOrExHardClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExHardClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.Class));

        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.ExHardClear));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.ExHardClear));
    }

    [Test]
    public void TestCourseLampSurvivesAbortedResultsAndDoesNotDowngradeHardClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 300_000), attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, attempt: attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Aborted, ScoreRank.F, createScore(false, 100_000));

        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.HardClear));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.HardClear));
    }

    [Test]
    public void TestCourseRankDoesNotFollowLampHierarchy()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, createScore(true, 900_000), attempt(BmsGaugeType.ExHardClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 800_000), attempt(BmsGaugeType.Class));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 700_000), attempt(BmsGaugeType.Class));

        Assert.Multiple(() =>
        {
            Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.ExHardClear));
            Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));
        });

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));
    }

    [Test]
    public void TestCourseModsKeepAutoGaugeAndReplaceSelectedGauge()
    {
        var mirror = new BmsModMirror();
        var mods = BmsCourseSession.CreateCourseMods([mirror, new BmsModHardGauge(), new BmsModAutoGauge()], BmsGaugeType.ExHardClass);

        Assert.Multiple(() =>
        {
            Assert.That(mods, Has.Count.EqualTo(2));
            Assert.That(mods.Single(mod => mod is BmsModMirror), Is.Not.SameAs(mirror));
            Assert.That(mods, Has.One.TypeOf<BmsModAutoGauge>());
            Assert.That(mods, Has.None.TypeOf<BmsModGauge>());
        });
    }

    [Test]
    public void TestCourseModsUseResolvedClassGauge()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModHardGauge()], BmsGaugeType.ExClass);

        Assert.That(mods.Single(), Is.TypeOf<BmsModExClassGauge>());
    }

    [Test]
    public void TestCourseResultJsonIsLoadedOnDemand()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));

        store = new BmsCourseResultStore(courseResultsDirectory);
        File.Delete(Directory.EnumerateFiles(courseResultsDirectory, "*.json").Single());

        Assert.Multiple(() =>
        {
            Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Clear));
            Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));
            Assert.That(store.GetHistory("course"), Is.Empty);
        });
    }

    [Test]
    public void TestCourseResultsPersistAsIndividualFilesAndNotify()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        var changed = new List<string>();
        store.Changed += changed.Add;

        store.Record("course/with unsafe characters", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));
        store.Record("course/with unsafe characters", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 500_000));
        store.Record("other", BmsCourseStatus.Passed, ScoreRank.A, createScore(true, 700_000));

        Assert.Multiple(() =>
        {
            Assert.That(Directory.EnumerateFiles(courseResultsDirectory, "*.json").Count(), Is.EqualTo(3));
            Assert.That(changed, Is.EqualTo(["course/with unsafe characters", "course/with unsafe characters", "other"]));
        });

        store = new BmsCourseResultStore(courseResultsDirectory);

        Assert.Multiple(() =>
        {
            Assert.That(store.GetHistory("course/with unsafe characters").Select(result => result.Score?.TotalScore),
                Is.EqualTo(new long?[] { 900_000, 500_000 }));
            Assert.That(store.GetHistory("other").Single().Score?.TotalScore, Is.EqualTo(700_000));
        });
    }

    [Test]
    public void TestCourseScoreCloneRetainsGaugeHistory()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        var score = createScore(true);
        BmsScoreGaugeHistoryStore.Set(score,
        [
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.ExClass,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
            ]),
        ]);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(score,
        [
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ]);

        Assert.That(BmsScoreGaugeHistoryStore.TryGet(session.CurrentStage.Score!, out var history), Is.True);
        Assert.That(history.Single().States.Select(state => state.GaugeType), Is.EqualTo(
        [
            BmsGaugeType.ExHardClass,
            BmsGaugeType.ExClass,
            BmsGaugeType.Class,
        ]));
    }

    [Test]
    public async Task TestCourseScoreHistoryLoadsAsynchronouslyAndSupportsCancellation()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 500_000));

        store = new BmsCourseResultStore(courseResultsDirectory);
        var history = await store.GetHistoryAsync("course", CancellationToken.None);

        Assert.That(history.Select(result => result.Score?.TotalScore), Is.EqualTo(new long?[] { 900_000, 500_000 }));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellationToken = cancelled.Token;
        Assert.ThrowsAsync<OperationCanceledException>(async () => await store.GetHistoryAsync("other", cancellationToken));
    }

    [Test]
    public void TestCourseScoreHistoryPersists()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        var firstScore = createScore(true, 900_000);
        firstScore.Rank = ScoreRank.S;
        var firstAttempt = new BmsCourseAttemptData
        {
            Status = BmsCourseStatus.Passed,
            GaugeType = BmsGaugeType.Class,
            Stages =
            [
                new BmsCourseStageAttemptData
                {
                    BeatmapHash = "first-stage",
                    Status = BmsCourseStageStatus.Passed,
                    ScoreId = firstScore.ID,
                    EndingHealth = 0.75,
                },
            ],
        };
        var secondScore = createScore(true, 800_000);
        secondScore.Rank = ScoreRank.A;

        store.Record("course", BmsCourseStatus.Passed, firstScore.Rank, firstScore, firstAttempt);
        store.Record("course", BmsCourseStatus.Passed, secondScore.Rank, secondScore);

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(store.TryGet("course", out var result), Is.True);
        Assert.That(result.Score?.TotalScore, Is.EqualTo(900_000));
        Assert.That(result.Rank, Is.EqualTo(ScoreRank.S));
        Assert.That(store.GetHistory("course").Select(history => history.Score?.TotalScore),
            Is.EqualTo(new long?[] { 900_000, 800_000 }));
        Assert.That(store.GetHistory("course")[0].Attempt?.Stages.Single().ScoreId, Is.EqualTo(firstScore.ID));
    }

    [Test]
    public void TestCourseStartsWithoutGaugeStateToRestore()
    {
        var singleGauge = createSession(mods: [new BmsModExClassGauge()], gaugeType: BmsGaugeType.ExClass);
        var autoGauge = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);

        Assert.Multiple(() =>
        {
            Assert.That(singleGauge.CurrentGaugeStates, Is.Empty);
            Assert.That(autoGauge.CurrentGaugeStates, Is.Empty);
        });
    }

    [Test]
    public void TestEmptyAbortedCourseDoesNotCreateResult()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("empty-aborted-course", BmsCourseStatus.Aborted);

        Assert.That(store.TryGet("empty-aborted-course", out _), Is.False);
    }

    [Test]
    public void TestFailureStoresPartialScoreAndEndsCourse()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.FailCurrentStage(createScore(false, 654321), gaugeStates(0, true));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Failed));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Failed));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(654321));
            Assert.That(session.CurrentHealth, Is.Zero);
            Assert.That(session.Stages[1].Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
            Assert.That(session.Stages[1].Score, Is.Null);
        });
    }

    [Test]
    public void TestFinalPassCompletesCourse()
    {
        var session = createSession(1);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), gaugeStates(0.75));

        Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Passed));
        Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
        Assert.Throws<InvalidOperationException>(session.RequestAdvance);
    }

    [Test]
    public void TestPassedStageCarriesHealthAndAdvances()
    {
        var session = createSession();
        var score = createScore(true, 123456);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(score, gaugeStates(0.42));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.InProgress));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
            Assert.That(session.CurrentHealth, Is.EqualTo(0.42));
            Assert.That(session.CurrentStage.Score, Is.Not.SameAs(score));
        });

        session.RequestAdvance();
        session.Advance();

        Assert.Multiple(() =>
        {
            Assert.That(session.CurrentStageIndex, Is.EqualTo(1));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
            Assert.That(session.AdvanceRequested, Is.False);
            Assert.That(session.CurrentHealth, Is.EqualTo(0.42));
        });
    }
}
