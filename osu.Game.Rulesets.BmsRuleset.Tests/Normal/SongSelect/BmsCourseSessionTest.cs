using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
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
        session.CompleteCurrentStage(score, 0.42);

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
        session.CompleteCurrentStage(createScore(true), 0.75);

        Assert.That(session.Status, Is.EqualTo(BmsCourseStatus.Passed));
        Assert.That(session.CurrentStage.Status, Is.EqualTo(BmsCourseStageStatus.Passed));
        Assert.Throws<InvalidOperationException>(session.RequestAdvance);
    }

    [Test]
    public void TestFailureStoresPartialScoreAndEndsCourse()
    {
        var session = createSession();

        session.BeginCurrentStage();
        session.FailCurrentStage(createScore(false, 654321), -0.1);

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
        session.AbortCurrentStage(createScore(false, 1000), 0.3);

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
        session.CompleteCurrentStage(createScore(true, 2000), 0.6);
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
        session.CompleteCurrentStage(createScore(true), 0.5);
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
    public void TestCourseModsOverrideSelectedGaugeMods()
    {
        var mirror = new BmsModMirror();
        var mods = BmsCourseSession.CreateCourseMods(
            [mirror, new BmsModHardGauge(), new BmsModAutoGauge()],
            BmsGaugeType.ExClass);

        Assert.Multiple(() =>
        {
            Assert.That(mods, Has.Count.EqualTo(2));
            Assert.That(mods.Single(mod => mod is BmsModMirror), Is.Not.SameAs(mirror));
            Assert.That(mods.Count(mod => mod is BmsModGauge), Is.EqualTo(1));
            Assert.That(mods.Single(mod => mod is BmsModGauge), Is.TypeOf<BmsModExClassGauge>());
            Assert.That(mods, Has.None.TypeOf<BmsModAutoGauge>());
        });
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

    private static BmsCourseSession createSession(int stageCount = 3)
    {
        var stages = Enumerable.Range(1, stageCount)
            .Select(index => new BmsResolvedCourseStage(
                new BmsCourseStage($"Stage {index}", $"Level {index}", BeatmapHash: $"hash-{index}"),
                new BeatmapInfo { Hash = $"hash-{index}" }))
            .ToArray();
        var course = new BmsCourseDefinition("course", "Table", "Course", stages.Select(stage => stage.Definition).ToArray(), "Class", []);
        return new BmsCourseSession(course, stages, [], BmsGaugeType.Class);
    }

    private static ScoreInfo createScore(bool passed, long totalScore = 0) => new()
    {
        Passed = passed,
        TotalScore = totalScore,
    };
}
