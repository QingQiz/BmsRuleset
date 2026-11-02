using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeGridTest
{
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
        var lines = BmsPracticeGrid.Create(map, beatDivisor);

        Assert.That(lines, Has.Length.EqualTo(beatDivisor + 1));
        Assert.That(lines.Select(line => line.Time),
            Is.EqualTo(Enumerable.Range(0, beatDivisor + 1).Select(index => index * 2000d / beatDivisor)).Within(1e-9));
        foreach (var line in lines)
            Assert.That(BmsPracticeGrid.Snap(map, line.Time, beatDivisor), Is.EqualTo(line.Time).Within(1e-9));
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
        var lines = BmsPracticeGrid.Create(map, beatDivisor);

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
    public void SnapUsesMusicalDistanceAcrossBpmChangesAndLeadIn()
    {
        var original = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 60, 0), new(20, 480, 416.666666666667)], []);
        var map = original.ShiftedBy(2500);

        // Tick 20 is closer to tick 0 than tick 48, despite being much closer to tick 48 in milliseconds.
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(20), 4), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(28), 4), Is.EqualTo(map.ProjectTickToTime(48)));
    }

    [Test]
    public void SnapKeepsMusicalPositionFrozenThroughoutStop()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], [new(18, 1000, 96, 120, 0)]);

        Assert.That(BmsPracticeGrid.Snap(map, 1100, 4), Is.Zero);
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(24), 4), Is.Zero);
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(25), 4), Is.EqualTo(map.ProjectTickToTime(48)));
    }

    [TestCase(4)]
    [TestCase(12)]
    [TestCase(32)]
    [TestCase(64)]
    public void VariableDivisionsKeepBpmAndStopProjection(int beatDivisor)
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]).ShiftedBy(300);
        var lines = BmsPracticeGrid.Create(map, beatDivisor);

        foreach (var line in lines)
        {
            var tick = line.IsMeasureLine && line.Measure == 1 ? 192 : line.Subdivision * 192 / beatDivisor;
            Assert.That(line.Time, Is.EqualTo(map.ProjectTickToTime(tick)));
            Assert.That(BmsPracticeGrid.Snap(map, line.Time, beatDivisor), Is.EqualTo(line.Time).Within(1e-9));
        }
    }

    [Test]
    public void FractionalGridSnapRespectsStopWithoutSmearingItsDuration()
    {
        var map = new BmsTimingMap(10, [new(0, 0, 10, 1)], [new(0, 120, 0)], [new(1, 500, 48, 120, 0)]);

        Assert.That(BmsPracticeGrid.Snap(map, 150, 16), Is.EqualTo(125));
        Assert.That(BmsPracticeGrid.Snap(map, 300, 16), Is.EqualTo(750));
        Assert.That(BmsPracticeGrid.Snap(map, 650, 16), Is.EqualTo(750));
        Assert.That(BmsPracticeGrid.Snap(map, 770, 16), Is.EqualTo(750));
    }

    [Test]
    public void BarSnapUsesActualIrregularMeasureAndClampsToChartBounds()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 20, 20d / 192), new(1, 20, 120, 0.625)], [new(0, 120, 0)], []);

        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(9), 0), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(11), 0), Is.EqualTo(map.ProjectTickToTime(20)));
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(79), 0), Is.EqualTo(map.ProjectTickToTime(20)));
        Assert.That(BmsPracticeGrid.Snap(map, map.ProjectTickToTime(81), 0), Is.EqualTo(map.ProjectTickToTime(140)));
        Assert.That(BmsPracticeGrid.Snap(map, -100, 0), Is.EqualTo(map.ProjectTickToTime(0)));
        Assert.That(BmsPracticeGrid.Snap(map, 10000, 0), Is.EqualTo(map.ProjectTickToTime(140)));
    }

    [Test]
    public void InvalidDivisionIsRejectedAndMissingTimingLeavesSnapUnchanged()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsPracticeGrid.Create(null, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsPracticeGrid.Snap(null, 123, -1));
        Assert.That(BmsPracticeGrid.Snap(null, 123, 0), Is.EqualTo(123));
        Assert.That(BmsPracticeGrid.Snap(new BmsTimingMap(192, [], [], []), 123), Is.EqualTo(123));
    }

    [Test]
    public void NormalMeasureHasSixteenMusicalCellsAndClosingBarline()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], []);
        var lines = BmsPracticeGrid.Create(map);

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
        var lines = BmsPracticeGrid.Create(map);

        Assert.That(lines.Count(line => line.Measure == 0), Is.EqualTo(16));
        Assert.That(lines.Count(line => line.Measure == 1), Is.EqualTo(12));
        Assert.That(lines.Count(line => line.Measure == 2), Is.EqualTo(10));
        Assert.That(lines.Where(line => line.Measure == 3).Select(line => line.Subdivision), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(lines.Where(line => line.IsMeasureLine).Select(line => line.Time), Is.EqualTo(new[] { 0d, 2000d, 3500d, 4750d, 4958.333333333333d }).Within(1e-9));
        Assert.That(lines.Single(line => line.Measure == 1 && line.Subdivision == 1).Time, Is.EqualTo(2125));
    }

    [Test]
    public void BpmChangesAndLeadInUseAuthoritativeTickProjection()
    {
        var original = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1000)], []);
        var map = original.ShiftedBy(250);
        var lines = BmsPracticeGrid.Create(map);

        Assert.That(lines[0].Time, Is.EqualTo(250));
        Assert.That(lines[7].Time, Is.EqualTo(1125));
        Assert.That(lines[8].Time, Is.EqualTo(1250));
        Assert.That(lines[9].Time, Is.EqualTo(1312.5));
        Assert.That(lines[^1].Time, Is.EqualTo(1750));
        foreach (var line in lines.Take(16))
            Assert.That(line.Time, Is.EqualTo(map.ProjectTickToTime(line.Subdivision * 12L)));
    }

    [Test]
    public void StopLeavesARealTimeGapWithoutAddingMusicalSubdivisions()
    {
        var map = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]);
        var lines = BmsPracticeGrid.Create(map);

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
        var lines = BmsPracticeGrid.Create(map);

        Assert.That(lines, Has.Length.EqualTo(17));
        Assert.That(lines[1].Time, Is.EqualTo(125));
        Assert.That(lines[2].Time, Is.EqualTo(750));
        Assert.That(lines[3].Time, Is.EqualTo(875));
        Assert.That(lines[^1].Time, Is.EqualTo(2500));
    }

    [Test]
    public void MissingTimingProducesNoInventedSecondBasedGrid()
    {
        Assert.That(BmsPracticeGrid.Create(null), Is.Empty);
    }
}
