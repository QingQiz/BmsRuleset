using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private const int base_tick_resolution = 192;

    // ── Base-62 6-bit encoding ───────────────────────────────────────────

    /// <summary>256-entry lookup: ASCII char → 6-bit value (0–61), 255 = invalid.</summary>
    private static readonly byte[] s_char_to6 = buildCharTo6Table();


    // ── Internal types ───────────────────────────────────────────────────

    /// <summary>
    /// A parsed BMS channel line. Stores a reference to the original line string plus the payload's
    /// start offset and length — zero allocation (no Substring or
    /// <see cref="System.ReadOnlySpan{T}.ToString"/>).
    /// </summary>
    private readonly record struct RawChannelLine(int Measure, ushort Channel, string Line, int PayloadStart, int PayloadLength, int Sequence);

    private readonly record struct RawCell(long Tick, ushort Channel, ushort Value, int Sequence, int Column);

    private readonly record struct TimingEvent(long Tick, double Bpm, double Time, int Sequence = 0);

    private readonly record struct StopEvent(long Tick, double Duration, double StopValue, double Bpm, int Sequence);

    /// <summary>Extract the high 6-bit digit (the "tens" place).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hi(ushort k) => k >> 6;

    /// <summary>Extract the low 6-bit digit (the "ones" place).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Lo(ushort k) => k & 0x3F;

    /// <summary>Pack hi/lo 6-bit digits into a 12-bit ushort (hi &lt;&lt; 6 | lo).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort Pack(int hi, int lo) => (ushort)((hi << 6) | lo);

    public static BmsParseResult Parse(IEnumerable<string> lines, string? path = null, Func<int, int>? randomValueSelector = null)
    {
        var state = new ParseState();
        var commentStripper = new BmsCommentStripper();

        // Merged pipeline: comment stripping → OfType filter → control-flow resolution → parseLine.
        // A single foreach loop replaces four iterator layers (Select, OfType, MaterializeControlFlow,
        // and the consuming foreach), eliminating per-element state-machine dispatch overhead.
        randomValueSelector ??= selectRandomValue;
        var frames = new List<ControlFrame>();

        foreach (var rawLine in lines)
        {
            // Comment stripping + OfType filter (inline).
            string? line;
            if (rawLine.Length > 0 && rawLine[0] == '%')
                line = rawLine;
            else
                line = commentStripper.ProcessLine(rawLine);

            if (line == null) continue;

            // Control-flow resolution (inline MaterializeControlFlow).
            if (tryReadControlCommand(line, out var command, out var value))
            {
                applyControlCommand(command, value, frames, randomValueSelector, state.BranchDecisions);
                continue;
            }

            if (isActive(frames))
                parseLine(line, state);
        }

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
        var sampleDefinitions = new Dictionary<ushort, string>(state.SampleDefinitions);

        // Pre-size output lists to avoid AddWithResize during collection.
        var hitObjects = new List<BmsParsedHitObject>(state.ChannelLines.Count);
        collectHitObjects(state, totalColumns, measureStarts, timingMap, hitObjects);
        hitObjects.Sort(default(HitObjectComparer));

        var longNoteTailSampleEvents = new List<BmsSampleEvent>(hitObjects.Count / 4);
        collectLongNoteTailSampleEvents(hitObjects, longNoteTailSampleEvents);
        longNoteTailSampleEvents.Sort(default(SampleEventComparer));

        var textEvents = collectTextEvents(state, measureStarts, timingMap);

        var bgSampleEvents = new List<BmsSampleEvent>(state.ChannelLines.Count / 10);
        collectBackgroundSampleEvents(state, measureStarts, timingMap, bgSampleEvents);

        return new BmsParseResult(
            state.Title,
            state.Artist,
            state.PlayLevel,
            state.Rank,
            state.Total,
            tickResolution,
            timingMap,
            layoutVariant,
            totalColumns,
            sampleDefinitions,
            bgSampleEvents.ToArray(),
            longNoteTailSampleEvents,
            hitObjects,
            state.BranchDecisions.ToArray(),
            textEvents,
            state.Genre,
            state.Subtitle,
            state.SubArtist,
            state.Maker,
            state.Url,
            state.Email,
            state.Comment);
    }

    /// <summary>Encode a 2-char base-62 pair into a 12-bit ushort (case-sensitive).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort EncodePair(char hi, char lo) =>
        Pack(charTo6(hi), charTo6(lo));

    /// <summary>Encode with case-folding to uppercase (for sample keys, cell values — BMS is case-insensitive for these).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ushort EncodePairCi(char hi, char lo) =>
        Pack(charTo6(toUpperFast(hi)), charTo6(toUpperFast(lo)));

    /// <summary>Encode a 2-char string, case-insensitive (traditional BMS default).</summary>
    internal static ushort Enc(string s) => s.Length == 2
        ? EncodePairCi(s[0], s[1])
        : (ushort)0;

    private static byte[] buildCharTo6Table()
    {
        var t = new byte[256];
        Array.Fill(t, (byte)255);
        for (int i = '0'; i <= '9'; i++) t[i] = (byte)(i - '0');      // 0–9
        for (int i = 'A'; i <= 'Z'; i++) t[i] = (byte)(i - 'A' + 10); // 10–35
        for (int i = 'a'; i <= 'z'; i++) t[i] = (byte)(i - 'a' + 36); // 36–61
        return t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int charTo6(char c) => s_char_to6[c];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static char toUpperFast(char c) => c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

    /// <summary>Encode a cell value or sample key, respecting #BASE 62 case-sensitivity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort encodeValue(bool useBase62, char hi, char lo) =>
        useBase62 ? EncodePair(hi, lo) : EncodePairCi(hi, lo);

    private static void parseLine(string line, ParseState state)
    {
        // Skip leading whitespace via direct indexing (avoids span allocation for this common path).
        var start = 0;
        while (start < line.Length && (line[start] == ' ' || line[start] == '\t')) start++;
        if (start >= line.Length) return;

        var firstChar = line[start];
        if (firstChar != '#' && firstChar != '%') return;

        // Use span from `start` for the remainder — it covers the trimmed portion of the line.
        var span = line.AsSpan(start);

        // Handle % prefix commands (%URL, %EMAIL) — only two exist, handle inline.
        if (firstChar == '%')
        {
            if (span.Length == 1) return;

            var rest = span[1..];
            if (rest.StartsWith("URL", StringComparison.OrdinalIgnoreCase) && rest.Length > 3 && (rest[3] == ' ' || rest[3] == '\t'))
            {
                state.Url = rest[4..].Trim().ToString();
                return;
            }

            if (rest.StartsWith("EMAIL", StringComparison.OrdinalIgnoreCase) && rest.Length > 5 && (rest[5] == ' ' || rest[5] == '\t'))
            {
                state.Email = rest[6..].Trim().ToString();
                // ReSharper disable once RedundantJumpStatement
                return;
            }

            return;
        }

        // Attempt channel line: #XXXYY:...
        // Format: # followed by 3 digits (measure), 2 chars (channel), ':', then payload.
        // Use direct indexing on the original line to avoid ReadOnlySpan<char>.ToString().
        if (span.Length >= 7)
        {
            var d1 = span[1];
            var d2 = span[2];
            var d3 = span[3];
            if (d1 >= '0' && d1 <= '9' && d2 >= '0' && d2 <= '9' && d3 >= '0' && d3 <= '9' && span[6] == ':')
            {
                var measure = (d1 - '0') * 100 + (d2 - '0') * 10 + (d3 - '0');
                state.MaxMeasure = Math.Max(state.MaxMeasure, measure);

                // Payload starts at start+7 in the original line.
                // Trim trailing whitespace by walking backward from line end.
                var plStart = start + 7;
                var plEnd = line.Length;
                while (plEnd > plStart && (line[plEnd - 1] == ' ' || line[plEnd - 1] == '\t')) plEnd--;
                var plLen = plEnd - plStart;

                if (span[4] == '0' && span[5] == '2')
                {
                    if (plLen > 0 && tryParseDouble(line.AsSpan(plStart, plLen), out var length) && length > 0)
                        state.MeasureLengths[measure] = length;
                }
                else if (plLen >= 2)
                {
                    // Channel IDs are hex (0-9, A-F) and case-insensitive per BMS spec.
                    // #BASE 62 does NOT affect channel encoding — only definitions and cell values.
                    var channelKey = EncodePairCi(span[4], span[5]);
                    // Store original line ref + payload offset — zero allocation.
                    state.ChannelLines.Add(new RawChannelLine(measure, channelKey, line, plStart, plLen, state.NextSequence++ * 4096));
                }

                return;
            }
        }

        // Attempt command line: #KEYWORD value
        var cmdStart = 1;
        while (cmdStart < span.Length && span[cmdStart] == ' ') cmdStart++;
        if (cmdStart >= span.Length) return;

        // Find end of command (first whitespace)
        var cmdEnd = cmdStart;
        while (cmdEnd < span.Length && span[cmdEnd] != ' ' && span[cmdEnd] != '\t') cmdEnd++;

        // Require at least one whitespace after command
        if (cmdEnd >= span.Length) return;

        var cmdSpan = span[cmdStart..cmdEnd];
        var valueSpan = span[(cmdEnd + 1)..].Trim();

        // Match commands using span comparisons (zero-alloc).
        if (cmdSpan.Equals("TITLE", StringComparison.OrdinalIgnoreCase))
        {
            state.Title = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("ARTIST", StringComparison.OrdinalIgnoreCase))
        {
            state.Artist = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("GENRE", StringComparison.OrdinalIgnoreCase)
            || cmdSpan.Equals("GENLE", StringComparison.OrdinalIgnoreCase))
        {
            state.Genre = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("SUBTITLE", StringComparison.OrdinalIgnoreCase))
        {
            state.Subtitle = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("SUBARTIST", StringComparison.OrdinalIgnoreCase))
        {
            state.SubArtist = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("MAKER", StringComparison.OrdinalIgnoreCase))
        {
            state.Maker = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("URL", StringComparison.OrdinalIgnoreCase))
        {
            state.Url = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("EMAIL", StringComparison.OrdinalIgnoreCase))
        {
            state.Email = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("COMMENT", StringComparison.OrdinalIgnoreCase))
        {
            state.Comment = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Equals("PLAYLEVEL", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var difficulty))
                state.PlayLevel = (float)difficulty;
            return;
        }

        if (cmdSpan.Equals("BPM", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var bpm) && bpm > 0)
                state.InitialBpm = bpm;
            return;
        }

        if (cmdSpan.Equals("BASEBPM", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var baseBpm) && baseBpm > 0)
                state.BaseBpm = baseBpm;
            return;
        }

        if (cmdSpan.Equals("LNTYPE", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lnType))
                state.LnType = lnType;
            return;
        }

        if (cmdSpan.Equals("RANK", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) && rank >= 0 && rank <= 4)
                state.Rank = rank;
            return;
        }

        if (cmdSpan.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var total) && total > 0)
                state.Total = total;
            return;
        }

        if (cmdSpan.Equals("BASE", StringComparison.OrdinalIgnoreCase))
        {
            if (valueSpan.Length >= 2 && valueSpan[..2].Equals("62", StringComparison.Ordinal))
                state.UseBase62 = true;
            return;
        }

        if (cmdSpan.Equals("LNOBJ", StringComparison.OrdinalIgnoreCase))
        {
            if (valueSpan.Length >= 2)
                state.LnObjValues.Add(encodeValue(state.UseBase62, valueSpan[0], valueSpan[1]));
            return;
        }

        // Definition commands: #BPMxx, #WAVxx, #STOPxx, #TEXTxx, #SONGxx
        if (cmdSpan.Length == 5 && cmdSpan.StartsWith("BPM", StringComparison.OrdinalIgnoreCase)
                                && tryParseDouble(valueSpan, out var extendedBpm) && extendedBpm > 0)
        {
            state.BpmDefinitions[encodeValue(state.UseBase62, cmdSpan[3], cmdSpan[4])] = extendedBpm;
            return;
        }

        if (cmdSpan.Length == 5 && cmdSpan.StartsWith("WAV", StringComparison.OrdinalIgnoreCase)
                                && valueSpan.Length > 0)
        {
            state.SampleDefinitions[encodeValue(state.UseBase62, cmdSpan[3], cmdSpan[4])] = valueSpan.Trim('"').ToString();
            return;
        }

        if (cmdSpan.Length == 6 && cmdSpan.StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
                                && tryParseDouble(valueSpan, out var stopValue) && stopValue > 0)
        {
            state.StopDefinitions[encodeValue(state.UseBase62, cmdSpan[4], cmdSpan[5])] = stopValue;
            return;
        }

        if (cmdSpan.Length == 6 && cmdSpan.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase)
                                && valueSpan.Length > 0)
        {
            state.TextDefinitions[encodeValue(state.UseBase62, cmdSpan[4], cmdSpan[5])] = valueSpan.ToString();
            return;
        }

        if (cmdSpan.Length == 6 && cmdSpan.StartsWith("SONG", StringComparison.OrdinalIgnoreCase)
                                && valueSpan.Length > 0)
        {
            state.TextDefinitions.TryAdd(encodeValue(state.UseBase62, cmdSpan[4], cmdSpan[5]), valueSpan.ToString());
        }
    }

    private static void collectBackgroundSampleEvents(
        ParseState state, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap,
        List<BmsSampleEvent> output)
    {
        foreach (var line in state.ChannelLines)
        {
            if (line.Channel != CH_01) continue;

            var pairCount = line.PayloadLength / 2;
            if (pairCount == 0) continue;

            var mStart = measureStarts[line.Measure];
            var mLength = measureStarts[line.Measure + 1] - mStart;
            var payload = line.Line.AsSpan(line.PayloadStart, line.PayloadLength);
            var useBase62 = state.UseBase62;

            for (var i = 0; i < pairCount; i++)
            {
                var offset = i * 2;
                var value = encodeValue(useBase62, payload[offset], payload[offset + 1]);
                if (value == 0) continue; // 0 = "00"

                var tick = mStart + mLength * i / pairCount;
                output.Add(new BmsSampleEvent(timingMap.ProjectTickToTime(tick), tick, value));
            }
        }
    }

    private static BmsTextEvents collectTextEvents(
        ParseState state, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap)
    {
        var events = new List<BmsTextEvent>();
        foreach (var line in state.ChannelLines)
        {
            if (line.Channel != CH_99) continue;

            foreach (var cell in expandCells(line, measureStarts, false, state.UseBase62))
            {
                if (state.TextDefinitions.TryGetValue(cell.Value, out var text))
                    events.Add(new BmsTextEvent(timingMap.ProjectTickToTime(cell.Tick), cell.Tick, text));
            }
        }

        events.Sort((a, b) => a.Time.CompareTo(b.Time));
        state.TextDefinitions.TryGetValue(0, out var mistake); // 0 = "00"
        return new BmsTextEvents(mistake, events.ToArray());
    }

    private static void collectLongNoteTailSampleEvents(IEnumerable<BmsParsedHitObject> hitObjects, List<BmsSampleEvent> output)
    {
        foreach (var hitObject in hitObjects)
        {
            if (!hitObject.IsLongNote || hitObject.EndTick <= hitObject.Tick)
                continue;

            // Only emit a tail sample event if the tail has its own sample key AND the
            // sample path resolves (i.e. the key has a #WAV definition in the chart).
            // Do NOT fallback to the head's SampleKey — if the terminating cell has
            // no sample defined, the tail simply has no sound.
            if (hitObject.TailSampleKey == 0 || string.IsNullOrWhiteSpace(hitObject.TailSamplePath))
                continue;

            output.Add(new BmsSampleEvent(hitObject.StartTime + hitObject.Duration, hitObject.EndTick, hitObject.TailSampleKey));
        }
    }

    private static int calculateTickResolution(ParseState state)
    {
        var resolution = base_tick_resolution;
        foreach (var line in state.ChannelLines)
        {
            var pairCount = line.PayloadLength / 2;
            if (pairCount > 0)
                resolution = lcmChecked(resolution, pairCount);
        }

        foreach (var length in state.MeasureLengths.Values)
            resolution = lcmChecked(resolution, denominatorFor(length));

        return resolution;
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
            if (line.Channel != CH_03 && line.Channel != CH_08)
                continue;

            var pairCount = line.PayloadLength / 2;
            if (pairCount == 0) continue;

            var mStart = measureStarts[line.Measure];
            var mLength = measureStarts[line.Measure + 1] - mStart;
            var payload = line.Line.AsSpan(line.PayloadStart, line.PayloadLength);
            var isHexChannel = line.Channel == CH_03;

            for (var i = 0; i < pairCount; i++)
            {
                var offset = i * 2;
                // Channel 03 is always hex — unaffected by #BASE 62 (which is case-sensitive).
                // Hex uses 0-9/A-F/a-f; we always fold case via EncodePairCi so "1a" and "1A"
                // both decode to BPM 26, regardless of the #BASE 62 setting.
                var value = isHexChannel
                    ? EncodePairCi(payload[offset], payload[offset + 1])
                    : encodeValue(state.UseBase62, payload[offset], payload[offset + 1]);
                if (value == 0) continue; // 0 = "00"

                var tick = mStart + mLength * i / pairCount;
                var bpm = isHexChannel ? parseHexBpm(value) : state.BpmDefinitions.GetValueOrDefault(value);

                if (bpm is > 0)
                    events.Add(new TimingEvent(tick, bpm.Value, 0, line.Sequence + i));
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

        foreach (var line in state.ChannelLines)
        {
            if (line.Channel != CH_09) continue;

            var pairCount = line.PayloadLength / 2;
            if (pairCount == 0) continue;

            var mStart = measureStarts[line.Measure];
            var mLength = measureStarts[line.Measure + 1] - mStart;
            var payload = line.Line.AsSpan(line.PayloadStart, line.PayloadLength);
            var useBase62 = state.UseBase62;

            for (var i = 0; i < pairCount; i++)
            {
                var offset = i * 2;
                var value = encodeValue(useBase62, payload[offset], payload[offset + 1]);
                if (value == 0) continue; // 0 = "00"

                if (!state.StopDefinitions.TryGetValue(value, out var stopValue) || stopValue <= 0)
                    continue;

                var tick = mStart + mLength * i / pairCount;
                var bpm = bpmAtTick(tick, timingEvents);
                var duration = stopValue * 60000 / (bpm * 48);

                events.Add(new StopEvent(tick, duration, stopValue, bpm, line.Sequence + i));
            }
        }

        return events.OrderBy(e => e.Tick).ThenBy(e => e.Sequence).ToList();
    }

    private static List<TimingEvent> applyStopOffsetsToTimingEvents(List<TimingEvent> timingEvents, IReadOnlyList<StopEvent> stopEvents)
    {
        if (stopEvents.Count == 0)
            return timingEvents;

        return timingEvents.Select(e => e with { Time = e.Time + stopEvents.Where(s => s.Tick < e.Tick).Sum(s => s.Duration) }).ToList();
    }

    private static void collectHitObjects(
        ParseState state, int totalColumns, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap,
        List<BmsParsedHitObject> output)
    {
        var notes = new List<RawCell>();
        var lnCells = new List<RawCell>();
        var mines = new List<RawCell>();

        foreach (var line in state.ChannelLines)
        {
            if (BmsLayout.TryMapVisibleChannel(line.Channel, totalColumns, out var column))
            {
                foreach (var cell in expandCells(line, measureStarts, false, state.UseBase62))
                    notes.Add(cell with { Column = column });
                continue;
            }

            if (tryMapLongNoteChannel(line.Channel, totalColumns, out column))
            {
                var includeZeroCells = state.LnType == 2;
                foreach (var cell in expandCells(line, measureStarts, includeZeroCells, state.UseBase62))
                    lnCells.Add(cell with { Column = column });
                continue;
            }

            if (tryMapLandmineChannel(line.Channel, totalColumns, out column))
            {
                // Mine damage channels (D*, E*) use 36-base values — unaffected by #BASE 62.
                foreach (var cell in expandCells(line, measureStarts, false, false))
                    mines.Add(cell with { Column = column });
            }
        }

        if (state.LnType == 2)
            collectLnType2Objects(lnCells, timingMap, state.SampleDefinitions, output);
        else
            collectLnType1Objects(lnCells, timingMap, state.SampleDefinitions, output);

        collectVisibleObjects(notes, state, timingMap, output);

        foreach (var mine in mines.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
            output.Add(createMineHitObject(mine, timingMap, state.SampleDefinitions));
    }

    private static void collectVisibleObjects(
        IEnumerable<RawCell> notes, ParseState state, BmsTimingMap timingMap,
        List<BmsParsedHitObject> output)
    {
        if (state.LnObjValues.Count == 0)
        {
            foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
                output.Add(createHitObject(note, note.Tick, false, timingMap, state.SampleDefinitions));

            return;
        }

        var pendingByColumn = new Dictionary<int, RawCell>();

        foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
        {
            if (state.LnObjValues.Contains(note.Value))
            {
                if (pendingByColumn.Remove(note.Column, out var start) && note.Tick > start.Tick)
                    output.Add(createHitObject(start, note.Tick, true, timingMap, state.SampleDefinitions, note.Value));

                continue;
            }

            if (pendingByColumn.TryGetValue(note.Column, out var previous))
                output.Add(createHitObject(previous, previous.Tick, false, timingMap, state.SampleDefinitions));

            pendingByColumn[note.Column] = note;
        }

        foreach (var pending in pendingByColumn.Values.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
            output.Add(createHitObject(pending, pending.Tick, false, timingMap, state.SampleDefinitions));
    }

    private static void collectLnType1Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap, IReadOnlyDictionary<ushort, string> sampleDefinitions,
        List<BmsParsedHitObject> output)
    {
        var openByColumn = new Dictionary<int, RawCell>();

        foreach (var cell in lnCells.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
        {
            if (openByColumn.Remove(cell.Column, out var start))
            {
                if (cell.Tick > start.Tick)
                    output.Add(createHitObject(start, cell.Tick, true, timingMap, sampleDefinitions, cell.Value));
            }
            else
            {
                openByColumn[cell.Column] = cell;
            }
        }
    }

    private static void collectLnType2Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap, IReadOnlyDictionary<ushort, string> sampleDefinitions,
        List<BmsParsedHitObject> output)
    {
        foreach (var channelGroup in lnCells.GroupBy(c => c.Channel))
        {
            RawCell? openRun = null;

            foreach (var cell in channelGroup.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
            {
                if (cell.Value != 0) // 0 = "00"
                {
                    openRun ??= cell;
                    continue;
                }

                if (openRun is { } start && cell.Tick > start.Tick)
                {
                    output.Add(createHitObject(start, cell.Tick, true, timingMap, sampleDefinitions, cell.Value));

                    openRun = null;
                }
            }
        }
    }

    private static BmsParsedHitObject createHitObject(
        RawCell start, long endTick, bool isLongNote, BmsTimingMap timingMap,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        ushort tailCellValue = 0)
    {
        var startTime = timingMap.ProjectTickToTime(start.Tick);
        var endTime = timingMap.ProjectTickToTime(endTick);

        // Resolve tail sample from the terminating cell's value.
        // 0 ("00") is a control value (no note), so treat it as "no tail sample".
        // Non-zero values that exist in sampleDefinitions will have a tail sample;
        // others will have an empty tail sample path (play nothing).
        //
        // For LNTYPE 1 the terminating cell has the same value as the head, which
        // would play the identical sample on release.  Skip the tail sample when
        // it matches the head's sample key to avoid the double-play.
        var tailSampleKey = tailCellValue != 0 && tailCellValue != start.Value
            ? tailCellValue
            : (ushort)0;
        var tailSamplePath = tailSampleKey != 0
            ? sampleDefinitions.GetValueOrDefault(tailSampleKey, string.Empty)
            : string.Empty;

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
            string.Empty,
            tailSampleKey,
            tailSamplePath);
    }

    private static BmsParsedHitObject createMineHitObject(
        RawCell mine, BmsTimingMap timingMap, IReadOnlyDictionary<ushort, string> sampleDefinitions)
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
            parseBase36Value(mine.Value) / 2d,
            sampleDefinitions.GetValueOrDefault((ushort)0, string.Empty), // 0 = "00"
            0,
            string.Empty);
    }

    private static IEnumerable<RawCell> expandCells(
        RawChannelLine line, IReadOnlyDictionary<int, long> measureStarts, bool includeZeroCells, bool useBase62)
    {
        var pairCount = line.PayloadLength / 2;

        if (pairCount == 0)
            yield break;

        var measureStart = measureStarts[line.Measure];
        var measureLength = measureStarts[line.Measure + 1] - measureStart;
        // Index directly into the original line string — avoids ReadOnlySpan<char>
        // which cannot cross yield boundaries.
        var lineStr = line.Line;
        var plStart = line.PayloadStart;

        for (var i = 0; i < pairCount; i++)
        {
            var offset = i * 2;
            var pos = plStart + offset;
            var value = encodeValue(useBase62, lineStr[pos], lineStr[pos + 1]);

            if (!includeZeroCells && value == 0) // 0 = "00"
                continue;

            yield return new RawCell(measureStart + measureLength * i / pairCount, line.Channel, value, line.Sequence + i, -1);
        }
    }

    private static bool tryMapLongNoteChannel(ushort channel, int totalColumns, out int column)
    {
        var hi = Hi(channel);
        if (hi is not (5 or 6))
        {
            column = -1;
            return false;
        }

        var visibleKey = Pack(hi == 5 ? 1 : 2, Lo(channel));
        return BmsLayout.TryMapVisibleChannel(visibleKey, totalColumns, out column);
    }

    private static bool tryMapLandmineChannel(ushort channel, int totalColumns, out int column)
    {
        var hi = Hi(channel);
        if (hi is not (13 or 14))
        {
            column = -1;
            return false;
        } // 13='D', 14='E'

        var visibleKey = Pack(hi == 13 ? 1 : 2, Lo(channel));
        return BmsLayout.TryMapVisibleChannel(visibleKey, totalColumns, out column);
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

    /// <summary>Decode a hex BPM value from a 2-char encoded cell value (channel 03).</summary>
    private static double? parseHexBpm(ushort value) =>
        Hi(value) * 16 + Lo(value) is var bpm and > 0 ? bpm : null;

    /// <summary>Decode a base-36 integer from an encoded cell value (mine damage).</summary>
    private static int parseBase36Value(ushort value) => Hi(value) * 36 + Lo(value);

    private static bool tryParseDouble(ReadOnlySpan<char> value, out double result) =>
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

    // Channel constants (base-62 encoded, uppercase = traditional BMS).
    // ReSharper disable InconsistentNaming
    // ReSharper disable ShiftExpressionZeroLeftOperand
    private const ushort CH_01 = (0 << 6) | 1;

    // ReSharper disable once UnusedMember.Local
    private const ushort CH_02 = (0 << 6) | 2;
    private const ushort CH_03 = (0 << 6) | 3;
    private const ushort CH_08 = (0 << 6) | 8;
    private const ushort CH_09 = (0 << 6) | 9;
    private const ushort CH_99 = (9 << 6) | 9;

    // ReSharper restore InconsistentNaming
    // ReSharper restore ShiftExpressionZeroLeftOperand

    private sealed class ParseState
    {
        public Dictionary<int, double> MeasureLengths { get; } = new();

        public Dictionary<ushort, double> BpmDefinitions { get; } = new();

        public Dictionary<ushort, double> StopDefinitions { get; } = new();

        public Dictionary<ushort, string> SampleDefinitions { get; } = new();

        public Dictionary<ushort, string> TextDefinitions { get; } = new();

        public HashSet<ushort> LnObjValues { get; } = new();

        public List<RawChannelLine> ChannelLines { get; } = [];

        public List<BmsBranchDecision> BranchDecisions { get; } = [];

        public string? Title { get; set; }

        public string? Artist { get; set; }

        public string? Genre { get; set; }

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

        /// <summary>#BASE 62 — when set, cell values and sample keys use case-sensitive base-62 encoding.</summary>
        public bool UseBase62 { get; set; }

        public int LnType { get; set; } = 1;

        // Default RANK 2 = NORMAL per BMS spec.
        public int Rank { get; set; } = 2;

        /// <summary>BMS #TOTAL value: gauge recovery coefficient. Zero means use the default formula.</summary>
        public double Total { get; set; }

        public int MaxMeasure { get; set; }

        public int NextSequence { get; set; }
    }

    // ── Struct comparers for List<T>.Sort — zero allocation, no virtual dispatch ──

    private struct HitObjectComparer : IComparer<BmsParsedHitObject>
    {
        public int Compare(BmsParsedHitObject a, BmsParsedHitObject b)
        {
            var cmp = a.StartTime.CompareTo(b.StartTime);
            if (cmp != 0) return cmp;

            cmp = a.Tick.CompareTo(b.Tick);
            if (cmp != 0) return cmp;

            return a.Column.CompareTo(b.Column);
        }
    }

    private struct SampleEventComparer : IComparer<BmsSampleEvent>
    {
        public int Compare(BmsSampleEvent a, BmsSampleEvent b)
        {
            var cmp = a.Time.CompareTo(b.Time);
            if (cmp != 0) return cmp;

            return a.Tick.CompareTo(b.Tick);
        }
    }
}
