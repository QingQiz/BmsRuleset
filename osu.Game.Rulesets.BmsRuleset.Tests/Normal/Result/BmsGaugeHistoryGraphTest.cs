using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsGaugeHistoryGraphTest
{
    [Test]
    public void TestAutoGaugeCreatesParallelGaugeSeries()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(
            new ScoreInfo
            {
                Mods = [new BmsModAutoGauge(), new BmsModHardGauge()],
                HitEvents = [new HitEvent(0, 1, HitResult.Ok, new BmsNote { StartTime = 1000 }, null, null)],
            },
            createBeatmap());

        Assert.That(series.Select(s => s.Name), Is.EqualTo([
            "Hazard",
            "ExHard",
            "Hard",
            "Normal",
            "Easy",
            "Assist Easy",
        ]));

        Assert.That(series.Single(s => s.Name == "Hazard").Points.Last().Health, Is.Zero);
        Assert.That(series.Single(s => s.Name == "ExHard").Points.Last().Health, Is.GreaterThan(0));

        var finalGauge = series.Single(s => s.Name == "Hard");

        Assert.That(finalGauge.IsFinalUsedGauge, Is.True);
        Assert.That(finalGauge.LineRadius, Is.GreaterThan(series.Where(s => !s.IsFinalUsedGauge).Max(s => s.LineRadius)));
    }

    [Test]
    public void TestAutoGaugeUsesPersistedGaugeHistoryWhenHitEventsUnderReportDamage()
    {
        var score = new ScoreInfo
        {
            Mods = [new BmsModAutoGauge()],
            HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 3000 }, null, null)],
        };

        BmsScoreGaugeHistoryStore.Set(score,
        [
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.Hard,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.2, false),
            ]),
            new BmsGaugeHistoryEvent(3000, BmsGaugeType.Normal,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.82, false),
            ]),
        ]);

        var series = BmsGaugeHistoryGraph.CreateSeries(score, createBeatmap()).ToArray();

        Assert.That(series.Single(s => s.Name == "Hard").FailurePoint, Is.Not.Null);
        Assert.That(series.Single(s => s.Name == "Hard").Points.Last().Health, Is.Zero);
        Assert.That(series.Single(s => s.Name == "Normal").IsFinalUsedGauge, Is.True);
    }

    [Test]
    public void TestPersistedHistoryUsesResolvedAutoGaugeType()
    {
        var score = new ScoreInfo
        {
            Mods = [new BmsModAutoGauge(), new BmsModEasyGauge()],
        };
        BmsScoreGaugeHistoryStore.Set(score,
        [
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.Normal,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.6, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.7, false),
            ]),
        ]);

        var series = BmsGaugeHistoryGraph.CreateSeries(score, createBeatmap());

        Assert.That(series.Single(s => s.Name == "Easy").IsFinalUsedGauge, Is.True);
        Assert.That(series.Single(s => s.Name == "Normal").IsFinalUsedGauge, Is.False);
    }

    [Test]
    public void TestInvertGaugeFailureUsesPlayableBeatmapDuration()
    {
        var score = new ScoreInfo { Mods = [new BmsModInvert(), new BmsModHardGauge()] };
        BmsScoreGaugeHistoryStore.Set(score,
        [
            new BmsGaugeHistoryEvent(1500, BmsGaugeType.Hard,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
            ]),
        ]);
        var beatmap = new BmsBeatmap
        {
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Duration = 500 },
                new BmsNote { StartTime = 3000 },
            },
        };

        var series = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.5f));
        Assert.That(series.Points.Last().Time, Is.EqualTo(1));
    }

    [Test]
    public void TestFallbackGaugeFailureUsesPlayableBeatmapDuration()
    {
        var score = new ScoreInfo
        {
            Mods = [new BmsModInvert(), new BmsModHazardGauge()],
            HitEvents =
            [
                new HitEvent(0, 1, HitResult.Ok, new BmsLongNote { StartTime = 1000, Duration = 500 }, null, null),
            ],
        };
        var beatmap = new BmsBeatmap
        {
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Duration = 500 },
                new BmsNote { StartTime = 3000 },
            },
        };

        var series = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.5f));
        Assert.That(series.Points.Last().Time, Is.EqualTo(1));
    }

    [Test]
    public void TestPersistedHistoryPreservesCanonicalSnapshotOrder()
    {
        var score = new ScoreInfo { Mods = [new BmsModAutoGauge()] };
        BmsScoreGaugeHistoryStore.Set(score,
        [
            new BmsGaugeHistoryEvent(2000, BmsGaugeType.Normal,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.7, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.8, false),
            ]),
            new BmsGaugeHistoryEvent(1000, BmsGaugeType.Easy,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.6, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.9, false),
            ]),
        ]);

        var series = BmsGaugeHistoryGraph.CreateSeries(score, createBeatmap());

        var easySeries = series.Single(s => s.Name == "Easy");

        Assert.That(easySeries.IsFinalUsedGauge, Is.True);
        Assert.That(easySeries.Points[^1].Health, Is.EqualTo(0.9f));
        Assert.That(easySeries.Points.Select(p => p.Time), Is.Ordered.Ascending);
    }

    [Test]
    public void TestCanonicalGaugeEventsAreNotReorderedByExpectedTime()
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
        var score = new ScoreInfo { Mods = [new BmsModHardGauge()] };
        score.HitEvents = BmsJudgementEventProjection.CreateTimingHitEvents(events);
        BmsJudgementEventStore.Set(score, events);

        var series = BmsGaugeHistoryGraph.CreateSeries(score, createBeatmap()).Single();

        Assert.That(series.Points[1].Health, Is.GreaterThan(series.Points[2].Health));
        Assert.That(series.Points.Select(p => p.Time), Is.Ordered.Ascending);
    }

    [Test]
    public void TestGaugeModCreatesSingleGaugeSeries()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(
            new ScoreInfo
            {
                Mods = [new BmsModHardGauge()],
                HitEvents = [new HitEvent(0, 1, HitResult.Great, new BmsNote { StartTime = 1000 }, null, null)],
            },
            createBeatmap());

        Assert.That(series, Has.Count.EqualTo(1));
        Assert.That(series.Single().Name, Is.EqualTo("Hard"));
        Assert.That(series.Single().IsFinalUsedGauge, Is.True);
        Assert.That(series.Single().Points.Last().Time, Is.EqualTo(1));
    }

    [Test]
    public void TestEmptyPoorFailureLocksExHardSeriesAtZero()
    {
        var processor = new BmsScoreProcessor();

        for (var i = 0; i < 13; i++)
            processor.RegisterEmptyPoor(1000 + i * 100);

        var score = new ScoreInfo { Mods = [new BmsModExHardGauge()] };
        processor.PopulateScore(score);
        score.HitEvents.Add(new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 3000 }, null, null));

        var series = BmsGaugeHistoryGraph.CreateSeries(score, createBeatmap()).Single();

        Assert.That(series.Name, Is.EqualTo("ExHard"));
        Assert.That(series.Points.Last().Health, Is.Zero);
    }

    [Test]
    public void TestSurvivalGaugeFailurePointIsRecordedAtZeroHealthEvent()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(
            new ScoreInfo
            {
                Mods = [new BmsModExHardGauge()],
                HitEvents =
                [
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 1000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 3000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 4000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 5000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 6000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 7000 }, null, null),
                    new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 8000 }, null, null),
                ],
            },
            createBeatmap()).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.875f).Within(0.001));
        Assert.That(series.FailurePoint.Value.Health, Is.Zero);
    }

    [Test]
    public void TestGrooveGaugeFailurePointIsRecordedAtZeroHealthEvent()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(
            new ScoreInfo
            {
                HitEvents =
                [
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 1000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 2000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 3000 }, null, null),
                    new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 4000 }, null, null),
                    new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 5000 }, null, null),
                ],
            },
            createBeatmap()).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.8f).Within(0.001));
        Assert.That(series.FailurePoint.Value.Health, Is.Zero);
    }

    [Test]
    public void TestGrooveGaugeFailurePointIsRecordedAtEndWhenBelowClearThreshold()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(
            new ScoreInfo
            {
                HitEvents = [new HitEvent(0, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null)],
            },
            createBeatmap()).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(1));
        Assert.That(series.FailurePoint.Value.Health, Is.LessThan(0.8f));
        Assert.That(series.FailurePoint.Value.Health, Is.GreaterThan(0));
    }

    [Test]
    public void TestEndFailureMarkerStaysOnRightAxis()
    {
        var graphSize = new Vector2(300, 180);
        var points = new[]
        {
            new BmsGaugeHistoryGraph.GaugePoint(0, 0.2f),
            new BmsGaugeHistoryGraph.GaugePoint(1, 0.4f),
        };

        var position = BmsGaugeHistoryGraph.CalculateFailureMarkerPosition(points, points[^1], 2, graphSize);

        Assert.That(position.X, Is.LessThanOrEqualTo(graphSize.X));
    }

    [Test]
    public void TestEndFailureMarkerCentreStaysOnFailurePoint()
    {
        var graphSize = new Vector2(300, 180);
        var points = new[]
        {
            new BmsGaugeHistoryGraph.GaugePoint(0, 0.2f),
            new BmsGaugeHistoryGraph.GaugePoint(1, 0.4f),
        };

        var position = BmsGaugeHistoryGraph.CalculateFailureMarkerPosition(points, points[^1], 2, graphSize);

        Assert.That(position.X, Is.EqualTo(graphSize.X).Within(0.001));
        Assert.That(position.Y, Is.EqualTo(graphSize.Y * 0.6f).Within(0.001));
    }

    private static BmsBeatmap createBeatmap() => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K,
        TotalColumns = 8,
        Total = 200,
        HitObjects =
        {
            new BmsNote { StartTime = 1000 },
            new BmsNote { StartTime = 2000 },
        },
    };
}
