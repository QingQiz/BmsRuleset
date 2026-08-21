using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

[TestFixture]
public class BmsExScoreTest
{

    [TestCase(0, 1800, ScoreRank.C, 1000)]
    [TestCase(1000, 1800, ScoreRank.B, 1200)]
    [TestCase(1200, 1800, ScoreRank.A, 1400)]
    [TestCase(1400, 1800, ScoreRank.S, 1600)]
    [TestCase(1600, 1800, ScoreRank.X, 1800)]
    [TestCase(1800, 1800, ScoreRank.X, 1800)]
    public void TestNextTarget(int personalBest, int maximum, ScoreRank expectedRank, int expectedScore)
    {
        var targetRank = BmsExScore.NextRank(BmsExScore.RankFromScore(personalBest, maximum));

        Assert.Multiple(() =>
        {
            Assert.That(targetRank, Is.EqualTo(expectedRank));
            Assert.That(BmsExScore.MinimumScoreForRank(targetRank, maximum), Is.EqualTo(expectedScore));
        });
    }

    [TestCase(1600, 0, 1000, 0)]
    [TestCase(1600, 250, 1000, 400)]
    [TestCase(1600, 1000, 1000, 1600)]
    [TestCase(1600, 2000, 1000, 1600)]
    public void TestLinearTargetProgress(int finalScore, int judgedEvents, int totalEvents, int expected)
    {
        Assert.That(BmsScoreGraph.ScoreAtProgress(finalScore, [], judgedEvents, totalEvents), Is.EqualTo(expected));
    }

    private static BmsJudgementEvent createEvent(BmsJudgementSource source, HitResult result, double? actualTime = null) => new(
        source,
        result,
        [new BmsTimingObservation(BmsTimingObservationKind.Note, source.EndTime, actualTime ?? source.EndTime, 1, result)]);

    [Test]
    public void TestCalculateFromStatistics()
    {
        Dictionary<HitResult, int> statistics = new()
        {
            [HitResult.Perfect] = 10,
            [HitResult.Great] = 3,
            [HitResult.Good] = 20,
        };

        Assert.That(BmsExScore.Calculate(statistics), Is.EqualTo(23));
    }

    [Test]
    public void TestCanonicalProgressionCountsLongNoteResultOnce()
    {
        BmsJudgementEvent[] events =
        [
            new(
                BmsJudgementSource.From(new BmsLongNote()),
                HitResult.Great,
                [
                    new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 990, 1, HitResult.Perfect),
                    new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1510, 1, HitResult.Great),
                ]),
            new(
                BmsJudgementSource.From(new BmsNote()),
                HitResult.Perfect,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 2000, 2000, 1, HitResult.Perfect)]),
            new(
                BmsJudgementSource.From(new BmsNote()),
                HitResult.Great,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 2500, 2500, 1, HitResult.Great)]),
            new(
                BmsJudgementSource.From(new HitObject()),
                HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 3000, 3000, 1, HitResult.Miss)]),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(BmsExScore.CountScoringEvents(events), Is.EqualTo(3));
            Assert.That(BmsExScore.CreateProgression(events), Is.EqualTo([0, 1, 3, 4]));
        });
    }

    [Test]
    public void TestMissingPersonalBestJudgementsRemainUnavailable()
    {
        Assert.That(new BmsScoreGraph.JudgementProgressCursor().GetCountAtTime(1000, HitResult.Perfect), Is.Null);
    }

    [Test]
    public void TestMissingProgressionFallsBackToLinearEstimate()
    {
        Assert.That(BmsScoreGraph.ScoreAtProgress(6, [], 2, 3), Is.EqualTo(4));
    }

    [Test]
    public void TestMissingStatisticsFallsBackToAccuracy()
    {
        var score = new ScoreInfo { Accuracy = 0.75 };

        Assert.That(BmsExScore.Calculate(score, 2000), Is.EqualTo(1500));
    }

    [Test]
    public void TestPersistedProgressionClampsToAvailableEvents()
    {
        Assert.That(BmsScoreGraph.ScoreAtProgress(6, [0, 2, 3], 5, 5), Is.EqualTo(3));
    }

    [Test]
    public void TestPersistedProgressionTakesPriorityOverLinearEstimate()
    {
        Assert.That(BmsScoreGraph.ScoreAtProgress(6, [0, 2, 2, 3], 2, 3), Is.EqualTo(2));
    }

    [Test]
    public void TestPersonalBestJudgementCursorHandlesForwardPlaybackAndRewind()
    {
        BmsJudgementEvent[] events =
        [
            createEvent(new BmsJudgementSource(100, 0, BmsJudgementSourceKind.Note), HitResult.Perfect),
            createEvent(new BmsJudgementSource(200, 0, BmsJudgementSourceKind.Note), HitResult.Great),
            createEvent(new BmsJudgementSource(300, 0, BmsJudgementSourceKind.Note), HitResult.Perfect),
        ];
        var cursor = new BmsScoreGraph.JudgementProgressCursor();
        cursor.SetProgression(BmsScoreGraph.CreateJudgementProgression(events));

        Assert.Multiple(() =>
        {
            Assert.That(cursor.GetCountAtTime(50, HitResult.Perfect), Is.Zero);
            Assert.That(cursor.GetCountAtTime(100, HitResult.Perfect), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(250, HitResult.Great), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(300, HitResult.Perfect), Is.EqualTo(2));
            Assert.That(cursor.GetCountAtTime(150, HitResult.Perfect), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(150, HitResult.Great), Is.Zero);
        });
    }

    [Test]
    public void TestPersonalBestJudgementCursorKeepsSnapshotUntilBoundaryOrSeek()
    {
        BmsJudgementEvent[] events =
        [
            createEvent(new BmsJudgementSource(100, 0, BmsJudgementSourceKind.Note), HitResult.Perfect),
            createEvent(new BmsJudgementSource(200, 0, BmsJudgementSourceKind.Note), HitResult.Great),
        ];
        var cursor = new BmsScoreGraph.JudgementProgressCursor();
        cursor.SetProgression(BmsScoreGraph.CreateJudgementProgression(events));

        var beforeFirst = cursor.GetCountsAtTime(50);
        var first = cursor.GetCountsAtTime(100);

        Assert.Multiple(() =>
        {
            Assert.That(cursor.GetCountsAtTime(150), Is.SameAs(first));
            Assert.That(cursor.GetCountsAtTime(200), Is.Not.SameAs(first));
            Assert.That(cursor.GetCountsAtTime(150), Is.SameAs(first));
            Assert.That(cursor.GetCountsAtTime(50), Is.SameAs(beforeFirst));
        });
    }

    [Test]
    public void TestPersonalBestJudgementsUseCurrentPlaybackTime()
    {
        BmsJudgementEvent[] events =
        [
            createEvent(new BmsJudgementSource(200, 0, BmsJudgementSourceKind.Note), HitResult.Great),
            createEvent(new BmsJudgementSource(100, 0, BmsJudgementSourceKind.Note), HitResult.Perfect),
            createEvent(new BmsJudgementSource(150, 0, BmsJudgementSourceKind.EmptyPoor), HitResult.Miss),
            createEvent(new BmsJudgementSource(100, 0, BmsJudgementSourceKind.LongNote, 200), HitResult.Good),
            createEvent(new BmsJudgementSource(400, 0, BmsJudgementSourceKind.Note), HitResult.Ok, 350),
        ];

        var progression = BmsScoreGraph.CreateJudgementProgression(events);
        var cursor = new BmsScoreGraph.JudgementProgressCursor();
        cursor.SetProgression(progression);

        Assert.Multiple(() =>
        {
            Assert.That(cursor.GetCountAtTime(99, HitResult.Perfect), Is.Zero);
            Assert.That(cursor.GetCountAtTime(150, HitResult.Perfect), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(150, HitResult.Miss), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(299, HitResult.Good), Is.Zero);
            Assert.That(cursor.GetCountAtTime(300, HitResult.Good), Is.EqualTo(1));
            Assert.That(cursor.GetCountAtTime(350, HitResult.Ok), Is.EqualTo(1));
        });
    }
}
