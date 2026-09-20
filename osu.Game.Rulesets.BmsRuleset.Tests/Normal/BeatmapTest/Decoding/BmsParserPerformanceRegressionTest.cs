using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsParserPerformanceRegressionTest
{
    [TestCase("dense", "6DFA4681290A6BC94746D6321A1FCFD4B753DFE0A854785DDD850B190F635D39")]
    [TestCase("timing", "40A497ECCDD1FAF0B80732EDAFC52EF341A7F06CEC9D211B5BE17B5A2C32A128")]
    [TestCase("longnotes", "F14DADB46553C6AB69691670676B17090C31621EBC75D785A85AC82E4DE72D4D")]
    public void GeneratedChartKeepsCompleteParsedResult(string scenario, string expectedFingerprint)
    {
        var lines = CreateChart(scenario);
        var parsed = BmsChartParser.Parse(lines, "fixture.bme", _ => 1);
        var summary = BmsChartParser.ParseImportSummary(lines, "fixture.bme", _ => 1);
        var fingerprint = Fingerprint(parsed);
        Assert.That(fingerprint, Is.EqualTo(expectedFingerprint));
        Assert.That(summary.TotalObjectCount, Is.EqualTo(parsed.HitObjects.Count));
        Assert.That(summary.StarRatingNoteTimings.Select(n => (n.Column, n.StartTime, n.EndTime)),
            Is.EquivalentTo(parsed.HitObjects.Where(n => !n.IsMine).Select(n => (n.Column, n.StartTime, n.StartTime + n.Duration))));
    }

    [Test]
    public void ProjectionKeepsCoincidentStopsAndLastBpmAtTheSameTick()
    {
        BmsBpmEvent[] bpms = [new(0, 120, 0), new(192, 180, 2300, 1), new(192, -240, 2300, 2), new(768, 150, 6125, 3)];
        BmsStopEvent[] stops = [new(0, 100, 0, 120, 0), new(0, 200, 0, 120, 1), new(192, 50, 0, 240, 2), new(192, 75, 0, 240, 3), new(576, 700, 0, 240, 4)];
        var converter = new BmsTickTimeConverter(192, bpms, stops);
        long[] ticks = [-192, -1, 0, 1, 191, 192, 193, 575, 576, 577, 767, 768, 769, 2000];
        foreach (var tick in ticks.Concat(ticks.Reverse()))
        {
            var bpm = bpms.LastOrDefault(b => b.Tick <= tick);
            if (bpm == default) bpm = bpms[0];
            var stopDuration = stops.Where(s => s.Tick >= bpm.Tick && s.Tick < tick).Sum(s => s.Duration);
            var expected = bpm.Time + (tick - bpm.Tick) * (60000 / Math.Abs(bpm.Bpm)) / 48 + stopDuration;
            Assert.That(converter.ProjectTickToTime(tick), Is.EqualTo(expected).Within(1e-10), $"tick {tick}");
        }
    }

    [Test]
    public void ProjectionDoesNotSearchStopOriginAgainForEveryNote()
    {
        var bpms = new[] { new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(12000, 180, 150000) };
        var stops = new CountingStops(Enumerable.Range(0, 4096).Select(i => new BmsStopEvent(i * 4, 1, 0, 120, i)).ToArray());
        var converter = new BmsTickTimeConverter(192, bpms, stops);
        stops.Reads = 0;
        for (var i = 0; i < 1000; i++)
            converter.ProjectTickToTime(i * 13);
        Assert.That(stops.Reads, Is.LessThanOrEqualTo(14000), "Only the query endpoint needs a STOP lookup; the BPM origin is invariant.");
    }

    [Test]
    public void ShiftedTimingMapDoesNotRebuildOrResortItsEventTimeline()
    {
        var map = CreateTimingMap(4096);
        map.ShiftedBy(1000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var shifted = map.ShiftedBy(1000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.LessThan(1_100_000));
        foreach (var tick in new long[] { -1, 0, 1, 37, 38, 3800, 155610 })
            Assert.That(shifted.ProjectTickToTime(tick), Is.EqualTo(map.ProjectTickToTime(tick) + 1000).Within(1e-7));
        foreach (var time in new[] { -50.0, 0, 1, 100, 4000, 32000, 900000 })
        {
            Assert.That(shifted.GetScrollPositionAtTime(time + 1000), Is.EqualTo(map.GetScrollPositionAtTime(time)).Within(1e-7));
            Assert.That(shifted.GetScrollFactorAtTime(time + 1000), Is.EqualTo(map.GetScrollFactorAtTime(time)));
            Assert.That(shifted.GetSpeedFactorAtTime(time + 1000), Is.EqualTo(map.GetSpeedFactorAtTime(time)));
        }
        Assert.Throws<InvalidOperationException>(() => shifted.ShiftedBy(10));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(257)]
    [TestCase(4096)]
    public void CellSortingPreservesSourceOrderForEqualKeys(int count)
    {
        var random = new Random(713);
        var input = Enumerable.Range(0, count)
            .Select(i => (Tick: (long)random.Next(8), Sequence: random.Next(4), Value: (ushort)i)).ToArray();
        var ordered = input.OrderBy(c => c.Tick).ThenBy(c => c.Sequence).ToArray();
        var cellType = typeof(BmsChartParser).GetNestedType("RawCell", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(cellType);
        var sort = typeof(BmsChartParser).GetMethod("sortCells", BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var source in new[] { input, ordered, ordered.Reverse().ToArray() })
        {
            var cells = (IList)Activator.CreateInstance(listType)!;
            foreach (var cell in source)
                cells.Add(Activator.CreateInstance(cellType, cell.Tick, (ushort)0, cell.Value, cell.Sequence, 0));

            // LINQ supplies an independent stable-order oracle, including colliding source sequences.
            var expected = cells.Cast<object>()
                .OrderBy(c => (long)cellType.GetProperty("Tick")!.GetValue(c)!)
                .ThenBy(c => (int)cellType.GetProperty("Sequence")!.GetValue(c)!).ToArray();
            sort.Invoke(null, [cells]);
            Assert.That(cells.Cast<object>(), Is.EqualTo(expected));
        }
    }

    [Test]
    public void SortedAndUnsortedTimingInputsKeepCoincidentEventSemantics()
    {
        BmsMeasureInfo[] measures = [new(0, 0, 192, 1), new(1, 192, 192, 1)];
        BmsBpmEvent[] bpms = [new(0, 120, 0), new(192, 180, 2000, 1), new(192, -240, 2000, 2), new(384, 120, 3375, 3)];
        BmsStopEvent[] stops = [new(192, 125, 0, 240, 0), new(192, 250, 0, 240, 1)];
        BmsScrollEvent[] scrolls = [new(192, 0.5, 1), new(192, 2, 2)];
        BmsSpeedEvent[] speeds = [new(192, 0.75, 1), new(192, 1.25, 2)];
        var sorted = BmsTimingMap.FromSortedEvents(192, measures, bpms, stops, scrolls, speeds, 120);
        var unsorted = new BmsTimingMap(192, measures.Reverse(), bpms.Reverse(), stops.Reverse(), scrolls.Reverse(), speeds.Reverse(), 120);
        (double Time, double Position, double Scroll, double Speed)[] expected =
        [
            (0, 0, 1, 1), (1999, 1999, 1, 1), (2000, 2000, 1, 1), (2125, 2000, 1, 1),
            (2374, 2000, 1, 1), (2375, 2000, 2, 1.25), (2625, 1000, 2, 1.25),
            (3375, -2000, 2, 1.25), (3625, -1500, 2, 1.25),
        ];
        Assert.That(unsorted.Measures, Is.EqualTo(measures));
        foreach (var map in new[] { sorted, unsorted })
        {
            foreach (var sample in expected.Concat(expected.Reverse()))
            {
                Assert.That(map.GetScrollPositionAtTime(sample.Time), Is.EqualTo(sample.Position).Within(1e-10));
                Assert.That(map.GetScrollFactorAtTime(sample.Time), Is.EqualTo(sample.Scroll));
                Assert.That(map.GetSpeedFactorAtTime(sample.Time), Is.EqualTo(sample.Speed));
            }
            Assert.That(map.ProjectTickToTime(192), Is.EqualTo(2000));
            Assert.That(map.ProjectTickToTime(240), Is.EqualTo(2625));
            Assert.That(map.ProjectTickToTime(384), Is.EqualTo(3375));
        }
    }

    internal static BmsTimingMap CreateTimingMap(int count)
    {
        var bpms = Enumerable.Range(0, count).Select(i => new BmsBpmEvent(i * 37, i % 3 == 0 ? -150 : 180, i * 500, i)).ToArray();
        var stops = Enumerable.Range(0, count / 2).Select(i => new BmsStopEvent(i * 74, 20, 0, 150, i)).ToArray();
        var scroll = Enumerable.Range(0, count / 2).Select(i => new BmsScrollEvent(i * 74 + 1, i % 3 * 0.5, i)).ToArray();
        var speed = Enumerable.Range(0, count / 2).Select(i => new BmsSpeedEvent(i * 74 + 2, 0.5 + i % 4, i)).ToArray();
        return new BmsTimingMap(192, [new(0, 0, 192, 1)], bpms, stops, scroll, speed, 150);
    }

    internal static string[] CreateChart(string scenario)
    {
        var lines = new List<string>
        {
            "#TITLE Parser performance", "#ARTIST Fixture", "#BPM 150", "#RANK 2", "#WAV01 tap.wav", "#WAV02 tail.wav",
            "#BPM01 180", "#BPM02 120", "#STOP01 12", "#SCROLL01 0.5", "#SCROLL02 2", "#SPEED01 1.25",
            "#BMP01 background.png", "#TEXT01 text", "#EXRANK01 150",
        };
        var payload = string.Concat(Enumerable.Repeat("01", 16));
        var lnPayload = string.Concat(Enumerable.Repeat("01000200", 4));
        for (var measure = 1; measure <= 320; measure++)
        {
            foreach (var channel in new[] { "11", "12", "13", "14", "15", "18", "19", "16" })
                lines.Add($"#{measure:D3}{channel}:{payload}");
            if (scenario == "timing")
            {
                lines.Add($"#{measure:D3}08:01020102010201020102010201020102");
                lines.Add($"#{measure:D3}09:01000100010001000100010001000100");
                lines.Add($"#{measure:D3}SC:01020000");
                lines.Add($"#{measure:D3}SP:0001");
                lines.Add($"#{measure:D3}01:{payload}");
                lines.Add($"#{measure:D3}04:01");
                lines.Add($"#{measure:D3}99:01");
                lines.Add($"#{measure:D3}A0:01");
            }
            if (scenario == "longnotes")
            {
                lines.Add($"#{measure:D3}51:{lnPayload}");
                lines.Add($"#{measure:D3}52:{lnPayload}");
                lines.Add($"#{measure:D3}D3:00010000");
            }
        }
        return lines.ToArray();
    }

    internal static string Fingerprint(BmsParseResult result) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result)));

    private sealed class CountingStops(BmsStopEvent[] items) : IReadOnlyList<BmsStopEvent>
    {
        public int Reads { get; set; }
        public int Count => items.Length;
        public BmsStopEvent this[int index] { get { Reads++; return items[index]; } }
        public IEnumerator<BmsStopEvent> GetEnumerator() => ((IEnumerable<BmsStopEvent>)items).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
