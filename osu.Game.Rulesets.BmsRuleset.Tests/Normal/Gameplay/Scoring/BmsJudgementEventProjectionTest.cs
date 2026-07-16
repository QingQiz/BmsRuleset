using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

[TestFixture]
public class BmsJudgementEventProjectionTest
{
    [Test]
    public void TestStandardLongNoteHasOneScoringEventAndTwoTimingEvents()
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 2 };
        BmsJudgementEvent[] events =
        [
            new BmsJudgementEvent(BmsJudgementSource.From(longNote), HitResult.Great,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 988, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1519, 1, HitResult.Great),
            ]),
        ];

        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(events);
        var timing = BmsJudgementEventProjection.CreateTimingHitEvents(events);

        Assert.Multiple(() =>
        {
            Assert.That(scoring, Has.Count.EqualTo(1));
            Assert.That(scoring.Single().Result, Is.EqualTo(HitResult.Great));
            Assert.That(scoring.Single().HitObject, Is.TypeOf<BmsLongNote>());
            Assert.That(scoring.Single().HitObject.StartTime, Is.EqualTo(1000));
            Assert.That(((BmsLongNote)scoring.Single().HitObject).Duration, Is.EqualTo(500));
            Assert.That(scoring.Single().TimeOffset, Is.EqualTo(19));
            Assert.That(timing, Has.Count.EqualTo(2));
            Assert.That(timing.Select(e => e.TimeOffset), Is.EqualTo(new[] { -12, 19 }));
            Assert.That(timing.Select(e => e.HitObject.StartTime), Is.EqualTo(new[] { 1000, 1500 }));
        });
    }

    [Test]
    public void TestExProgressionUsesCanonicalLongNoteResultOnce()
    {
        BmsJudgementEvent[] events =
        [
            new BmsJudgementEvent(BmsJudgementSource.From(new BmsLongNote()), HitResult.Great,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 988, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1519, 1, HitResult.Great),
            ]),
        ];

        Assert.That(BmsExScore.CreateProgression(events), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void TestHeadPoorScoringEventUsesHeadTime()
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 2 };
        BmsJudgementEvent[] events =
        [
            new BmsJudgementEvent(BmsJudgementSource.From(longNote), HitResult.Meh,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 1200, 1, HitResult.Meh),
            ]),
        ];

        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(events).Single();

        Assert.Multiple(() =>
        {
            Assert.That(scoring.HitObject, Is.TypeOf<BmsNote>());
            Assert.That(scoring.HitObject.StartTime, Is.EqualTo(1000));
            Assert.That(scoring.TimeOffset, Is.EqualTo(200));
        });
    }

    [Test]
    public void TestScoringProjectionPreservesCanonicalOrder()
    {
        BmsJudgementEvent[] events =
        [
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsNote { StartTime = 2000 }),
                HitResult.Perfect,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 2000, 2100, 1, HitResult.Perfect)]),
            new BmsJudgementEvent(
                BmsJudgementSource.From(new BmsNote { StartTime = 1000 }),
                HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 2200, 1, HitResult.Meh)]),
        ];

        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(events);

        Assert.That(scoring.Select(e => e.Result), Is.EqualTo([HitResult.Perfect, HitResult.Meh]));
    }
}
