using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result.Statistic;

[TestFixture]
public class BmsGaugeHistoryGraphTest
{
    private static BmsBeatmap createBeatmap(double duration = 2000) => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K,
        TotalColumns = 8,
        Total = 200,
        HitObjects =
        {
            new BmsNote { StartTime = duration / 2 },
            new BmsNote { StartTime = duration },
        },
    };

    [Test]
    public void TestAutoGaugeCreatesParallelGaugeSeries()
    {
        var history = new[]
        {
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hazard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.ExHard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.6, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.8, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.9, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.AssistEasy, 1, false),
            ]),
        };
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history);

        Assert.That(series.Select(s => s.Name.ToString()), Is.EqualTo([
            "Hazard", "ExHard", "Hard", "Normal", "Easy", "Assist Easy",
        ]));
        Assert.That(series.Single(s => s.Name.ToString() == "Hazard").Points.Last().Health, Is.Zero);
        var finalGauge = series.Single(s => s.Name.ToString() == "Hard");
        Assert.That(finalGauge.IsFinalUsedGauge, Is.True);
        Assert.That(finalGauge.LineRadius, Is.GreaterThan(series.Where(s => !s.IsFinalUsedGauge).Max(s => s.LineRadius)));
    }

    [Test]
    public void TestEmptyHistoryHasNoSeries()
    {
        Assert.That(BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), []), Is.Empty);
    }

    [Test]
    public void TestAutoGaugeDisplaysRecordedFailureAndSelectedLayer()
    {
        GameplayHealthHistoryEvent[] history =
        [
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.2, false),
            ]),
            BmsTestHealth.History(3000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.82, false),
            ]),
        ];

        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history).ToArray();

        Assert.That(series.Single(s => s.Name.ToString() == "Hard").FailurePoint, Is.Not.Null);
        Assert.That(series.Single(s => s.Name.ToString() == "Hard").Points.Last().Health, Is.Zero);
        Assert.That(series.Single(s => s.Name.ToString() == "Normal").IsFinalUsedGauge, Is.True);
    }

    [Test]
    public void TestCourseAutoGaugeCreatesOnlyCourseGaugeSeries()
    {
        var history = new[]
        {
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.ExHardClass, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.8, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.9, false),
            ]),
        };
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history);

        Assert.That(series.Select(s => s.Name.ToString()), Is.EqualTo(["ExHard Class", "ExClass", "Class"]));
        Assert.That(series.Single(s => s.Name.ToString() == "ExClass").IsFinalUsedGauge, Is.True);
    }

    [Test]
    public void TestCourseGaugeDoesNotContinueAfterPreviousStageFailure()
    {
        var firstScore = new ScoreInfo { Mods = [new BmsModAutoGauge()] };
        GameplayHealthHistoryEvent[] firstHistory =
        [
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.5, false),
            ]),
        ];
        var secondScore = new ScoreInfo { Mods = [new BmsModAutoGauge()] };
        GameplayHealthHistoryEvent[] secondHistory =
        [
            BmsTestHealth.History(0,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.5, false),
            ]),
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.6, false),
            ]),
        ];

        var series = BmsResultStatisticsData.CreateCourse(
        [
            BmsResultStatisticsData.Create(firstScore, createBeatmap(), gauges: firstHistory),
            BmsResultStatisticsData.Create(secondScore, createBeatmap(), gauges: secondHistory),
        ]).Gauges;

        Assert.Multiple(() =>
        {
            Assert.That(series.Single(gauge => gauge.Name.ToString() == "Hard").Segments, Has.Count.EqualTo(1));
            var normal = series.Single(gauge => gauge.Name.ToString() == "Normal");
            Assert.That(normal.Segments[1][0].Health, Is.EqualTo(normal.Segments[0][^1].Health));
        });
    }

    [Test]
    public void TestCourseGaugeStartsEachStageAtPreviousEndingHealth()
    {
        var firstHistory = new[]
        {
            BmsTestHealth.History(0, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 1, false)]),
            BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.6, false)]),
        };
        var secondHistory = new[]
        {
            BmsTestHealth.History(0, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.6, false)]),
            BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.8, false)]),
        };
        var series = BmsGaugeHistoryGraph.CreateCourseSeries(
        [
            BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), firstHistory),
            BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), secondHistory),
        ], [2000, 2000]).Single();

        Assert.That(series.Segments, Has.Count.EqualTo(2));
        Assert.That(series.Segments[1][0].Health, Is.EqualTo(series.Segments[0][^1].Health));
    }

    [Test]
    public void TestCourseGaugeUsesStageDurationProportions()
    {
        var firstScore = new ScoreInfo
        {
            Mods = [new BmsModHardGauge()],
            HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null)],
        };
        var secondScore = new ScoreInfo
        {
            Mods = [new BmsModHardGauge()],
            HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 3000 }, null, null)],
        };
        var data = BmsResultStatisticsData.CreateCourse(
        [
            BmsResultStatisticsData.Create(firstScore, createBeatmap(1000),
                [BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 1, false)])]),
            BmsResultStatisticsData.Create(secondScore, createBeatmap(3000),
                [BmsTestHealth.History(3000, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 1, false)])]),
        ]);
        var series = data.Gauges.Single();
        var boundaries = data.StageBoundaries;

        Assert.Multiple(() =>
        {
            Assert.That(series.Segments[0][^1].Time, Is.EqualTo(0.25f).Within(0.001));
            Assert.That(series.Segments[1][0].Time, Is.EqualTo(0.25f).Within(0.001));
            Assert.That(boundaries, Is.EqualTo([0.25f]).Within(0.001));
        });
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
    public void TestGaugeFailedByEventAtStageStartIsDisplayed()
    {
        GameplayHealthHistoryEvent[] history =
        [
            BmsTestHealth.History(0,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
            ]),
        ];

        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history);

        Assert.That(series.Select(gauge => gauge.Name.ToString()), Is.EqualTo(["Hard"]));
    }

    [Test]
    public void TestSingleGaugeHistoryCreatesSingleSeries()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(),
            [BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 1, false)])]);

        Assert.That(series, Has.Count.EqualTo(1));
        Assert.That(series.Single().Name.ToString(), Is.EqualTo("Hard"));
        Assert.That(series.Single().IsFinalUsedGauge, Is.True);
        Assert.That(series.Single().Points.Last().Time, Is.EqualTo(1));
    }

    [Test]
    public void TestGrooveGaugeFailurePointIsRecordedAtEndWhenBelowClearThreshold()
    {
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(),
            [BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.4, false)])]).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(1));
        Assert.That(series.FailurePoint.Value.Health, Is.EqualTo(0.4f).Within(0.001));
    }

    [Test]
    public void TestGrooveGaugeCanRecoverFromMinimumHealth()
    {
        var history = new[]
        {
            BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.02, false)]),
            BmsTestHealth.History(2000, [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.8, false)]),
        };
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history).Single();

        Assert.That(series.FailurePoint, Is.Null);
        Assert.That(series.Points.Min(p => p.Health), Is.EqualTo(0.02f).Within(0.001));
        Assert.That(series.Points.Last().Health, Is.GreaterThanOrEqualTo(0.8f));
    }

    [Test]
    public void TestGaugeFailureUsesPlayableBeatmapDuration()
    {
        GameplayHealthHistoryEvent[] history =
        [
            BmsTestHealth.History(1500,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0, true),
            ]),
        ];
        var beatmap = new BmsBeatmap
        {
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Duration = 500 },
                new BmsNote { StartTime = 3000 },
            },
        };

        var series = BmsGaugeHistoryGraph.CreateSeries(beatmap, history).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.5f));
        Assert.That(series.Points.Last().Time, Is.EqualTo(1));
    }

    [Test]
    public void TestHistoryPreservesCanonicalSnapshotOrder()
    {
        GameplayHealthHistoryEvent[] history =
        [
            BmsTestHealth.History(2000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.7, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.8, false),
            ]),
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.6, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.9, false),
            ]),
        ];

        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history);

        var easySeries = series.Single(s => s.Name.ToString() == "Easy");

        Assert.That(easySeries.IsFinalUsedGauge, Is.True);
        Assert.That(easySeries.Points[^1].Health, Is.EqualTo(0.9f));
        Assert.That(easySeries.Points.Select(p => p.Time), Is.Ordered.Ascending);
    }

    [Test]
    public void TestHistoryUsesSelectedLayer()
    {
        GameplayHealthHistoryEvent[] history =
        [
            BmsTestHealth.History(1000,
            [
                new BmsGaugeStateSnapshot(BmsGaugeType.Normal, 0.6, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Easy, 0.7, false),
            ]),
        ];

        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(), history);

        Assert.That(series.Single(s => s.Name.ToString() == "Easy").IsFinalUsedGauge, Is.True);
        Assert.That(series.Single(s => s.Name.ToString() == "Normal").IsFinalUsedGauge, Is.False);
    }

    [Test]
    public void TestSurvivalGaugeFailurePointIsRecordedAtZeroHealthEvent()
    {
        var history = new[]
        {
            BmsTestHealth.History(1000, [new BmsGaugeStateSnapshot(BmsGaugeType.ExHard, 0.5, false)]),
            BmsTestHealth.History(7000, [new BmsGaugeStateSnapshot(BmsGaugeType.ExHard, 0, true)]),
            BmsTestHealth.History(8000, [new BmsGaugeStateSnapshot(BmsGaugeType.ExHard, 0, true)]),
        };
        var series = BmsGaugeHistoryGraph.CreateSeries(createBeatmap(8000), history).Single();

        Assert.That(series.FailurePoint, Is.Not.Null);
        Assert.That(series.FailurePoint!.Value.Time, Is.EqualTo(0.875f).Within(0.001));
        Assert.That(series.FailurePoint.Value.Health, Is.Zero);
    }
}
