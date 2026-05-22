using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class BmsEmbeddedSongDecoderTest
{
    private static readonly Assembly assembly = typeof(BmsEmbeddedSongDecoderTest).Assembly;

    private static readonly Dictionary<string, int> visible_channel_map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["16"] = 0,
        ["11"] = 1,
        ["12"] = 2,
        ["13"] = 3,
        ["14"] = 4,
        ["15"] = 5,
        ["18"] = 6,
        ["19"] = 7,
        ["21"] = 8,
        ["22"] = 9,
        ["23"] = 10,
        ["24"] = 11,
        ["25"] = 12,
        ["28"] = 13,
        ["29"] = 14,
        ["26"] = 15,
    };

    [TestCaseSource(nameof(getChartResourceNames))]
    public void TestEmbeddedSampleChartDecodes(string resourceName)
    {
        var beatmap = decodeResource(resourceName);
        var objects = beatmap.HitObjects.Cast<BmsHitObject>().ToArray();

        Assert.That(beatmap.Metadata.Title, Is.Not.Empty, resourceName);
        Assert.That(beatmap.Metadata.Artist, Is.Not.Empty, resourceName);
        Assert.That(objects.Length, Is.GreaterThan(100), resourceName);
        Assert.That(objects, Is.Ordered.By(nameof(BmsHitObject.StartTime)), resourceName);
        Assert.That(objects.Select(o => o.TickInfo.Tick), Is.Ordered, resourceName);
        Assert.That(objects.All(o => o.StartTime >= 0), Is.True, resourceName);
        Assert.That(objects.All(o => o.EndTime >= o.StartTime), Is.True, resourceName);
        Assert.That(beatmap.ControlPointInfo.TimingPoints, Is.Not.Empty, resourceName);
    }

    private static string[] getChartResourceNames() => assembly.GetManifestResourceNames()
        .Where(n => n.Contains("bms_test_songs", StringComparison.Ordinal))
        .Where(n => n.EndsWith(".bms", StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith(".bme", StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith(".bml", StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith(".pms", StringComparison.OrdinalIgnoreCase))
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    private static string[] getSampleResourceNames() => assembly.GetManifestResourceNames()
        .Where(n => n.Contains("bms_test_songs", StringComparison.Ordinal))
        .OrderBy(n => n, StringComparer.Ordinal)
        .ToArray();

    private static Beatmap decodeResource(string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        using var reader = new LineBufferedReader(stream);

        return new BmsBeatmapDecoder().Decode(reader);
    }

    private static Beatmap decodeFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new LineBufferedReader(stream);

        return new BmsBeatmapDecoder().Decode(reader);
    }

    private static BmsBeatmap convert(Beatmap beatmap) =>
        (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

    private static ChartExpectation analyseChart(string path, int tickResolution)
    {
        double initialBpm = 130;
        var measureLengths = new Dictionary<int, double>();
        var bpmDefinitions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var stopDefinitions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var channelLines = new List<ExpectedChannelLine>();
        var maxMeasure = 0;
        var sequence = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = stripComments(rawLine).Trim();

            if (line.Length == 0 || !line.StartsWith('#'))
                continue;

            var channelMatch = channelLineRegex().Match(line);

            if (channelMatch.Success)
            {
                var measure = int.Parse(channelMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                var channel = channelMatch.Groups[2].Value.ToUpperInvariant();
                var payload = channelMatch.Groups[3].Value.Trim();

                maxMeasure = Math.Max(maxMeasure, measure);

                if (channel == "02")
                {
                    if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out var length) && length > 0)
                        measureLengths[measure] = length;
                }
                else if (payload.Length >= 2)
                {
                    channelLines.Add(new ExpectedChannelLine(measure, channel, payload, sequence++ * 4096));
                }

                continue;
            }

            var commandMatch = commandLineRegex().Match(line);

            if (!commandMatch.Success)
                continue;

            var command = commandMatch.Groups[1].Value.ToUpperInvariant();
            var value = commandMatch.Groups[2].Value.Trim();

            if (command == "BPM" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var bpm) && bpm > 0)
                initialBpm = bpm;
            else if (command.Length == 5
                     && command.StartsWith("BPM", StringComparison.OrdinalIgnoreCase)
                     && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var extendedBpm)
                     && extendedBpm > 0)
                bpmDefinitions[command[3..5]] = extendedBpm;
            else if (command.Length == 6
                     && command.StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
                     && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var stopValue)
                     && stopValue > 0)
                stopDefinitions[command[4..6]] = stopValue;
        }

        var measureStarts = calculateMeasureStarts(measureLengths, maxMeasure, tickResolution);
        var timingEvents = collectExpectedTimingEvents(channelLines, bpmDefinitions, measureStarts, tickResolution, initialBpm);
        var stopEvents = collectExpectedStopEvents(channelLines, stopDefinitions, measureStarts, tickResolution, timingEvents);
        timingEvents = timingEvents.Select(e => e with { Time = e.Time + stopEvents.Where(s => s.Tick < e.Tick).Sum(s => s.Duration) }).ToList();

        var notes = channelLines.Where(l => visible_channel_map.ContainsKey(l.Channel))
            .SelectMany(l => expandExpectedCells(l, measureStarts))
            .Where(c => c.Value != "00")
            .Select(c => new ExpectedNote(c.Measure, c.Tick, c.Channel, visible_channel_map[c.Channel], c.Value, c.Sequence))
            .OrderBy(n => n.Tick)
            .ThenBy(n => n.Sequence)
            .ToArray();

        return new ChartExpectation(notes, timingEvents, stopEvents, tickResolution);
    }

    private static Dictionary<int, long> calculateMeasureStarts(IReadOnlyDictionary<int, double> measureLengths, int maxMeasure, int tickResolution)
    {
        var result = new Dictionary<int, long>();
        long currentTick = 0;

        for (var measure = 0; measure <= maxMeasure + 1; measure++)
        {
            result[measure] = currentTick;
            currentTick += (long)Math.Round(tickResolution * measureLengths.GetValueOrDefault(measure, 1));
        }

        return result;
    }

    private static List<ExpectedTimingEvent> collectExpectedTimingEvents(IEnumerable<ExpectedChannelLine> channelLines, IReadOnlyDictionary<string, double> bpmDefinitions,
                                                                         IReadOnlyDictionary<int, long> measureStarts, int tickResolution, double initialBpm)
    {
        var events = new List<ExpectedTimingEvent> { new(0, initialBpm, 0, 0) };

        foreach (var line in channelLines.Where(l => l.Channel is "03" or "08"))
        {
            foreach (var cell in expandExpectedCells(line, measureStarts).Where(c => c.Value != "00"))
            {
                double? bpm = line.Channel == "03"
                    ? int.TryParse(cell.Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexBpm) ? hexBpm : null
                    : bpmDefinitions.GetValueOrDefault(cell.Value);

                if (bpm is > 0)
                    events.Add(new ExpectedTimingEvent(cell.Tick, bpm.Value, 0, cell.Sequence));
            }
        }

        events = events.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList();
        double time = 0;
        var previousTick = events[0].Tick;
        var previousBpm = events[0].Bpm;

        for (var i = 0; i < events.Count; i++)
        {
            var current = events[i];

            if (i > 0)
                time += ticksToMilliseconds(current.Tick - previousTick, previousBpm, tickResolution);

            events[i] = current with { Time = time };
            previousTick = current.Tick;
            previousBpm = current.Bpm;
        }

        return events;
    }

    private static List<ExpectedStopEvent> collectExpectedStopEvents(IEnumerable<ExpectedChannelLine> channelLines, IReadOnlyDictionary<string, double> stopDefinitions,
                                                                     IReadOnlyDictionary<int, long> measureStarts, int tickResolution,
                                                                     IReadOnlyList<ExpectedTimingEvent> timingEvents)
    {
        var events = new List<ExpectedStopEvent>();

        foreach (var line in channelLines.Where(l => l.Channel == "09"))
        {
            foreach (var cell in expandExpectedCells(line, measureStarts).Where(c => c.Value != "00"))
            {
                if (!stopDefinitions.TryGetValue(cell.Value, out var stopValue))
                    continue;

                events.Add(new ExpectedStopEvent(cell.Tick, stopValue * 60000 / (bpmAtTick(cell.Tick, timingEvents) * 48), cell.Sequence));
            }
        }

        return events.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList();
    }

    private static IEnumerable<ExpectedCell> expandExpectedCells(ExpectedChannelLine line, IReadOnlyDictionary<int, long> measureStarts)
    {
        var pairCount = line.Payload.Length / 2;
        var measureStart = measureStarts[line.Measure];
        var measureLength = measureStarts[line.Measure + 1] - measureStart;

        for (var i = 0; i < pairCount; i++)
        {
            yield return new ExpectedCell(line.Measure, measureStart + measureLength * i / pairCount, line.Channel, line.Payload.Substring(i * 2, 2).ToUpperInvariant(),
                line.Sequence + i);
        }
    }

    private static double bpmAtTick(long tick, IReadOnlyList<ExpectedTimingEvent> timingEvents)
    {
        var current = timingEvents[0];

        for (var i = 1; i < timingEvents.Count; i++)
        {
            if (timingEvents[i].Tick > tick)
                break;

            current = timingEvents[i];
        }

        return current.Bpm;
    }

    private static double ticksToMilliseconds(long ticks, double bpm, int tickResolution) =>
        ticks * (60000 / bpm) / (tickResolution / 4d);

    private static string stripComments(string line)
    {
        var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    [GeneratedRegex(@"^#(\d{3})([0-9A-Z]{2}):(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex channelLineRegex();

    [GeneratedRegex(@"^#([A-Z0-9]+)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex commandLineRegex();

    static internal string TestSongsRoot => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "bms_test_songs"));

    private sealed record ChartExpectation(
        IReadOnlyList<ExpectedNote> Notes,
        IReadOnlyList<ExpectedTimingEvent> TimingEvents,
        IReadOnlyList<ExpectedStopEvent> StopEvents,
        int TickResolution)
    {
        public double ProjectTickToTime(long tick, bool includeStops)
        {
            var current = TimingEvents[0];

            for (var i = 1; i < TimingEvents.Count; i++)
            {
                if (TimingEvents[i].Tick > tick)
                    break;

                current = TimingEvents[i];
            }

            var stopOffset = includeStops ? StopEvents.Where(s => s.Tick >= current.Tick && s.Tick < tick).Sum(s => s.Duration) : 0;

            return current.Time + ticksToMilliseconds(tick - current.Tick, current.Bpm, TickResolution) + stopOffset;
        }
    }

    private readonly record struct ExpectedChannelLine(int Measure, string Channel, string Payload, int Sequence);

    private readonly record struct ExpectedCell(int Measure, long Tick, string Channel, string Value, int Sequence);

    private readonly record struct ExpectedNote(int Measure, long Tick, string Channel, int Column, string Value, int Sequence);

    private readonly record struct ExpectedTimingEvent(long Tick, double Bpm, double Time, int Sequence);

    private readonly record struct ExpectedStopEvent(long Tick, double Duration, int Sequence);

    [Test]
    public void TestDoublePlayChartsConvertWithSixteenColumns()
    {
        foreach (var resourceName in getChartResourceNames().Where(n => n.Contains("._14", StringComparison.Ordinal) || n.Contains(".mianhaeyo", StringComparison.Ordinal)))
        {
            var beatmap = decodeResource(resourceName);
            var converted = convert(beatmap);

            Assert.That(converted.TotalColumns, Is.EqualTo(16), resourceName);
        }
    }

    [Test]
    public void TestRealFilesystemBmsFileDecodes()
    {
        var path = Path.Combine(TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");

        var beatmap = decodeFile(path);
        var converted = convert(beatmap);

        Assert.That(converted.Metadata.Title, Is.EqualTo("Aleph-0[NORMAL]"));
        Assert.That(converted.HitObjects, Has.Count.GreaterThan(100));
        Assert.That(converted.TotalColumns, Is.EqualTo(8));
    }

    [Test]
    public void TestSampleChartsAreEmbedded()
    {
        Assert.That(getChartResourceNames(), Has.Length.EqualTo(20));
        Assert.That(getSampleResourceNames(), Has.Length.GreaterThan(100));
        Assert.That(getSampleResourceNames().Any(n => n.EndsWith(".kick_deep2.ogg", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(getSampleResourceNames().Any(n => n.EndsWith("._bga.mpg", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(getChartResourceNames().Any(n => n.Contains("Destr0yer", StringComparison.Ordinal)), Is.True);
        Assert.That(getChartResourceNames().Any(n => n.Contains("_Clue_Random", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void TestSevenKeyChartsConvertWithEightColumns()
    {
        foreach (var resourceName in getChartResourceNames().Where(n => n.Contains("._7", StringComparison.Ordinal)))
        {
            var beatmap = decodeResource(resourceName);
            var converted = convert(beatmap);

            Assert.That(converted.TotalColumns, Is.EqualTo(8), resourceName);
        }
    }

    [Test]
    public void TestSevenNormalDecodedObjectsMatchBmsTextSemantics()
    {
        var path = Path.Combine(TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
        var beatmap = decodeFile(path);
        var converted = convert(beatmap);
        var objects = beatmap.HitObjects.Cast<BmsHitObject>().ToArray();
        var tickResolution = converted.TickResolution;
        var expected = analyseChart(path, tickResolution);

        Assert.That(expected.Notes, Has.Count.GreaterThan(100));
        Assert.That(objects, Has.Length.EqualTo(expected.Notes.Count));
        Assert.That(objects.Any(o => o.IsLongNote), Is.False);

        var expectedFirst = expected.Notes[0];
        var actualFirst = objects[0];

        Assert.That(expectedFirst.Measure, Is.EqualTo(49));
        Assert.That(expectedFirst.Channel, Is.EqualTo("11"));
        Assert.That(expectedFirst.Value, Is.EqualTo("08"));
        Assert.That(actualFirst.Column, Is.EqualTo(expectedFirst.Column));
        Assert.That(actualFirst.SourceChannel, Is.EqualTo(expectedFirst.Channel));
        Assert.That(actualFirst.SampleKey, Is.EqualTo(expectedFirst.Value));
        Assert.That(actualFirst.TickInfo.Tick, Is.EqualTo(expectedFirst.Tick));

        Assert.That(actualFirst.StartTime, Is.EqualTo(expected.ProjectTickToTime(expectedFirst.Tick, true)).Within(0.001));
        Assert.That(
            expected.ProjectTickToTime(expectedFirst.Tick, true) - expected.ProjectTickToTime(expectedFirst.Tick, false),
            Is.EqualTo(960).Within(0.001));

        var expectedColumnCounts = expected.Notes.GroupBy(n => n.Column).ToDictionary(g => g.Key, g => g.Count());
        var actualColumnCounts = objects.GroupBy(o => o.Column).ToDictionary(g => g.Key, g => g.Count());

        Assert.That(actualColumnCounts, Is.EquivalentTo(expectedColumnCounts));
        Assert.That(expected.TimingEvents.Any(t => Math.Abs(t.Bpm - 0.2441406) < 0.0001), Is.True);
        Assert.That(beatmap.ControlPointInfo.TimingPoints.Any(t => Math.Abs(t.BPM - 250) < 0.0001), Is.True);
    }
}
