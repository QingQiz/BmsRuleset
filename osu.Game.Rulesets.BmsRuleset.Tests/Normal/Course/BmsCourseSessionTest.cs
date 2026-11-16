using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Randomisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course;

[TestFixture]
public partial class BmsCourseSessionTest
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
        var course = new BmsCourseDefinition("course", "Table", "Course", stages.Select(stage => stage.Definition).ToArray(), []);
        return new BmsCourseSession(course, stages, BmsCourseSession.CreateCourseMods(mods ?? [], gaugeType), gaugeType);
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
        Stages = [new BmsCourseStageAttemptData { Status = BmsCourseStageStatus.Passed, ScoreId = Guid.NewGuid() }],
    };

    [Test]
    public async Task TestCourseRecordUsesFinalGaugeTierOverStartingTier()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        // Auto Gauge starts at ExHardClass; passing on the surviving Class tier must
        // record the NORMAL lamp rather than the starting tier's EX-HARD.
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExHardClass), finalGaugeType: BmsGaugeType.Class);

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(BmsLamp.Clear));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(BmsLamp.Clear));
    }

    [Test]
    public void TestAbortDuringStageStoresPartialScore()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.AbortCurrentStage(createScore(false, 1000), BmsTestHealth.ToState(gaugeStates(0.3)));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Aborted));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Aborted));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(1000));
            Assert.That(session.CourseGauges(), Is.EqualTo(gaugeStates(0.3)));
        });
    }

    [Test]
    public void TestAbortFromStageResultsPreservesPassedScore()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true, 2000), BmsTestHealth.ToState(gaugeStates(0.6)));
        session.AbortAfterStageResult();

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Aborted));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(2000));
        });
    }

    [Test]
    public async Task TestAbortedCourseCreatesResultWhenScoreIsAvailable()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("aborted-course", BmsCourseStatus.Aborted, ScoreRank.F, createScore(false, 1000));
        var result = (await store.GetHistoryAsync("aborted-course", CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Lamp, Is.EqualTo(BmsLamp.Failed));
            Assert.That(result.Rank, Is.EqualTo(ScoreRank.F));
            Assert.That(result.Score?.TotalScore, Is.EqualTo(1000));
        });
    }

    [Test]
    public void TestCourseAutoGaugeOmitsFailedGaugesFromNextStage()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState([
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ]));
        session.RequestAdvance();
        session.Advance();

        var healthProcessor = new BmsHealthProcessor();
        foreach (var mod in session.Mods.OfType<IApplicableToHealthProcessor>())
            mod.ApplyToHealthProcessor(healthProcessor);

        Assert.That(healthProcessor.Gauge().CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
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

        foreach (var mod in session.Mods.OfType<IApplicableToHealthProcessor>())
            mod.ApplyToHealthProcessor(healthProcessor);

        Assert.Multiple(() =>
        {
            Assert.That(healthProcessor.Gauge().IsCourseGaugeMode, Is.True);
            Assert.That(healthProcessor.Gauge().CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
            [
                BmsGaugeType.ExHardClass,
                BmsGaugeType.ExClass,
                BmsGaugeType.Class,
            ]));
            Assert.That(healthProcessor.Gauge().CurrentGaugeStates, Has.None.Matches<BmsGaugeStateSnapshot>(state => state.GaugeType == BmsGaugeType.Normal));
        });
    }

    [Test]
    public void TestCourseCannotAdvanceAfterEnding()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState(gaugeStates(0.5)));
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
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState(states));

        Assert.That(session.CourseGauges(), Is.EqualTo(states));
    }

    [Test]
    public void TestCourseGaugeModInitializesWithCarriedHealth()
    {
        var healthProcessor = new BmsHealthProcessor();
        var session = createSession(mods: [new BmsModClassGauge()]);
        var observedHealth = new List<double>();
        healthProcessor.Health.ValueChanged += change => observedHealth.Add(change.NewValue);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState(gaugeStates(0.42)));
        session.RequestAdvance();
        session.Advance();
        session.Mods.OfType<BmsModClassGauge>().Single().ApplyToHealthProcessor(healthProcessor);

        Assert.Multiple(() =>
        {
            Assert.That(healthProcessor.Health.Value, Is.EqualTo(0.42).Within(0.001));
            Assert.That(healthProcessor.Gauge().CurrentGaugeStates.Single().Health, Is.EqualTo(0.42).Within(0.001));
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
    public async Task TestCourseLampPersistsAndDoesNotDowngradeClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Failed, attempt: attempt(BmsGaugeType.Class));
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []),
            Is.EqualTo((BmsLamp.Failed, (ScoreRank?)ScoreRank.F)));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []),
            Is.EqualTo((BmsLamp.Failed, (ScoreRank?)ScoreRank.F)));

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, attempt: attempt(BmsGaugeType.Class));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.Class));
        store.Record("course", BmsCourseStatus.Aborted);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []),
            Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Rank, Is.EqualTo(ScoreRank.S));
    }

    [TestCase(BmsGaugeType.Class, BmsLamp.Clear)]
    [TestCase(BmsGaugeType.ExClass, BmsLamp.HardClear)]
    [TestCase(BmsGaugeType.ExHardClass, BmsLamp.ExHardClear)]
    public async Task TestCourseLampReflectsClassTierOnPass(BmsGaugeType gaugeType, BmsLamp expectedLamp)
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(gaugeType));

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(expectedLamp));
    }

    [TestCase(BmsGaugeType.Class, BmsLamp.Clear)]
    [TestCase(BmsGaugeType.ExClass, BmsLamp.HardClear)]
    [TestCase(BmsGaugeType.ExHardClass, BmsLamp.ExHardClear)]
    public async Task TestCourseLampUsesFinalGaugeTierOnPass(BmsGaugeType finalGaugeType, BmsLamp expectedLamp)
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        // Auto Gauge starts at ExHardClass but can conclude on any surviving tier.
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExHardClass), finalGaugeType: finalGaugeType);

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(expectedLamp));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(expectedLamp));
    }

    [Test]
    public async Task TestCourseLampFallsBackToStartingTierWithoutFinalGauge()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExHardClass));

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(BmsLamp.ExHardClear));
    }

    [Test]
    public async Task TestCourseLampDefaultsToClearWithoutAttemptData()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A);

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(BmsLamp.Clear));
    }

    [TestCase(BmsGaugeType.Class)]
    [TestCase(BmsGaugeType.ExClass)]
    [TestCase(BmsGaugeType.ExHardClass)]
    public async Task TestCourseLampIgnoresTierOnFail(BmsGaugeType gaugeType)
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, attempt: attempt(gaugeType));

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Lamp, Is.EqualTo(BmsLamp.Failed));
    }

    [Test]
    public async Task TestCourseLampDoesNotDowngradeHardOrExHardClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExHardClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.Class));

        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Lamp, Is.EqualTo(BmsLamp.ExHardClear));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Lamp, Is.EqualTo(BmsLamp.ExHardClear));
    }

    [Test]
    public async Task TestCourseLampSurvivesAbortedResultsAndDoesNotDowngradeHardClear()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, attempt: attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 300_000), attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, attempt: attempt(BmsGaugeType.ExClass));
        store.Record("course", BmsCourseStatus.Aborted, ScoreRank.F, createScore(false, 100_000));

        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Lamp, Is.EqualTo(BmsLamp.HardClear));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Lamp, Is.EqualTo(BmsLamp.HardClear));
    }

    [Test]
    public async Task TestCourseRankDoesNotFollowLampHierarchy()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A, createScore(true, 900_000), attempt(BmsGaugeType.ExHardClass));
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 800_000), attempt(BmsGaugeType.Class));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 700_000), attempt(BmsGaugeType.Class));

        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []),
            Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That(BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync("course", CancellationToken.None), []).Rank, Is.EqualTo(ScoreRank.S));
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
    public void TestCourseModsDropAutoplay()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModAutoplay()], BmsGaugeType.Class);

        Assert.Multiple(() =>
        {
            Assert.That(mods, Has.None.TypeOf<BmsModAutoplay>());
            Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
        });
    }

    [Test]
    public void TestCourseModsUseResolvedClassGauge()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModHardGauge()], BmsGaugeType.ExClass);

        Assert.That(mods.Single(), Is.TypeOf<BmsModExClassGauge>());
    }

    [TestCase("no_good", typeof(BmsModNoGood))]
    [TestCase("no_great", typeof(BmsModNoGreat))]
    [TestCase("ln", typeof(BmsModLongNote))]
    [TestCase("cn", typeof(BmsModChargeNote))]
    [TestCase("hcn", typeof(BmsModHellChargeNote))]
    public void TestCourseConstraintModsAreCreated(string constraint, Type expectedModType)
    {
        var mods = BmsCourseSession.CreateCourseMods([], BmsGaugeType.Class, [constraint]);

        Assert.That(mods, Has.One.TypeOf(expectedModType));
    }

    [Test]
    public void TestCourseJudgementConstraintOverridesPlayerSelection()
    {
        var upgraded = BmsCourseSession.CreateCourseMods([new BmsModNoGood()], BmsGaugeType.Class, ["no_great"]);

        Assert.That(upgraded, Has.One.TypeOf<BmsModNoGreat>());
        Assert.That(upgraded, Has.None.TypeOf<BmsModNoGood>());

        var replaced = BmsCourseSession.CreateCourseMods([new BmsModNoGreat()], BmsGaugeType.Class, ["no_good"]);

        Assert.That(replaced, Has.One.TypeOf<BmsModNoGood>());
        Assert.That(replaced, Has.None.TypeOf<BmsModNoGreat>());

        var same = BmsCourseSession.CreateCourseMods([new BmsModNoGood()], BmsGaugeType.Class, ["no_good"]);
        Assert.That(same, Has.One.TypeOf<BmsModNoGood>());
    }

    [Test]
    public void TestNoSpeedConstraintIsNotACourseMod()
    {
        var mods = BmsCourseSession.CreateCourseMods([], BmsGaugeType.Class, ["no_speed"]);

        Assert.That(mods, Has.Count.EqualTo(1));
        Assert.That(mods.Single(), Is.TypeOf<BmsModClassGauge>());
    }

    [Test]
    public void TestNoSpeedConstraintForbidsConstant()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModConstant()], BmsGaugeType.Class, ["no_speed"]);

        Assert.That(mods, Has.None.TypeOf<BmsModConstant>());
        Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
    }

    [Test]
    public void TestGradeConstraintRemovesMirrorAndShuffles()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModMirror(), new BmsModLaneRandom()], BmsGaugeType.Class, ["grade"]);

        Assert.That(mods, Has.None.TypeOf<BmsModMirror>());
        Assert.That(mods, Has.None.TypeOf<BmsModLaneRandom>());
        Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
    }

    [Test]
    public void TestGradeMirrorRemovesShufflesKeepsMirror()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModMirror()], BmsGaugeType.Class, ["grade_mirror"]);

        Assert.That(mods, Has.One.TypeOf<BmsModMirror>());
        Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());

        var withRandom = BmsCourseSession.CreateCourseMods([new BmsModLaneRandom()], BmsGaugeType.Class, ["grade_mirror"]);

        Assert.That(withRandom, Has.None.TypeOf<BmsModLaneRandom>());
    }

    [Test]
    public void TestGradeRandomRemovesNothing()
    {
        var mods = BmsCourseSession.CreateCourseMods([new BmsModMirror()], BmsGaugeType.Class, ["grade_random"]);

        Assert.That(mods, Has.One.TypeOf<BmsModMirror>());
        Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
    }

    [Test]
    public void TestCourseConstraintModsMapRealCourseConstraints()
    {
        var mods = BmsCourseSession.CreateCourseMods([], BmsGaugeType.Class, ["grade_mirror", "gauge_lr2", "ln", "no_good", "no_speed"]);

        Assert.That(mods, Has.Count.EqualTo(3));
        Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
        Assert.That(mods, Has.One.TypeOf<BmsModNoGood>());
        Assert.That(mods, Has.One.TypeOf<BmsModLongNote>());
    }

    [Test]
    public void TestCourseConstraintModsAreCaseInsensitive()
    {
        var mods = BmsCourseSession.CreateCourseMods([], BmsGaugeType.Class, ["LN"]);

        Assert.That(mods, Has.One.TypeOf<BmsModLongNote>());
    }

    [Test]
    public async Task TestCourseResultJsonIsLoadedOnDemand()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));

        store = new BmsCourseResultStore(courseResultsDirectory);
        File.Delete(Directory.EnumerateFiles(courseResultsDirectory, "*.json").Single());

        Assert.That(await store.GetHistoryAsync("course", CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task TestCourseResultWithUnsupportedVersionIsIgnored()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S);
        var path = Directory.EnumerateFiles(courseResultsDirectory, "*.json").Single();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Version\":0", "\"Version\":1", StringComparison.Ordinal));

        store = new BmsCourseResultStore(courseResultsDirectory);

        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)), Is.Empty);
    }

    [Test]
    public async Task TestCourseResultWithoutVersionUsesVersionZero()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S);
        var path = Directory.EnumerateFiles(courseResultsDirectory, "*.json").Single();
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"Version\":0,", string.Empty, StringComparison.Ordinal));

        store = new BmsCourseResultStore(courseResultsDirectory);

        Assert.That(await store.GetHistoryAsync("course", CancellationToken.None), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TestCourseResultsPersistAsIndividualFilesAndNotify()
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
        var history = await store.GetHistoryAsync("course/with unsafe characters", CancellationToken.None);
        var other = await store.GetHistoryAsync("other", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(history.Select(result => result.Score?.TotalScore),
                Is.EqualTo(new long?[] { 900_000, 500_000 }));
            Assert.That(other.Single().Score?.TotalScore, Is.EqualTo(700_000));
        });
    }

    [Test]
    public async Task TestDeletingCourseResultOnlyRemovesSelectedHistoryFile()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 500_000));

        var history = await store.GetHistoryAsync("course", CancellationToken.None);
        Assert.That(history, Has.Count.EqualTo(2));

        var changed = new List<string>();
        store.Changed += changed.Add;

        Assert.That(store.Delete("course", history[0]), Is.True);
        var remaining = await store.GetHistoryAsync("course", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(history[0].SourcePath), Is.False);
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining.Single().Score?.TotalScore, Is.EqualTo(500_000));
            Assert.That(Directory.EnumerateFiles(courseResultsDirectory, "*.json"), Has.One.Items);
            Assert.That(changed, Is.EqualTo(["course"]));
        });

        var reloaded = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That((await reloaded.GetHistoryAsync("course", CancellationToken.None)).Single().Score?.TotalScore, Is.EqualTo(500_000));
    }

    [Test]
    public void TestCourseScoreCloneRetainsSummaryAndFinalGaugeStates()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        var score = createScore(true);
        BmsGaugeStateSnapshot[] states =
        [
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ];
        session.BeginCurrentStage();
        session.CompleteCurrentStage(score, BmsTestHealth.ToState(states));

        Assert.That(session.CurrentStage.Score, Is.Not.SameAs(score));
        Assert.That(session.CurrentStage.Score!.TotalScore, Is.EqualTo(score.TotalScore));
        Assert.That(session.CourseGauges(), Is.EqualTo(states).And.Not.SameAs(states));
    }

    [Test]
    public async Task TestCourseHistoryRequestsKeepSnapshotsAndObserveRecordedAndDeletedResults()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 500_000));
        store = new BmsCourseResultStore(courseResultsDirectory);

        var firstRead = store.GetHistoryAsync("course", CancellationToken.None);
        var secondRead = store.GetHistoryAsync("course", CancellationToken.None);
        await store.RecordAsync("course", BmsCourseStatus.Passed, ScoreRank.A, createScore(true, 700_000));
        await Task.WhenAll(firstRead, secondRead);
        var history = await store.GetHistoryAsync("course", CancellationToken.None);
        Assert.That(history.Select(result => result.Score?.TotalScore), Is.EqualTo(new long?[] { 900_000, 500_000, 700_000 }));

        Assert.That(store.Delete("course", history[0]), Is.True);
        var remaining = await store.GetHistoryAsync("course", CancellationToken.None);
        Assert.That(remaining.Select(result => result.Score?.TotalScore), Is.EqualTo(new long?[] { 500_000, 700_000 }));
        Assert.That(history, Has.Count.EqualTo(3), "A published history snapshot must survive later deletion.");
        ((BmsCourseResult[])remaining)[0] = default;
        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Select(result => result.Score?.TotalScore),
            Is.EqualTo(new long?[] { 500_000, 700_000 }), "Mutating a returned array must not modify the cached history.");

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await store.GetHistoryAsync("course", cancelled.Token));
        var reloaded = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That((await reloaded.GetHistoryAsync("course", CancellationToken.None)).Select(result => result.Score?.TotalScore),
            Is.EqualTo(new long?[] { 500_000, 700_000 }));
    }

    [Test]
    public async Task TestCourseHistorySkipsMalformedFileAndRetainsValidResults()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S, createScore(true, 900_000));
        store.Record("course", BmsCourseStatus.Failed, ScoreRank.F, createScore(false, 500_000));
        var broken = (await store.GetHistoryAsync("course", CancellationToken.None))[0].SourcePath!;
        await File.WriteAllTextAsync(broken, "{ invalid JSON");

        store = new BmsCourseResultStore(courseResultsDirectory);
        Assert.That((await store.GetHistoryAsync("course", CancellationToken.None)).Single().Score?.TotalScore, Is.EqualTo(500_000));
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

    [TestCase(false)]
    [TestCase(true)]
    public async Task TestCourseScoreHistoryPersists(bool legacyEndingHealth)
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
                },
            ],
        };
        var secondScore = createScore(true, 800_000);
        secondScore.Rank = ScoreRank.A;

        store.Record("course", BmsCourseStatus.Passed, firstScore.Rank, firstScore, firstAttempt);
        store.Record("course", BmsCourseStatus.Passed, secondScore.Rank, secondScore);

        if (legacyEndingHealth)
        {
            var path = (await store.GetHistoryAsync("course", CancellationToken.None))[0].SourcePath;
            Assert.That(path, Is.Not.Null);
            File.WriteAllText(path!, File.ReadAllText(path!).Replace("\"BeatmapHash\":\"first-stage\",",
                "\"BeatmapHash\":\"first-stage\",\"EndingHealth\":0.75,", StringComparison.Ordinal));
        }

        store = new BmsCourseResultStore(courseResultsDirectory);
        var history = await store.GetHistoryAsync("course", CancellationToken.None);
        var result = history[0];
        Assert.That(result.Score?.TotalScore, Is.EqualTo(900_000));
        Assert.That(result.Rank, Is.EqualTo(ScoreRank.S));
        Assert.That(history.Select(entry => entry.Score?.TotalScore),
            Is.EqualTo(new long?[] { 900_000, 800_000 }));
        Assert.That(history[0].Attempt?.Stages.Single().ScoreId, Is.EqualTo(firstScore.ID));
    }

    [Test]
    public void TestCourseStartsWithoutGaugeStateToRestore()
    {
        var singleGauge = createSession(mods: [new BmsModExClassGauge()], gaugeType: BmsGaugeType.ExClass);
        var autoGauge = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);

        Assert.Multiple(() =>
        {
            Assert.That(singleGauge.CourseGauges(), Is.Empty);
            Assert.That(autoGauge.CourseGauges(), Is.Empty);
        });
    }

    [Test]
    public void TestStageGaugeModsCarryStartingHealthWithoutMutatingEarlierStages()
    {
        var session = createSession(mods: [new BmsModClassGauge()]);
        var firstStage = session.Mods.OfType<BmsModClassGauge>().Single();
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState(gaugeStates(0.42)));
        session.RequestAdvance();
        session.Advance();

        var secondStage = session.Mods.OfType<BmsModClassGauge>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(secondStage, Is.Not.SameAs(firstStage));
            Assert.That(firstStage.InitialHealth.Value, Is.EqualTo(1));
            Assert.That(secondStage.InitialHealth.Value, Is.EqualTo(0.42));
        });
        using var health = new BmsHealthProcessor();
        ((BmsModClassGauge)secondStage.DeepClone()).ApplyToHealthProcessor(health);
        Assert.That(health.Gauge().CurrentGaugeStates, Is.EqualTo(gaugeStates(0.42)));
    }

    [Test]
    public void TestStageAutoGaugeModKeepsOnlySurvivingStartingHealth()
    {
        var session = createSession(mods: [new BmsModAutoGauge()], gaugeType: BmsGaugeType.ExHardClass);
        var firstStage = session.Mods.OfType<BmsModAutoGauge>().Single();
        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState([
            new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
        ]));
        session.RequestAdvance();
        session.Advance();

        var secondStage = session.Mods.OfType<BmsModAutoGauge>().Single();
        using var health = new BmsHealthProcessor();
        ((BmsModAutoGauge)secondStage.DeepClone()).ApplyToHealthProcessor(health);
        Assert.Multiple(() =>
        {
            Assert.That(firstStage.ExHardClassHealth.Value, Is.EqualTo(1));
            Assert.That(secondStage.ExHardClassHealth.Value, Is.Null);
            Assert.That(health.Gauge().CurrentGaugeStates, Is.EqualTo(new[]
            {
                new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
            }));
        });
    }

    [Test]
    public async Task TestEmptyAbortedCourseDoesNotCreateResult()
    {
        var store = new BmsCourseResultStore(courseResultsDirectory);

        store.Record("empty-aborted-course", BmsCourseStatus.Aborted);

        Assert.That(await store.GetHistoryAsync("empty-aborted-course", CancellationToken.None), Is.Empty);
    }

    [Test]
    public void TestFailureStoresPartialScoreAndEndsCourse()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.FailCurrentStage(createScore(false, 654321), BmsTestHealth.ToState(gaugeStates(0, true)));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Failed));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Failed));
            Assert.That(session.CurrentStage.Score?.TotalScore, Is.EqualTo(654321));
            Assert.That(session.CourseGauges(), Is.EqualTo(gaugeStates(0, true)));
            Assert.That(session.Stages[1].Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
            Assert.That(session.Stages[1].Score, Is.Null);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public void TestFailedStageEndsCourse(int stageCount)
    {
        var session = createSession(stageCount);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(false, 100), BmsTestHealth.ToState(gaugeStates(0, true)));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Failed));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Failed));
            Assert.That(session.CanContinue, Is.False);
            Assert.That(session.AdvanceRequested, Is.False);
            Assert.That(session.CurrentStageIndex, Is.Zero);
        });

        Assert.Throws<InvalidOperationException>(session.RequestAdvance);
        Assert.Throws<InvalidOperationException>(session.Advance);
        Assert.Throws<InvalidOperationException>(session.AbortAfterStageResult);
    }

    [Test]
    public void TestFinalPassCompletesCourse()
    {
        var session = createSession(1);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(true), BmsTestHealth.ToState(gaugeStates(0.75)));

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
        session.CompleteCurrentStage(score, BmsTestHealth.ToState(gaugeStates(0.42)));

        Assert.Multiple(() =>
        {
            Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.InProgress));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
            Assert.That(session.CourseGauges(), Is.EqualTo(gaugeStates(0.42)));
            Assert.That(session.CurrentStage.Score, Is.Not.SameAs(score));
        });

        session.RequestAdvance();
        session.Advance();

        Assert.Multiple(() =>
        {
            Assert.That(session.CurrentStageIndex, Is.EqualTo(1));
            Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.NotPlayed));
            Assert.That(session.AdvanceRequested, Is.False);
            Assert.That(session.CourseGauges(), Is.EqualTo(gaugeStates(0.42)));
        });
    }
}
