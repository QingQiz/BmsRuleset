using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
[NonParallelizable]
public class BmsMeasureLineIndexTest
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void LinesIncludeMeasureZeroAndFinalBoundaryWithLeadIn()
    {
        var map = BmsChartParser.Parse(["#BPM 120", "#00111:01"]).TimingMap.ShiftedBy(1234.5);
        using var pair = new Pair(map);
        var infos = lineInfos(pair.Actual);

        Assert.Multiple(() =>
        {
            Assert.That(infos.Select(line => line.ScrollPosition), Is.EqualTo(new[] { 0d, 2000, 4000 }));
            Assert.That(infos.Select(line => line.Time), Is.EqualTo(new[] { 1234.5, 3234.5, 5234.5 }));
        });
    }

    [TestCase(8)]
    [TestCase(128)]
    [TestCase(4096)]
    public void RangesKeepMembershipInsertionAndPoolOrder(int count)
    {
        var map = createMap(count);
        using var pair = new Pair(map);
        var positions = lineInfos(pair.Actual).Select(line => line.ScrollPosition).ToArray();
        var ranges = positions.Where(double.IsFinite).Take(128).SelectMany(position => new[]
        {
            (position, position), (position - .00001, position), (position, position + .00001),
            (position - 1500, position + 2500), (position + 1, position - 1),
        }).Concat(new[] { (double.NegativeInfinity, double.PositiveInfinity), (1e12, 1e12 + 1) }).ToArray();
        foreach (var (min, max) in ranges.Concat(ranges.Reverse()))
            pair.Range(min, max);
        TestContext.WriteLine($"MEASURE_INDEX measures={count} ranges={ranges.Length * 2}");
    }

    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void NonfiniteCoordinatesAndBoundsKeepScanResults(double factor)
    {
        var map = new BmsTimingMap(192, Enumerable.Range(0, 8).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
            [new(0, 120, 0)], [], [new(192, -1, 0), new(384, factor, 1), new(576, double.NaN, 2)], [], 120);
        using var pair = new Pair(map);
        pair.Range(double.NegativeInfinity, double.PositiveInfinity);
        pair.TimeRange(0, 20000);
        foreach (var (min, max) in new[]
        {
            (0d, 2000d), (factor, factor), (double.NaN, 1000), (0, double.NaN),
            (double.NaN, double.NaN), (double.NegativeInfinity, double.PositiveInfinity), (1d, 0d),
        })
            pair.Range(min, max);
    }

    [Test]
    public void MapReloadAndTimeModeRefreshMembership()
    {
        using var pair = new Pair(createMap(128));
        pair.Range(-1000, 4000);
        pair.TimeRange(0, 25000);
        pair.Range(-1000, 4000);
        pair.TimeRange(18000, 22000);
        pair.Range(-1000, 4000);
        pair.Reset(createMap(32));
        pair.Range(-1000, 4000);
        pair.Reset(null);
        pair.AssertSame();
        pair.Reset(createMap(128).ShiftedBy(1234.5));
        pair.Range(-1000, 4000);
        pair.TimeRange(1234.5, 20000);
        pair.Range(-1000, 4000);
    }

    [Test]
    public void MonotonicAndEmptyMapsKeepOriginalRangeResults()
    {
        foreach (var count in new[] { 0, 1, 128 })
        {
            var map = new BmsTimingMap(192, Enumerable.Range(0, count).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
                [new(0, 120, 0)], [], [], [], 120);
            using var pair = new Pair(map);
            foreach (var time in new[] { -1000d, 0, 1999, 2000, 2001, 5000, 20000, 0 })
            {
                pair.Scroll.Update(time);
                invoke(pair.Actual, "Update");
                if (count > 0)
                    invoke(pair.Reference, "updateRangeByScroll", time, time + pair.Scroll.MeasureLineFutureWindow + 500);
                pair.RecordExpected(time, time + pair.Scroll.MeasureLineFutureWindow + 500, false);
                pair.AssertSame();
            }
        }
    }

    [TestCase("00_outlaw_spb.bms")]
    [TestCase("01_outlaw_spn.bms")]
    [TestCase("01_outlaw_spn2.bms")]
    [TestCase("02_outlaw_sph.bms")]
    [TestCase("03_outlaw_spa.bms")]
    [TestCase("99_outlaw_not_guilty.bms")]
    [TestCase("99_outlaw_prison_breaker.bms")]
    [TestCase("99_outlaw_scrollplus.bms")]
    public void RealChartAdvancingAndRewindingFramesKeepScanResults(string chart)
    {
        using var pair = new Pair(loadMap(chart));
        var monotonic = (bool)typeof(BmsMeasureLineContainer).GetField("scrollPositionsMonotonic", instance)!.GetValue(pair.Actual)!;
        TestContext.WriteLine($"MEASURE_INDEX_CHART chart={chart} lines={lineInfos(pair.Actual).Length} monotonic={monotonic}");
        foreach (var speed in new[] { 1d, 8, 12 })
        {
            pair.Scroll.SetConfiguredScrollSpeed(speed);
            for (var i = 0; i < 1000; i++)
            {
                pair.Scroll.ConstantScrollActive = i % 113 < 7;
                pair.Scroll.Update(i < 500 ? 60000 + i : 61000 - i);
                invoke(pair.Actual, "Update");
                var min = pair.Scroll.CurrentScrollPosition;
                var max = min + pair.Scroll.MeasureLineFutureWindow + 500;
                invoke(pair.Reference, pair.Scroll.ConstantScrollActive ? "updateRangeByTime" : monotonic ? "updateRangeByScroll" : "updateRangeByScan", min, max);
                pair.RecordExpected(min, max, !pair.Scroll.ConstantScrollActive && !monotonic);
                pair.AssertSame();
            }
        }
        TestContext.WriteLine($"MEASURE_INDEX_REAL chart={chart} lines={lineInfos(pair.Actual).Length} frames=3000");
    }

    private static BmsTimingMap createMap(int count)
    {
        double[] factors = [1, 0, -2, 3, 0, -.5];
        return new BmsTimingMap(192, Enumerable.Range(0, count).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
            [new(0, 120, 0)], [], Enumerable.Range(0, count).Select(i => new BmsScrollEvent(i * 192, factors[i % factors.Length], i)), [], 120);
    }

    private static BmsTimingMap loadMap(string chart)
    {
        using var stream = File.OpenRead(chartPath(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder(_ => 1).Decode(reader);
        return ((BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { BranchRandomValueSelector = _ => 1 }.Convert()).TimingMap!;
    }

    private static string chartPath(string chart, [CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../../bms_test_songs/103_outlaw_ogg", chart));

    private static void invoke(BmsMeasureLineContainer container, string method, params object[] args)
        => typeof(BmsMeasureLineContainer).GetMethod(method, instance)!.Invoke(container, args);

    private static void applyRange(BmsMeasureLineContainer container, double min, double max)
        => invoke(container, "updateRangeByScan", min, max);

    private static Dictionary<int, BmsMeasureLine> active(BmsMeasureLineContainer container)
        => (Dictionary<int, BmsMeasureLine>)typeof(BmsMeasureLineContainer).GetField("activeLines", instance)!.GetValue(container)!;

    private static BmsMeasureLineContainer.MeasureLineInfo[] lineInfos(BmsMeasureLineContainer container)
        => (BmsMeasureLineContainer.MeasureLineInfo[])typeof(BmsMeasureLineContainer).GetField("lines", instance)!.GetValue(container)!;

    private sealed class Pair : IDisposable
    {
        private readonly BmsPlayfield playfield = new(new BmsBeatmap { TotalColumns = BmsLayout.BME7_KEY_COLUMNS, LayoutVariant = BmsLayoutVariant.Bme7K });
        private readonly Dictionary<BmsMeasureLine, int> actualIds = [];
        private readonly Dictionary<BmsMeasureLine, int> referenceIds = [];
        private readonly HashSet<int> expectedIndices = [];
        internal BmsMeasureLineContainer Actual { get; } = new();
        internal BmsMeasureLineContainer Reference { get; } = new();
        internal BmsGameplayScrollController Scroll { get; private set; } = null!;

        internal Pair(BmsTimingMap map)
        {
            // This unloaded fixture has no dependency loader to supply the skin source
            // that the playfield's disposal path expects.
            typeof(BmsPlayfield).GetProperty("parentSkin", instance)!.SetValue(playfield,
                typeof(BmsPlayfield).GetField("activeSkin", instance)!.GetValue(playfield));
            Reset(map);
        }

        internal void Reset(BmsTimingMap map)
        {
            Scroll = new BmsGameplayScrollController(map);
            Actual.SetTimingMap(map, Scroll, playfield.Stage);
            Reference.SetTimingMap(map, Scroll, playfield.Stage);
            actualIds.Clear();
            referenceIds.Clear();
            expectedIndices.Clear();
        }

        internal void Range(double min, double max)
        {
            applyRange(Actual, min, max);
            invoke(Reference, "updateRangeByScan", min, max);
            RecordExpected(min, max, true);
            AssertSame();
        }

        internal void TimeRange(double min, double max)
        {
            invoke(Actual, "updateRangeByTime", min, max);
            invoke(Reference, "updateRangeByTime", min, max);
            RecordExpected(min, max, false, true);
            AssertSame();
        }

        internal void RecordExpected(double min, double max, bool inclusiveEnd, bool timeMode = false)
        {
            var infos = lineInfos(Actual);
            double value(int index) => timeMode || Scroll.ConstantScrollActive ? infos[index].Time : infos[index].ScrollPosition;
            if (inclusiveEnd)
                expectedIndices.RemoveWhere(index => value(index) < min || value(index) > max);
            else
                expectedIndices.Clear();
            for (var index = 0; index < infos.Length; index++)
            {
                var coordinate = value(index);
                if (coordinate >= min && (inclusiveEnd ? coordinate <= max : coordinate < max)
                    && (inclusiveEnd || double.IsFinite(coordinate)))
                    expectedIndices.Add(index);
            }
        }

        internal void AssertSame()
        {
            Assert.That(snapshot(Actual, actualIds), Is.EqualTo(snapshot(Reference, referenceIds)));
            Assert.That(active(Actual).Keys.Order(), Is.EqualTo(expectedIndices.Order()));
            Assert.That(Actual.Children.Select(child => actualIds[(BmsMeasureLine)child]), Is.EqualTo(Reference.Children.Select(child => referenceIds[(BmsMeasureLine)child])));
        }

        private static (int Index, int Identity)[] snapshot(BmsMeasureLineContainer container, Dictionary<BmsMeasureLine, int> ids)
            => active(container).Select(entry =>
            {
                if (!ids.TryGetValue(entry.Value, out var identity)) ids.Add(entry.Value, identity = ids.Count);
                return (entry.Key, identity);
            }).ToArray();

        public void Dispose()
        {
            Actual.Dispose();
            Reference.Dispose();
            playfield.Dispose();
        }
    }
}
