using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private const int base_tick_resolution = 192;

    private readonly record struct RawChannelLine(int Measure, string Channel, string Payload, int Sequence);

    private readonly record struct RawCell(long Tick, string Channel, string Value, int Sequence, int Column);

    private readonly record struct TimingEvent(long Tick, double Bpm, double Time, int Sequence = 0);

    private readonly record struct StopEvent(long Tick, double Duration, double StopValue, double Bpm, int Sequence);

    public static BmsParseResult Parse(IEnumerable<string> lines, string? path = null, Func<int, int>? randomValueSelector = null)
    {
        var state = new ParseState();
        var commentStripper = new BmsCommentStripper();

        var strippedLines = lines
            .Select(line => line.TrimStart().StartsWith('%')
                ? line
                : commentStripper.ProcessLine(line))
            .OfType<string>();

        foreach (var line in MaterializeControlFlow(strippedLines, randomValueSelector, state.BranchDecisions))
            parseLine(line, state);

        var tickResolution = calculateTickResolution(state);
        var measures = calculateMeasures(state, tickResolution);
        var measureStarts = measures.ToDictionary(m => m.Index, m => m.StartTick);
        var timingEvents = collectTimingEvents(state, measureStarts, tickResolution);
        var stopEvents = collectStopEvents(state, measureStarts, timingEvents);
        timingEvents = applyStopOffsetsToTimingEvents(timingEvents, stopEvents);

        var timingMap = new BmsTimingMap(
            tickResolution,
            measures,
            timingEvents.Select(e => new BmsBpmEvent(e.Tick, e.Bpm, e.Time, e.Sequence)),
            stopEvents.Select(e => new BmsStopEvent(e.Tick, e.Duration, e.StopValue, e.Bpm, e.Sequence)),
            state.BaseBpm);

        var layoutVariant = BmsLayout.InferVariant(state.ChannelLines.Select(l => l.Channel), path);
        var totalColumns = BmsLayout.GetTotalColumns(layoutVariant);
        var sampleDefinitions = new Dictionary<string, string>(state.SampleDefinitions, StringComparer.OrdinalIgnoreCase);
        var hitObjects = collectHitObjects(state, totalColumns, measureStarts, timingMap)
            .OrderBy(h => h.StartTime)
            .ThenBy(h => h.Tick)
            .ThenBy(h => h.Column)
            .ToArray();
        var longNoteTailSampleEvents = collectLongNoteTailSampleEvents(hitObjects)
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Tick)
            .ToArray();
        var textEvents = collectTextEvents(state, measureStarts, timingMap);

        return new BmsParseResult(
            state.Title,
            state.Artist,
            state.Source,
            state.PlayLevel,
            state.Rank,
            state.Total,
            tickResolution,
            timingMap,
            layoutVariant,
            totalColumns,
            sampleDefinitions,
            collectBackgroundSampleEvents(state, measureStarts, timingMap).ToArray(),
            longNoteTailSampleEvents,
            hitObjects,
            state.BranchDecisions.ToArray(),
            textEvents,
            state.Subtitle,
            state.SubArtist,
            state.Maker,
            state.Url,
            state.Email,
            state.Comment);
    }

    private static void parseLine(string line, ParseState state)
    {
        line = line.Trim();

        if (line.Length == 0 || (line[0] != '#' && line[0] != '%'))
            return;

        // Normalize % prefix to # for regex matching (%URL, %EMAIL).
        if (line[0] == '%')
            line = "#" + line[1..];

        var channelMatch = channelLineRegex().Match(line);

        if (channelMatch.Success)
        {
            var measure = int.Parse(channelMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            var channel = channelMatch.Groups[2].Value.ToUpperInvariant();
            var payload = channelMatch.Groups[3].Value.Trim();

            state.MaxMeasure = Math.Max(state.MaxMeasure, measure);

            if (channel == "02")
            {
                if (tryParseDouble(payload, out var length) && length > 0)
                    state.MeasureLengths[measure] = length;
            }
            else if (payload.Length >= 2)
            {
                state.ChannelLines.Add(new RawChannelLine(measure, channel, payload, state.NextSequence++ * 4096));
            }

            return;
        }

        var commandMatch = commandLineRegex().Match(line);

        if (!commandMatch.Success)
            return;

        var command = commandMatch.Groups[1].Value.ToUpperInvariant();
        var value = commandMatch.Groups[2].Value.Trim();

        switch (command)
        {
            case "TITLE":
                state.Title = value;
                break;

            case "ARTIST":
                state.Artist = value;
                break;

            case "GENRE":
            case "GENLE":
                state.Source = value;
                break;

            case "SUBTITLE":
                state.Subtitle = value;
                break;

            case "SUBARTIST":
                state.SubArtist = value;
                break;

            case "MAKER":
                state.Maker = value;
                break;

            case "URL":
                state.Url = value;
                break;

            case "EMAIL":
                state.Email = value;
                break;

            case "COMMENT":
                state.Comment = value;
                break;

            case "PLAYLEVEL":
                if (tryParseDouble(value, out var difficulty))
                    state.PlayLevel = (float)difficulty;
                break;

            case "BPM":
                if (tryParseDouble(value, out var bpm) && bpm > 0)
                    state.InitialBpm = bpm;
                break;

            case "BASEBPM":
                if (tryParseDouble(value, out var baseBpm) && baseBpm > 0)
                    state.BaseBpm = baseBpm;
                break;

            case "LNTYPE":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lnType))
                    state.LnType = lnType;
                break;

            case "RANK":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) && rank >= 0 && rank <= 4)
                    state.Rank = rank;
                break;

            case "TOTAL":
                if (tryParseDouble(value, out var total) && total > 0)
                    state.Total = total;
                break;

            case "LNOBJ":
                if (value.Length >= 2)
                    state.LnObjValues.Add(value[..2].ToUpperInvariant());
                break;
        }

        switch (command.Length)
        {
            case 5 when command.StartsWith("BPM", StringComparison.OrdinalIgnoreCase)
                        && tryParseDouble(value, out var extendedBpm)
                        && extendedBpm > 0:
                state.BpmDefinitions[command[3..5]] = extendedBpm;
                break;

            case 5 when command.StartsWith("WAV", StringComparison.OrdinalIgnoreCase)
                        && value.Length > 0:
                state.SampleDefinitions[command[3..5]] = value.Trim().Trim('"');
                break;

            case 6 when command.StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
                        && tryParseDouble(value, out var stopValue)
                        && stopValue > 0:
                state.StopDefinitions[command[4..6]] = stopValue;
                break;

            case 6 when command.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase)
                        && value.Length > 0:
                state.TextDefinitions[command[4..6]] = value;
                break;

            case 6 when command.StartsWith("SONG", StringComparison.OrdinalIgnoreCase)
                        && value.Length > 0:
            {
                var key = command[4..6];

                state.TextDefinitions.TryAdd(key, value);

                break;
            }
        }
    }

    private static IEnumerable<BmsSampleEvent> collectBackgroundSampleEvents(
        ParseState state, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap)
    {
        return from line in state.ChannelLines.Where(l => l.Channel == "01")
               from cell in expandCells(line, measureStarts, false)
               select new BmsSampleEvent(timingMap.ProjectTickToTime(cell.Tick), cell.Tick, cell.Value);
    }

    /// <summary>
    /// ordered
    /// </summary>
    /// <param name="state"></param>
    /// <param name="measureStarts"></param>
    /// <param name="timingMap"></param>
    /// <returns></returns>
    private static BmsTextEvents collectTextEvents(
        ParseState state, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap)
    {
        var events = state.ChannelLines
            .Where(l => l.Channel == "99")
            .SelectMany(line => expandCells(line, measureStarts, false))
            .Where(cell => state.TextDefinitions.ContainsKey(cell.Value))
            .Select(cell => new BmsTextEvent(
                timingMap.ProjectTickToTime(cell.Tick),
                cell.Tick,
                state.TextDefinitions[cell.Value]))
            .OrderBy(e => e.Time);
        state.TextDefinitions.TryGetValue("00", out var mistake);
        return new BmsTextEvents(mistake, events.ToArray());
    }

    private static IEnumerable<BmsSampleEvent> collectLongNoteTailSampleEvents(IEnumerable<BmsParsedHitObject> hitObjects)
    {
        foreach (var hitObject in hitObjects)
        {
            if (!hitObject.IsLongNote || string.IsNullOrWhiteSpace(hitObject.SampleKey) || hitObject.EndTick <= hitObject.Tick)
                continue;

            yield return new BmsSampleEvent(hitObject.StartTime + hitObject.Duration, hitObject.EndTick, hitObject.SampleKey);
        }
    }

    private static int calculateTickResolution(ParseState state)
    {
        var resolution = state.ChannelLines
            .Select(line => line.Payload.Length / 2)
            .Where(pairCount => pairCount > 0)
            .Aggregate(base_tick_resolution, lcmChecked);

        return state.MeasureLengths.Values
            .Aggregate(resolution, (current, length) => lcmChecked(current, denominatorFor(length)));
    }

    private static List<BmsMeasureInfo> calculateMeasures(ParseState state, int tickResolution)
    {
        var result = new List<BmsMeasureInfo>();
        long currentTick = 0;
        var finalMeasure = Math.Max(state.MaxMeasure + 1, 1);

        for (var measure = 0; measure <= finalMeasure; measure++)
        {
            var length = state.MeasureLengths.GetValueOrDefault(measure, 1);
            var lengthTicks = checked((long)Math.Round(tickResolution * length));

            result.Add(new BmsMeasureInfo(measure, currentTick, lengthTicks, length));
            currentTick += lengthTicks;
        }

        return result;
    }

    private static List<TimingEvent> collectTimingEvents(ParseState state, IReadOnlyDictionary<int, long> measureStarts, int tickResolution)
    {
        var events = new List<TimingEvent>
        {
            new(0, Math.Max(state.InitialBpm, 1), 0),
        };

        foreach (var line in state.ChannelLines)
        {
            if (line.Channel is not ("03" or "08"))
                continue;

            foreach (var cell in expandCells(line, measureStarts, false))
            {
                var bpm = line.Channel == "03" ? parseHexBpm(cell.Value) : state.BpmDefinitions.GetValueOrDefault(cell.Value);

                if (bpm is > 0)
                    events.Add(new TimingEvent(cell.Tick, bpm.Value, 0, cell.Sequence));
            }
        }

        var ordered = events.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList();
        double time = 0;
        var previousTick = ordered[0].Tick;
        var previousBpm = ordered[0].Bpm;

        for (var i = 0; i < ordered.Count; i++)
        {
            var timingEvent = ordered[i];

            if (i > 0)
                time += ticksToMilliseconds(timingEvent.Tick - previousTick, previousBpm, tickResolution);

            ordered[i] = timingEvent with { Time = time };
            previousTick = timingEvent.Tick;
            previousBpm = timingEvent.Bpm;
        }

        return ordered;
    }

    private static List<StopEvent> collectStopEvents(ParseState state, IReadOnlyDictionary<int, long> measureStarts, List<TimingEvent> timingEvents)
    {
        var events = new List<StopEvent>();

        foreach (var cell in state.ChannelLines
                     .Where(line => line.Channel == "09")
                     .SelectMany(line => expandCells(line, measureStarts, false)))
        {
            if (!state.StopDefinitions.TryGetValue(cell.Value, out var stopValue) || stopValue <= 0)
                continue;

            var bpm = bpmAtTick(cell.Tick, timingEvents);
            var duration = stopValue * 60000 / (bpm * 48);

            events.Add(new StopEvent(cell.Tick, duration, stopValue, bpm, cell.Sequence));
        }

        return events.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList();
    }

    private static List<TimingEvent> applyStopOffsetsToTimingEvents(List<TimingEvent> timingEvents, IReadOnlyList<StopEvent> stopEvents)
    {
        if (stopEvents.Count == 0)
            return timingEvents;

        return timingEvents.Select(e => e with { Time = e.Time + stopEvents.Where(s => s.Tick < e.Tick).Sum(s => s.Duration) }).ToList();
    }

    private static IEnumerable<BmsParsedHitObject> collectHitObjects(
        ParseState state, int totalColumns, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap)
    {
        var notes = new List<RawCell>();
        var lnCells = new List<RawCell>();
        var mines = new List<RawCell>();

        foreach (var line in state.ChannelLines)
        {
            if (BmsLayout.TryMapVisibleChannel(line.Channel, totalColumns, out var column))
            {
                foreach (var cell in expandCells(line, measureStarts, false))
                    notes.Add(cell with { Column = column });
                continue;
            }

            if (tryMapLongNoteChannel(line.Channel, totalColumns, out column))
            {
                var includeZeroCells = state.LnType == 2;
                foreach (var cell in expandCells(line, measureStarts, includeZeroCells))
                    lnCells.Add(cell with { Column = column });
                continue;
            }

            if (tryMapLandmineChannel(line.Channel, totalColumns, out column))
            {
                foreach (var cell in expandCells(line, measureStarts, false))
                    mines.Add(cell with { Column = column });
            }
        }

        foreach (var hitObject in state.LnType == 2
                     ? collectLnType2Objects(lnCells, timingMap, state.SampleDefinitions)
                     : collectLnType1Objects(lnCells, timingMap, state.SampleDefinitions))
        {
            yield return hitObject;
        }

        foreach (var hitObject in collectVisibleObjects(notes, state, timingMap))
            yield return hitObject;

        foreach (var mine in mines.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
            yield return createMineHitObject(mine, timingMap, state.SampleDefinitions);
    }

    private static IEnumerable<BmsParsedHitObject> collectVisibleObjects(
        IEnumerable<RawCell> notes, ParseState state, BmsTimingMap timingMap)
    {
        if (state.LnObjValues.Count == 0)
        {
            foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
                yield return createHitObject(note, note.Tick, false, timingMap, state.SampleDefinitions);

            yield break;
        }

        var pendingByColumn = new Dictionary<int, RawCell>();

        foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
        {
            if (state.LnObjValues.Contains(note.Value))
            {
                if (pendingByColumn.Remove(note.Column, out var start) && note.Tick > start.Tick)
                    yield return createHitObject(start, note.Tick, true, timingMap, state.SampleDefinitions);

                continue;
            }

            if (pendingByColumn.TryGetValue(note.Column, out var previous))
                yield return createHitObject(previous, previous.Tick, false, timingMap, state.SampleDefinitions);

            pendingByColumn[note.Column] = note;
        }

        foreach (var pending in pendingByColumn.Values.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
            yield return createHitObject(pending, pending.Tick, false, timingMap, state.SampleDefinitions);
    }

    private static IEnumerable<BmsParsedHitObject> collectLnType1Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap, IReadOnlyDictionary<string, string> sampleDefinitions)
    {
        var openByColumn = new Dictionary<int, RawCell>();

        foreach (var cell in lnCells.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
        {
            if (openByColumn.Remove(cell.Column, out var start))
            {
                if (cell.Tick > start.Tick)
                    yield return createHitObject(start, cell.Tick, true, timingMap, sampleDefinitions);
            }
            else
            {
                openByColumn[cell.Column] = cell;
            }
        }
    }

    private static IEnumerable<BmsParsedHitObject> collectLnType2Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap, IReadOnlyDictionary<string, string> sampleDefinitions)
    {
        foreach (var channelGroup in lnCells.GroupBy(c => c.Channel))
        {
            RawCell? openRun = null;

            foreach (var cell in channelGroup.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
            {
                if (cell.Value != "00")
                {
                    openRun ??= cell;
                    continue;
                }

                if (openRun is { } start && cell.Tick > start.Tick)
                {
                    yield return createHitObject(start, cell.Tick, true, timingMap, sampleDefinitions);

                    openRun = null;
                }
            }
        }
    }

    private static BmsParsedHitObject createHitObject(
        RawCell start, long endTick, bool isLongNote, BmsTimingMap timingMap,
        IReadOnlyDictionary<string, string> sampleDefinitions)
    {
        var startTime = timingMap.ProjectTickToTime(start.Tick);
        var endTime = timingMap.ProjectTickToTime(endTick);

        return new BmsParsedHitObject(
            start.Tick,
            endTick,
            startTime,
            Math.Max(0, endTime - startTime),
            start.Column,
            start.Channel,
            start.Value,
            sampleDefinitions.GetValueOrDefault(start.Value, string.Empty),
            isLongNote,
            false,
            0,
            string.Empty);
    }

    private static BmsParsedHitObject createMineHitObject(
        RawCell mine, BmsTimingMap timingMap, IReadOnlyDictionary<string, string> sampleDefinitions)
    {
        var startTime = timingMap.ProjectTickToTime(mine.Tick);

        return new BmsParsedHitObject(
            mine.Tick,
            mine.Tick,
            startTime,
            0,
            mine.Column,
            mine.Channel,
            mine.Value,
            string.Empty,
            false,
            true,
            parseBase36(mine.Value) / 2d,
            sampleDefinitions.GetValueOrDefault("00", string.Empty));
    }

    private static IEnumerable<RawCell> expandCells(
        RawChannelLine line, IReadOnlyDictionary<int, long> measureStarts, bool includeZeroCells)
    {
        var pairCount = line.Payload.Length / 2;

        if (pairCount == 0)
            yield break;

        var measureStart = measureStarts[line.Measure];
        var measureLength = measureStarts[line.Measure + 1] - measureStart;

        for (var i = 0; i < pairCount; i++)
        {
            var value = line.Payload.Substring(i * 2, 2);

            if (!includeZeroCells && value == "00")
                continue;

            yield return new RawCell(measureStart + measureLength * i / pairCount, line.Channel, value, line.Sequence + i, -1);
        }
    }

    private static bool tryMapLongNoteChannel(string channel, int totalColumns, out int column)
    {
        if (channel.Length != 2 || channel[0] is not ('5' or '6'))
        {
            column = -1;
            return false;
        }

        var visibleChannel = channel[0] == '5' ? $"1{channel[1]}" : $"2{channel[1]}";
        return BmsLayout.TryMapVisibleChannel(visibleChannel, totalColumns, out column);
    }

    private static bool tryMapLandmineChannel(string channel, int totalColumns, out int column)
    {
        if (channel.Length != 2 || channel[0] is not ('D' or 'E'))
        {
            column = -1;
            return false;
        }

        var visibleChannel = channel[0] == 'D' ? $"1{channel[1]}" : $"2{channel[1]}";
        return BmsLayout.TryMapVisibleChannel(visibleChannel, totalColumns, out column);
    }

    private static double bpmAtTick(long tick, IReadOnlyList<TimingEvent> timingEvents)
    {
        var lo = 0;
        var hi = timingEvents.Count - 1;

        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;

            if (timingEvents[mid].Tick <= tick)
                lo = mid;
            else
                hi = mid - 1;
        }

        return timingEvents[lo].Bpm;
    }

    private static double ticksToMilliseconds(long ticks, double bpm, int tickResolution) =>
        ticks * (60000 / bpm) / (tickResolution / 4d);

    private static double? parseHexBpm(string value) =>
        int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bpm) ? bpm : null;

    private static int parseBase36(string value)
    {
        var result = 0;

        foreach (var c in value.ToUpperInvariant())
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => 0,
            };

            result = result * 36 + digit;
        }

        return result;
    }

    private static bool tryParseDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static int denominatorFor(double value)
    {
        const double tolerance = 0.0000001;

        for (var denominator = 1; denominator <= 1024; denominator++)
        {
            var numerator = Math.Round(value * denominator);

            if (Math.Abs(value - numerator / denominator) < tolerance)
                return denominator;
        }

        return 1;
    }

    private static int lcmChecked(int a, int b)
    {
        if (b == 0)
            return a;

        var result = (long)a / gcd(a, b) * b;
        return result > 12288 ? a : (int)result;
    }

    private static int gcd(int a, int b)
    {
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }

        return Math.Abs(a);
    }

    [GeneratedRegex(@"^#(\d{3})([0-9A-Z]{2}):(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex channelLineRegex();

    [GeneratedRegex(@"^#([A-Z0-9]+)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex commandLineRegex();

    private sealed class ParseState
    {
        public Dictionary<int, double> MeasureLengths { get; } = new();

        public Dictionary<string, double> BpmDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> StopDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> SampleDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> TextDefinitions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> LnObjValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<RawChannelLine> ChannelLines { get; } = [];

        public List<BmsBranchDecision> BranchDecisions { get; } = [];

        public string? Title { get; set; }

        public string? Artist { get; set; }

        public string? Source { get; set; }

        public string? Subtitle { get; set; }

        public string? SubArtist { get; set; }

        public string? Maker { get; set; }

        public string? Url { get; set; }

        public string? Email { get; set; }

        public string? Comment { get; set; }

        public float? PlayLevel { get; set; }

        public double InitialBpm { get; set; } = 130;

        /// <summary>#BASEBPM — visual BPM override for scroll speed. Default 0 = not set.</summary>
        public double BaseBpm { get; set; }

        public int LnType { get; set; } = 1;

        // Default RANK 2 = NORMAL per BMS spec.
        public int Rank { get; set; } = 2;

        /// <summary>BMS #TOTAL value: gauge recovery coefficient. Zero means use the default formula.</summary>
        public double Total { get; set; }

        public int MaxMeasure { get; set; }

        public int NextSequence { get; set; }
    }
}
