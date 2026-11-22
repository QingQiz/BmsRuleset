using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

[TestFixture]
public class BmsJudgementEventProjectionTest
{
    [TestCase(0)]
    [TestCase(600000.123456789)]
    [TestCase(-1e18)]
    public void TestRelativeProjectionMatchesTranslatedJudgements(double timeOrigin)
    {
        BmsJudgementEvent[] events =
        [
            new(new BmsJudgementSource(1000.1, 2, BmsJudgementSourceKind.LongNote, 500.2), HitResult.Great,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000.1, 988.3, 1.5, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500.3, 1519.4, 1.5, HitResult.Great),
            ]),
            new(new BmsJudgementSource(900.2, 1, BmsJudgementSourceKind.Note), HitResult.Good,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 900.2, 988.3, null, HitResult.Good)]),
            new(new BmsJudgementSource(1200.5, 3, BmsJudgementSourceKind.LongNote, 1000), HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1200.5, 1390.7, 1, HitResult.Meh)]),
            new(new BmsJudgementSource(600.6, 2, BmsJudgementSourceKind.EmptyInput), HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000.1, 600.6, 1, HitResult.Miss)]),
            new(new BmsJudgementSource(1000.1, 2, BmsJudgementSourceKind.NonConsumingHit), HitResult.Ok,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000.1, 800.9, 1, HitResult.Ok)]),
            new(new BmsJudgementSource(1700.8, 4, BmsJudgementSourceKind.Landmine, LandmineDamagePercent: 12.5), HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1700.8, 1700.8, 1, HitResult.Miss)]),
            new(new BmsJudgementSource(1000.1, 1, BmsJudgementSourceKind.Note), HitResult.Miss,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000.1, 1300.1, 1, HitResult.Miss)], true),
        ];
        var sources = events.Select(entry => entry.Source).ToArray();
        var observations = events.Select(entry => entry.TimingObservations.ToArray()).ToArray();
        var translated = events.Select(entry => new BmsJudgementEvent(
            entry.Source with { StartTime = entry.Source.StartTime - timeOrigin }, entry.Result,
            entry.TimingObservations.Select(observation => observation with
            {
                ExpectedTime = observation.ExpectedTime - timeOrigin,
                ActualTime = observation.ActualTime - timeOrigin,
            }), entry.SuppressPenalty)).ToArray();

        var timing = BmsJudgementEventProjection.CreateTimingHitEvents(events, timeOrigin);
        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(events, timeOrigin);
        assertEvents(timing, BmsJudgementEventProjection.CreateTimingHitEvents(translated));
        assertEvents(scoring, BmsJudgementEventProjection.CreateScoringHitEvents(translated));
        Assert.Multiple(() =>
        {
            Assert.That(timing, Has.Count.EqualTo(7));
            Assert.That(scoring, Has.Count.EqualTo(6));
            Assert.That(events.Select(entry => entry.Source), Is.EqualTo(sources));
            Assert.That(events.Select(entry => entry.TimingObservations), Is.EqualTo(observations));
        });
    }

    [Test]
    public void TestRelativeProjectionAcceptsEmptyInput()
    {
        Assert.That(BmsJudgementEventProjection.CreateTimingHitEvents([], 600000.25), Is.Empty);
        Assert.That(BmsJudgementEventProjection.CreateScoringHitEvents([], 600000.25), Is.Empty);
    }

    private static void assertEvents(IReadOnlyList<HitEvent> actual, IReadOnlyList<HitEvent> expected)
    {
        Assert.That(actual.Count, Is.EqualTo(expected.Count));
        for (var i = 0; i < actual.Count; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual[i].HitObject.GetType(), Is.EqualTo(expected[i].HitObject.GetType()));
                Assert.That(BmsJudgementSource.From(actual[i].HitObject), Is.EqualTo(BmsJudgementSource.From(expected[i].HitObject)));
                Assert.That(actual[i].HitObject.GetEndTime(), Is.EqualTo(expected[i].HitObject.GetEndTime()));
                Assert.That(actual[i].TimeOffset, Is.EqualTo(expected[i].TimeOffset));
                Assert.That(actual[i].GameplayRate, Is.EqualTo(expected[i].GameplayRate));
                Assert.That(actual[i].Result, Is.EqualTo(expected[i].Result));
                Assert.That(actual[i].LastHitObject, Is.SameAs(i == 0 ? null : actual[i - 1].HitObject));
            });
        }
    }

    [Test]
    public void TestEmptyPoorTimingProjectionKeepsPressTimeOffsetAndLane()
    {
        var judgementEvent = new BmsJudgementEvent(
            new BmsJudgementSource(600, 2, BmsJudgementSourceKind.EmptyInput),
            HitResult.Miss,
            [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 600, 1, HitResult.Miss)]);

        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents([judgementEvent]).Single();
        var timing = BmsJudgementEventProjection.CreateTimingHitEvents([judgementEvent]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(scoring.HitObject.StartTime, Is.EqualTo(600));
            Assert.That(scoring.TimeOffset, Is.EqualTo(-400));
            Assert.That(BmsJudgementSource.From(timing.HitObject), Is.EqualTo(judgementEvent.Source));
            Assert.That(timing.TimeOffset, Is.EqualTo(-400));
        });
    }

    [Test]
    public void TestExProgressionUsesCanonicalLongNoteResultOnce()
    {
        BmsJudgementEvent[] events =
        [
            new(BmsJudgementSource.From(new BmsLongNote()), HitResult.Great,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, 1000, 988, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, 1500, 1519, 1, HitResult.Great),
            ]),
        ];

        Assert.That(BmsTestExScore.CreateProgression(events), Is.EqualTo([0, 1]));
    }

    [Test]
    public void TestHeadPoorScoringEventUsesHeadTime()
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 2 };
        BmsJudgementEvent[] events =
        [
            new(BmsJudgementSource.From(longNote), HitResult.Meh,
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
            new(
                BmsJudgementSource.From(new BmsNote { StartTime = 2000 }),
                HitResult.Perfect,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 2000, 2100, 1, HitResult.Perfect)]),
            new(
                BmsJudgementSource.From(new BmsNote { StartTime = 1000 }),
                HitResult.Meh,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, 1000, 2200, 1, HitResult.Meh)]),
        ];

        var scoring = BmsJudgementEventProjection.CreateScoringHitEvents(events);

        Assert.That(scoring.Select(e => e.Result), Is.EqualTo([HitResult.Perfect, HitResult.Meh]));
    }

    [Test]
    public void TestStandardLongNoteHasOneScoringEventAndTwoTimingEvents()
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 2 };
        BmsJudgementEvent[] events =
        [
            new(BmsJudgementSource.From(longNote), HitResult.Great,
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
            Assert.That(timing.Select(e => e.TimeOffset), Is.EqualTo([-12, 19]));
            Assert.That(timing.Select(e => e.HitObject.StartTime), Is.EqualTo([1000, 1500]));
        });
    }
}
