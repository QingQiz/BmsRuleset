using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsCourseSessionTest
{
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
    public void TestFailureStoresPartialScoreAndEndsCourse()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.FailCurrentStage(createScore(false, 654321), gaugeStates(0, failed: true));

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

    [TestCase("Class", BmsGaugeType.Class, typeof(BmsModClassGauge))]
    [TestCase("EX Class", BmsGaugeType.ExClass, typeof(BmsModExClassGauge))]
    [TestCase("ex-hard-class", BmsGaugeType.ExHardClass, typeof(BmsModExHardClassGauge))]
    public void TestCourseGaugeParsingAndModCreation(string text, BmsGaugeType expectedType, Type expectedModType)
    {
        Assert.That(BmsCourseSession.TryParseGauge(text, out var gaugeType), Is.True);
        Assert.That(gaugeType, Is.EqualTo(expectedType));
        Assert.That(BmsCourseSession.CreateGaugeMod(gaugeType), Is.TypeOf(expectedModType));
    }

    [TestCase("Normal")]
    [TestCase("Hard")]
    [TestCase("Unknown")]
    public void TestNonCourseGaugeIsRejected(string text)
    {
        Assert.That(BmsCourseSession.TryParseGauge(text, out _), Is.False);
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

    [TestCase("gauge_5k", BmsGaugeProfileFamily.FiveKeys)]
    [TestCase("gauge_7k", BmsGaugeProfileFamily.SevenKeys)]
    [TestCase("gauge_9k", BmsGaugeProfileFamily.Pms)]
    [TestCase("gauge_24k", BmsGaugeProfileFamily.Keyboard)]
    [TestCase("gauge_lr2", BmsGaugeProfileFamily.Lr2)]
    public void TestCourseGaugeProfileFamilySelection(string constraint, BmsGaugeProfileFamily expected)
    {
        Assert.That(BmsCourseSession.ResolveCourseGaugeProfileFamily([constraint]), Is.EqualTo(expected));
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
        Assert.That(history.Single().States.Select(state => state.GaugeType), Is.EqualTo(new[]
        {
            BmsGaugeType.ExHardClass,
            BmsGaugeType.ExClass,
            BmsGaugeType.Class,
        }));
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
    public void TestCourseLampPersistsAndDoesNotDowngradeClear()
    {
        var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        var store = new BmsCourseResultStore(config);

        store.Record("course", BmsCourseStatus.Failed);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Failed));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.F));

        store = new BmsCourseResultStore(config);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Failed));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.F));

        store.Record("course", BmsCourseStatus.Passed, ScoreRank.S);
        store.Record("course", BmsCourseStatus.Passed, ScoreRank.A);
        store.Record("course", BmsCourseStatus.Aborted);
        Assert.That(store.GetLamp("course"), Is.EqualTo(BmsLamp.Clear));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));

        store = new BmsCourseResultStore(config);
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.S));
    }

    [Test]
    public void TestAbortedCourseCreatesResultWhenScoreIsAvailable()
    {
        var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        var store = new BmsCourseResultStore(config);

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
    public void TestEmptyAbortedCourseDoesNotCreateResult()
    {
        var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        var store = new BmsCourseResultStore(config);

        store.Record("empty-aborted-course", BmsCourseStatus.Aborted);

        Assert.That(store.TryGet("empty-aborted-course", out _), Is.False);
    }

    [Test]
    public void TestCourseScoreHistoryPersists()
    {
        var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        var store = new BmsCourseResultStore(config);
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

        store = new BmsCourseResultStore(config);
        Assert.That(store.TryGet("course", out var result), Is.True);
        Assert.That(result.Score?.TotalScore, Is.EqualTo(900_000));
        Assert.That(result.Rank, Is.EqualTo(ScoreRank.S));
        Assert.That(store.GetHistory("course").Select(history => history.Score?.TotalScore),
            Is.EqualTo(new long?[] { 900_000, 800_000 }));
        Assert.That(store.GetHistory("course")[0].Attempt?.Stages.Single().ScoreId, Is.EqualTo(firstScore.ID));
    }

    [Test]
    public void TestSingleResultStorageMigratesToHistory()
    {
        var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);
        var score = createScore(true, 700_000);
        score.Rank = ScoreRank.A;
        config.SetValue(BmsRulesetSetting.CourseResults, JsonSerializer.Serialize(new Dictionary<string, BmsCourseResult>
        {
            ["course"] = new(BmsLamp.Clear, ScoreRank.A, BmsCourseScoreData.From(score)),
        }));

        var store = new BmsCourseResultStore(config);

        Assert.That(store.GetHistory("course").Single().Score?.TotalScore, Is.EqualTo(700_000));
        Assert.That(store.GetRank("course"), Is.EqualTo(ScoreRank.A));
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
}
