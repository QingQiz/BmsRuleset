using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeAttemptResultTest
{
    [Test]
    public void TestLateIntervalPreservesTailGaugeClampingAndCombinedCoordinates()
    {
        const double origin = 600000.125;
        const double duration = 2750.75;
        var note = new BmsNote { StartTime = origin + 100, Column = 1 };
        var hold = new BmsLongNote { StartTime = origin + 750.25, Duration = 2000.5, Column = 2 };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = [note, hold],
        };
        BmsJudgementEvent[] judgements =
        [
            new(BmsJudgementSource.From(note), HitResult.Perfect,
                [new BmsTimingObservation(BmsTimingObservationKind.Note, note.StartTime, note.StartTime - 5, 1, HitResult.Perfect)]),
            new(BmsJudgementSource.From(hold), HitResult.Great,
            [
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteHead, hold.StartTime, hold.StartTime - 10, 1, HitResult.Perfect),
                new BmsTimingObservation(BmsTimingObservationKind.LongNoteTail, origin + duration, origin + duration + 19, 1, HitResult.Great),
            ]),
        ];
        GameplayHealthHistoryEvent[] gauges =
        [
            BmsTestHealth.History(origin - 50, [new(BmsGaugeType.Normal, 0.8, false)]),
            BmsTestHealth.History(origin + 500, [new(BmsGaugeType.Normal, 0.6, false)]),
            BmsTestHealth.History(origin + 4000, [new(BmsGaugeType.Normal, 0.4, false)]),
        ];
        var score = new ScoreInfo { HitEvents = BmsJudgementEventProjection.CreateTimingHitEvents(judgements) };
        var result = BmsPracticeAttemptResult.Create(1, score, gauges, beatmap,
            new BmsPracticeSettings { StartTime = origin, EndTime = origin + 1000 }, judgements);
        var statistics = result.Statistics;
        var combined = BmsResultStatisticsData.Combine([statistics, statistics]);

        Assert.Multiple(() =>
        {
            Assert.That(statistics.Timeline.Duration, Is.EqualTo(duration));
            Assert.That(statistics.GaugeDuration, Is.EqualTo(duration));
            Assert.That(statistics.HitEvents.Select(hit => hit.HitObject.StartTime), Is.EqualTo(new[] { 100, 750.25, duration }));
            Assert.That(statistics.HitEvents.Select(hit => hit.TimeOffset), Is.EqualTo(new[] { -5, -10, 19 }));
            Assert.That(statistics.Scatter.Overall.Points.Select(point => point.Time), Is.EqualTo(new[] { 100, 750.25, duration }));
            Assert.That(statistics.Gauges.Single().Points.First().Time, Is.Zero);
            Assert.That(statistics.Gauges.Single().Points.Last().Time, Is.EqualTo(1));
            Assert.That(statistics.Gauges.Single().Points.Last().Health, Is.EqualTo(0.4f));
            Assert.That(result.Health, Is.EqualTo(0.4));
            Assert.That(result.Fast, Is.EqualTo(2));
            Assert.That(result.Slow, Is.EqualTo(1));
            Assert.That(combined.GaugeDuration, Is.EqualTo(2 * duration));
            Assert.That(combined.StageBoundaries, Is.EqualTo(new[] { 0.5f }));
            Assert.That(combined.Scatter.Overall.Points.Skip(3).Select(point => point.Time),
                Is.EqualTo(new[] { duration + 100, duration + 750.25, 2 * duration }));
            Assert.That(note.StartTime, Is.EqualTo(origin + 100));
            Assert.That(hold.StartTime, Is.EqualTo(origin + 750.25));
            Assert.That(judgements[1].TimingObservations[1].ActualTime, Is.EqualTo(origin + duration + 19));
            Assert.That(gauges.Select(entry => entry.Time), Is.EqualTo(new[] { origin - 50, origin + 500, origin + 4000 }));
            Assert.That(score.HitEvents[0].HitObject.StartTime, Is.EqualTo(note.StartTime));
            Assert.That(result.Score, Is.Not.SameAs(score));
        });
    }
}
