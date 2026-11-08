using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeGridTest
{
    [Test]
    public void StopAtMusicalMidpointKeepsEarlierPositionUntilScrollingResumes()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], [new(24, 1000, 96, 120, 0)]);
        var grid = BmsPracticeGrid.Create(map, 4);

        Assert.That(grid.Snap(250), Is.Zero);
        Assert.That(grid.Snap(750), Is.Zero);
        Assert.That(grid.Snap(1250), Is.Zero);
        Assert.That(grid.Snap(1250.00001), Is.EqualTo(map.ProjectTickToTime(48)));
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(8)]
    [TestCase(12)]
    [TestCase(16)]
    [TestCase(24)]
    [TestCase(32)]
    [TestCase(48)]
    [TestCase(64)]
    public void PrecomputedSnappingMatchesInverseProjection(int divisor)
    {
        BmsTimingMap[] maps =
        [
            new(192, [new(0, 0, 20, 20d / 192), new(1, 20, 120, 0.625), new(2, 140, 192, 1)],
                [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]),
            new(10, [new(0, 0, 10, 1)], [new(0, 120, 0)], [new(1, 500, 48, 120, 0)]),
            new(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], [new(24, 1000, 96, 120, 0)]),
            new(192, [new(0, 0, 192, 1)], [new(0, 60, 0), new(20, 480, 416.666666666667)], []),
        ];
        foreach (var map in maps)
            assertProjectionMatches(map, divisor);
    }

    [TestCase("Aleph-0 (by LeaF)", "_7NORMAL.bms")]
    [TestCase("Aleph-0 (by LeaF)", "_14ANOTHER.bms")]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml")]
    [TestCase("103_outlaw_ogg", "99_outlaw_prison_breaker.bms")]
    public void RealChartSnappingMatchesInverseProjection(string directory, string filename)
    {
        var map = BmsChartParser.Parse(BmsChartParser.ReadAllLines(File.ReadAllBytes(
            Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, directory, filename))), randomValueSelector: _ => 1).TimingMap;
        foreach (var divisor in new[] { 0, 4, 8, 12, 16, 24, 32, 48, 64 })
            assertProjectionMatches(map, divisor);
    }

    private static void assertProjectionMatches(BmsTimingMap map, int divisor)
    {
        var grid = BmsPracticeGrid.Create(map, divisor);
        var end = grid.Lines[^1].Time;
        var random = new Random(1024);
        var measures = map.Measures.ToDictionary(measure => measure.Index);
        var stopDurations = map.StopEvents.GroupBy(stop => stop.Tick).ToDictionary(group => group.Key, group => group.Sum(stop => stop.Duration));
        var stride = Math.Max(1, grid.Lines.Length / 2048);
        var midpoints = grid.Lines.Zip(grid.Lines.Skip(1), (lower, upper) => (tickAt(lower) + tickAt(upper)) / 2)
            .Where((_, index) => index % stride == 0);
        var positions = grid.Lines.Where((_, index) => index % stride == 0).Select(line => line.Time)
            .Concat(midpoints.SelectMany(tick =>
            {
                var time = BmsPracticeGridReference.ProjectFractionalTick(map, tick);
                var stop = tick == Math.Floor(tick) ? stopDurations.GetValueOrDefault((long)tick) : 0;
                return new[] { time, time + stop };
            }))
            .Concat(Enumerable.Range(0, 2048).Select(_ => random.NextDouble() * (end + 200) - 100))
            .Concat(map.StopEvents.SelectMany(stop => new[]
            {
                map.ProjectTickToTime(stop.Tick), map.ProjectTickToTime(stop.Tick) + stop.Duration / 2,
                map.ProjectTickToTime(stop.Tick) + stop.Duration,
            }));
        foreach (var time in positions)
        {
            foreach (var offset in new[] { -1e-6, 0, 1e-6 })
            {
                var candidate = time + offset;
                var expected = BmsPracticeGridReference.Snap(map, candidate, divisor);
                var actual = grid.Snap(candidate);
                Assert.That(actual, Is.EqualTo(expected).Within(1e-7), $"divisor={divisor} time={candidate:R}");
            }
        }

        double tickAt(BmsPracticeGridLine line) => measures.TryGetValue(line.Measure, out var measure)
            ? measure.StartTick + (divisor == 0 ? 0 : (double)line.Subdivision * map.TickResolution / divisor)
            : map.Measures[^1].StartTick + map.Measures[^1].LengthTicks;
    }

    [TestCase(4)]
    [TestCase(8)]
    [TestCase(12)]
    [TestCase(16)]
    [TestCase(24)]
    [TestCase(32)]
    [TestCase(48)]
    [TestCase(64)]
    public void DivisionsPreserveTheirNoteValue(int beatDivisor)
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], []);
        var grid = BmsPracticeGrid.Create(map, beatDivisor);
        var lines = grid.Lines;

        Assert.That(lines, Has.Length.EqualTo(beatDivisor + 1));
        Assert.That(lines.Select(line => line.Time),
            Is.EqualTo(Enumerable.Range(0, beatDivisor + 1).Select(index => index * 2000d / beatDivisor)).Within(1e-9));
        foreach (var line in lines)
            Assert.That(grid.Snap(line.Time), Is.EqualTo(line.Time).Within(1e-9));
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(12)]
    [TestCase(24)]
    [TestCase(64)]
    public void IrregularMeasuresRestartDivisionsAtTheirActualBoundaries(int beatDivisor)
    {
        var map = new BmsTimingMap(192,
        [
            new(0, 0, 20, 20d / 192),
            new(1, 20, 120, 0.625),
            new(2, 140, 144, 0.75),
        ], [new(0, 120, 0)], []);
        var grid = BmsPracticeGrid.Create(map, beatDivisor);
        var lines = grid.Lines;

        Assert.That(lines.Where(line => line.IsMeasureLine).Select(line => line.Time),
            Is.EqualTo(new long[] { 0, 20, 140, 284 }.Select(map.ProjectTickToTime)));
        foreach (var measure in map.Measures)
        {
            var expectedCells = beatDivisor == 0 ? 1 : (int)Math.Ceiling(measure.LengthTicks * beatDivisor / 192d);
            Assert.That(lines.Count(line => line.Measure == measure.Index), Is.EqualTo(expectedCells));
            if (expectedCells > 1)
            {
                var firstSubdivision = lines.Single(line => line.Measure == measure.Index && line.Subdivision == 1);
                Assert.That(firstSubdivision.Time - map.ProjectTickToTime(measure.StartTick), Is.EqualTo(2000d / beatDivisor).Within(1e-9));
            }
        }

        if (beatDivisor == 0)
            Assert.That(lines.All(line => line.IsMeasureLine), Is.True);
    }

    [Test]
    public void SnapUsesMusicalDistanceAcrossBpmChanges()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 60, 0), new(20, 480, 416.666666666667)], []);

        // Tick 20 is closer to tick 0 than tick 48, despite being much closer to tick 48 in milliseconds.
        Assert.That(BmsPracticeGrid.Create(map, 4).Snap(map.ProjectTickToTime(20)), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Create(map, 4).Snap(map.ProjectTickToTime(28)), Is.EqualTo(map.ProjectTickToTime(48)));
    }

    [Test]
    public void SnapKeepsMusicalPositionFrozenThroughoutStop()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], [new(18, 1000, 96, 120, 0)]);

        Assert.That(BmsPracticeGrid.Create(map, 4).Snap(1100), Is.Zero);
        Assert.That(BmsPracticeGrid.Create(map, 4).Snap(map.ProjectTickToTime(24)), Is.Zero);
        Assert.That(BmsPracticeGrid.Create(map, 4).Snap(map.ProjectTickToTime(25)), Is.EqualTo(map.ProjectTickToTime(48)));
    }

    [TestCase(4)]
    [TestCase(12)]
    [TestCase(32)]
    [TestCase(64)]
    public void VariableDivisionsKeepBpmAndStopProjection(int beatDivisor)
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]);
        var grid = BmsPracticeGrid.Create(map, beatDivisor);
        var lines = grid.Lines;

        foreach (var line in lines)
        {
            var tick = line.IsMeasureLine && line.Measure == 1 ? 192 : line.Subdivision * 192 / beatDivisor;
            Assert.That(line.Time, Is.EqualTo(map.ProjectTickToTime(tick)));
            Assert.That(grid.Snap(line.Time), Is.EqualTo(line.Time).Within(1e-9));
        }
    }

    [Test]
    public void FractionalGridSnapRespectsStopWithoutSmearingItsDuration()
    {
        var map = new BmsTimingMap(10, [new(0, 0, 10, 1)], [new(0, 120, 0)], [new(1, 500, 48, 120, 0)]);

        Assert.That(BmsPracticeGrid.Create(map, 16).Snap(150), Is.EqualTo(125));
        Assert.That(BmsPracticeGrid.Create(map, 16).Snap(300), Is.EqualTo(750));
        Assert.That(BmsPracticeGrid.Create(map, 16).Snap(650), Is.EqualTo(750));
        Assert.That(BmsPracticeGrid.Create(map, 16).Snap(770), Is.EqualTo(750));
    }

    [Test]
    public void BarSnapUsesActualIrregularMeasureAndClampsToChartBounds()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 20, 20d / 192), new(1, 20, 120, 0.625)], [new(0, 120, 0)], []);

        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(map.ProjectTickToTime(9)), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(map.ProjectTickToTime(11)), Is.EqualTo(map.ProjectTickToTime(20)));
        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(map.ProjectTickToTime(79)), Is.EqualTo(map.ProjectTickToTime(20)));
        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(map.ProjectTickToTime(81)), Is.EqualTo(map.ProjectTickToTime(140)));
        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(-100), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Create(map, 0).Snap(10000), Is.EqualTo(map.ProjectTickToTime(140)));
    }

    [Test]
    public void InvalidDivisionIsRejectedAndMissingTimingLeavesSnapUnchanged()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsPracticeGrid.Create(null, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsPracticeGrid.Create(null, -1).Snap(123));
        Assert.That(BmsPracticeGrid.Create(null, 0).Snap(123), Is.EqualTo(123));
        Assert.That(BmsPracticeGrid.Create(new BmsTimingMap(192, [], [], [])).Snap(123), Is.EqualTo(123));
    }

    [Test]
    public void NormalMeasureHasSixteenMusicalCellsAndClosingBarline()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], []);
        var lines = BmsPracticeGrid.Create(map).Lines;

        Assert.That(lines, Has.Length.EqualTo(17));
        Assert.That(lines.Select(line => line.Time), Is.EqualTo(Enumerable.Range(0, 17).Select(index => index * 125d)));
        Assert.That(lines.Where(line => line.IsMeasureLine).Select(line => line.Time), Is.EqualTo(new[] { 0d, 2000d }));
        Assert.That(lines.Take(16).Select(line => line.Subdivision), Is.EqualTo(Enumerable.Range(0, 16)));
        Assert.That(lines[^1].Measure, Is.EqualTo(1));
    }

    [Test]
    public void ShortAndIrregularMeasuresKeepTrueSixteenthLength()
    {
        var map = new BmsTimingMap(192,
        [
            new(0, 0, 192, 1),
            new(1, 192, 144, 0.75),
            new(2, 336, 120, 0.625),
            new(3, 456, 20, 20d / 192),
        ], [new(0, 120, 0)], []);
        var lines = BmsPracticeGrid.Create(map).Lines;

        Assert.That(lines.Count(line => line.Measure == 0), Is.EqualTo(16));
        Assert.That(lines.Count(line => line.Measure == 1), Is.EqualTo(12));
        Assert.That(lines.Count(line => line.Measure == 2), Is.EqualTo(10));
        Assert.That(lines.Where(line => line.Measure == 3).Select(line => line.Subdivision), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(lines.Where(line => line.IsMeasureLine).Select(line => line.Time), Is.EqualTo(new[] { 0d, 2000d, 3500d, 4750d, 4958.333333333333d }).Within(1e-9));
        Assert.That(lines.Single(line => line.Measure == 1 && line.Subdivision == 1).Time, Is.EqualTo(2125));
    }

    [Test]
    public void BpmChangesUseAuthoritativeTickProjection()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1000)], []);
        var lines = BmsPracticeGrid.Create(map).Lines;

        Assert.That(lines[0].Time, Is.Zero);
        Assert.That(lines[7].Time, Is.EqualTo(875));
        Assert.That(lines[8].Time, Is.EqualTo(1000));
        Assert.That(lines[9].Time, Is.EqualTo(1062.5));
        Assert.That(lines[^1].Time, Is.EqualTo(1500));
        foreach (var line in lines.Take(16))
            Assert.That(line.Time, Is.EqualTo(map.ProjectTickToTime(line.Subdivision * 12L)));
    }

    [Test]
    public void StopLeavesARealTimeGapWithoutAddingMusicalSubdivisions()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]);
        var lines = BmsPracticeGrid.Create(map).Lines;

        Assert.That(lines, Has.Length.EqualTo(17));
        Assert.That(lines[4].Time, Is.EqualTo(500));
        Assert.That(lines[5].Time, Is.EqualTo(1125));
        Assert.That(lines[8].Time, Is.EqualTo(1500));
        Assert.That(lines[9].Time, Is.EqualTo(1562.5));
        Assert.That(lines[^1].Time, Is.EqualTo(2000));
    }

    [Test]
    public void FractionalResolutionDoesNotDistributeStopAcrossNeighbouringGridLines()
    {
        var map = new BmsTimingMap(10, [new(0, 0, 10, 1)], [new(0, 120, 0)], [new(1, 500, 48, 120, 0)]);
        var lines = BmsPracticeGrid.Create(map).Lines;

        Assert.That(lines, Has.Length.EqualTo(17));
        Assert.That(lines[1].Time, Is.EqualTo(125));
        Assert.That(lines[2].Time, Is.EqualTo(750));
        Assert.That(lines[3].Time, Is.EqualTo(875));
        Assert.That(lines[^1].Time, Is.EqualTo(2500));
    }

    [Test]
    public void MissingTimingProducesNoInventedSecondBasedGrid()
    {
        Assert.That(BmsPracticeGrid.Create(null).Lines, Is.Empty);
    }
}
