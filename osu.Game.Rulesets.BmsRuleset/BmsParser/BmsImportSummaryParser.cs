using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    public static BmsImportSummary ParseImportSummary(
        IEnumerable<string> lines,
        string? path = null,
        Func<int, int>? randomValueSelector = null)
    {
        var state = new ParseState();
        var commentStripper = new BmsCommentStripper();
        randomValueSelector ??= selectRandomValue;
        var frames = new List<ControlFrame>();

        foreach (var rawLine in lines)
        {
            string? line;
            if (rawLine.Length > 0 && rawLine[0] == '%')
                line = rawLine;
            else
                line = commentStripper.ProcessLine(rawLine);

            if (line == null)
                continue;

            if (tryReadControlCommand(line, out var command, out var value))
            {
                applyControlCommand(command, value, frames, randomValueSelector, state.BranchDecisions);
                continue;
            }

            if (isActive(frames))
                parseImportLine(line, state);
        }

        var tickResolution = calculateTickResolution(state);
        var measures = calculateMeasures(state, tickResolution);
        var measureStarts = measures.ToDictionary(m => m.Index, m => m.StartTick);
        var timingEvents = collectTimingEvents(state, measureStarts, tickResolution);
        var stopEvents = collectStopEvents(state, measureStarts, timingEvents);
        timingEvents = applyStopOffsetsToTimingEvents(timingEvents, stopEvents);

        var layoutVariant = BmsLayout.InferVariant(state.ChannelLines.Select(l => l.Channel), path);
        var totalColumns = BmsLayout.GetTotalColumns(layoutVariant);
        var timingMap = new BmsTimingMap(tickResolution, measures, timingEvents, stopEvents);
        var hitObjects = collectImportHitObjects(state, totalColumns, measureStarts, timingMap);

        hitObjects.Sort(default(ImportHitObjectComparer));

        var noteTimings = new List<BmsNoteTiming>(hitObjects.Count);
        foreach (var hitObject in hitObjects)
        {
            if (!hitObject.IsMine)
                noteTimings.Add(new BmsNoteTiming(hitObject.Column, hitObject.StartTime, hitObject.IsLongNote ? hitObject.EndTime : hitObject.StartTime));
        }

        var length = hitObjects.Count == 0 ? 0 : hitObjects[^1].EndTime;
        if (length < 0)
            length = 0;

        return new BmsImportSummary(
            extractImportMetadata(state, totalColumns, path),
            computeImportBpm(timingMap.BpmEvents, length),
            length,
            hitObjects.Count,
            hitObjects.Count(h => h.IsLongNote),
            noteTimings);
    }

    private static void parseImportLine(string line, ParseState state)
    {
        var start = 0;
        while (start < line.Length && (line[start] == ' ' || line[start] == '\t')) start++;
        if (start >= line.Length) return;

        var firstChar = line[start];
        if (firstChar != '#' && firstChar != '%') return;

        var span = line.AsSpan(start);
        if (firstChar == '%')
        {
            if (span.Length == 1) return;

            var rest = span[1..];
            if (rest.StartsWith("URL", StringComparison.OrdinalIgnoreCase) && rest.Length > 3 && (rest[3] == ' ' || rest[3] == '\t'))
                state.Url = rest[4..].Trim().ToString();
            else if (rest.StartsWith("EMAIL", StringComparison.OrdinalIgnoreCase) && rest.Length > 5 && (rest[5] == ' ' || rest[5] == '\t'))
                state.Email = rest[6..].Trim().ToString();

            return;
        }

        if (span.Length >= 7)
        {
            var d1 = span[1];
            var d2 = span[2];
            var d3 = span[3];
            if (d1 >= '0' && d1 <= '9' && d2 >= '0' && d2 <= '9' && d3 >= '0' && d3 <= '9' && span[6] == ':')
            {
                var measure = (d1 - '0') * 100 + (d2 - '0') * 10 + (d3 - '0');
                var plStart = start + 7;
                var plEnd = line.Length;
                while (plEnd > plStart && (line[plEnd - 1] == ' ' || line[plEnd - 1] == '\t')) plEnd--;
                var plLen = plEnd - plStart;

                if (span[4] == '0' && span[5] == '2')
                {
                    if (plLen > 0 && tryParseDouble(line.AsSpan(plStart, plLen), out var length) && length > 0)
                    {
                        state.MeasureLengths[measure] = length;
                        state.MaxMeasure = Math.Max(state.MaxMeasure, measure);
                    }

                    return;
                }

                if (plLen >= 2)
                {
                    var channelKey = EncodePairCi(span[4], span[5]);
                    if (isImportChannel(channelKey))
                    {
                        state.ChannelLines.Add(new RawChannelLine(measure, channelKey, line, plStart, plLen, state.NextSequence++ * 4096));
                        state.MaxMeasure = Math.Max(state.MaxMeasure, measure);
                    }
                }

                return;
            }
        }

        var cmdStart = 1;
        while (cmdStart < span.Length && span[cmdStart] == ' ') cmdStart++;
        if (cmdStart >= span.Length) return;

        var cmdEnd = cmdStart;
        while (cmdEnd < span.Length && span[cmdEnd] != ' ' && span[cmdEnd] != '\t') cmdEnd++;
        if (cmdEnd >= span.Length) return;

        var cmdSpan = span[cmdStart..cmdEnd];
        var valueSpan = span[(cmdEnd + 1)..].Trim();

        if (cmdSpan.Equals("TITLE", StringComparison.OrdinalIgnoreCase))
            state.Title = valueSpan.ToString();
        else if (cmdSpan.Equals("ARTIST", StringComparison.OrdinalIgnoreCase))
            state.Artist = valueSpan.ToString();
        else if (cmdSpan.Equals("SUBTITLE", StringComparison.OrdinalIgnoreCase))
            state.Subtitle = valueSpan.ToString();
        else if (cmdSpan.Equals("PLAYLEVEL", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var difficulty))
                state.PlayLevel = (float)difficulty;
        }
        else if (cmdSpan.Equals("BPM", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var bpm) && bpm != 0)
                state.InitialBpm = bpm;
        }
        else if (cmdSpan.Equals("BASE", StringComparison.OrdinalIgnoreCase))
        {
            if (valueSpan.Length >= 2 && valueSpan[..2].Equals("62", StringComparison.Ordinal))
                state.UseBase62 = true;
        }
        else if (cmdSpan.Equals("LNTYPE", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lnType))
                state.LnType = lnType;
        }
        else if (cmdSpan.Equals("LNMODE", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lnMode) && lnMode >= 1 && lnMode <= 3)
                state.LnMode = (BmsLongNoteMode)lnMode;
        }
        else if (cmdSpan.Equals("RANK", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(valueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) && rank >= 0 && rank <= 4)
                state.Rank = rank;
        }
        else if (cmdSpan.Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
        {
            if (tryParseDouble(valueSpan, out var total) && total > 0)
                state.Total = total;
        }
        else if (cmdSpan.Equals("LNOBJ", StringComparison.OrdinalIgnoreCase))
        {
            if (valueSpan.Length >= 2)
                state.LnObjValues.Add(encodeValue(state.UseBase62, valueSpan[0], valueSpan[1]));
        }
        else if (cmdSpan.Length == 5 && cmdSpan.StartsWith("BPM", StringComparison.OrdinalIgnoreCase)
                                     && tryParseDouble(valueSpan, out var extendedBpm) && extendedBpm != 0)
            state.BpmDefinitions[encodeValue(state.UseBase62, cmdSpan[3], cmdSpan[4])] = extendedBpm;
        else if (cmdSpan.Length == 6 && cmdSpan.StartsWith("STOP", StringComparison.OrdinalIgnoreCase)
                                     && tryParseDouble(valueSpan, out var stopValue) && stopValue > 0)
            state.StopDefinitions[encodeValue(state.UseBase62, cmdSpan[4], cmdSpan[5])] = stopValue;
    }

    private static bool isImportChannel(ushort channel)
    {
        var hi = Hi(channel);
        return channel is CH_03 or CH_08 or CH_09 || hi is 1 or 2 or 5 or 6 or 13 or 14;
    }

    private static BmsChartMetadata extractImportMetadata(ParseState state, int totalColumns, string? path)
    {
        var title = state.Title ?? (path == null ? string.Empty : Path.GetFileNameWithoutExtension(path));

        // Only the #SUBTITLE-derived name is known per-chart. The title-vs-difficulty
        // split is deferred to the set level (BmsFileImporter), where
        // InferCommonSetTitle gives the authoritative base title across all charts in
        // the folder — a per-chart InferTitle guess here would diverge for truncated
        // or outlier titles.
        var diffName = string.IsNullOrWhiteSpace(state.Subtitle)
            ? string.Empty
            : StripDifficultyDelimiters(state.Subtitle);

        return new BmsChartMetadata(
            Artist: state.Artist ?? string.Empty,
            DifficultyName: diffName,
            KeyCount: totalColumns,
            RawTitle: title,
            Rank: state.Rank,
            Total: state.Total,
            PlayLevel: state.PlayLevel,
            LockedLongNoteMode: state.LnMode);
    }

    private static double computeImportBpm(IReadOnlyList<BmsBpmEvent> bpms, double finalObjectEndTime)
    {
        if (bpms.Count == 0)
            return 0;

        if (bpms.Count == 1)
            return Math.Round(bpms[0].Bpm, 1);

        double totalWeight = 0;
        double weightedSum = 0;

        for (var i = 0; i < bpms.Count; i++)
        {
            var time = bpms[i].Time;
            var nextTime = i + 1 < bpms.Count ? bpms[i + 1].Time : finalObjectEndTime > 0 ? finalObjectEndTime : 60000;
            var duration = nextTime - time;

            if (duration <= 0)
                continue;

            weightedSum += bpms[i].Bpm * duration;
            totalWeight += duration;
        }

        return totalWeight > 0 ? Math.Round(weightedSum / totalWeight, 1) : Math.Round(bpms[0].Bpm, 1);
    }

    private static List<ImportHitObject> collectImportHitObjects(
        ParseState state, int totalColumns, IReadOnlyDictionary<int, long> measureStarts, BmsTimingMap timingMap)
    {
        var (notes, lnCells, mines) = collectPlayableCells(state, totalColumns, measureStarts);

        var output = new List<ImportHitObject>(notes.Count + lnCells.Count + mines.Count);

        if (state.LnType == 2)
            collectImportLnType2Objects(lnCells, timingMap, output);
        else
            collectImportLnType1Objects(lnCells, timingMap, output);

        collectImportVisibleObjects(notes, state, timingMap, output);

        foreach (var mine in mines.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
        {
            var startTime = timingMap.ProjectTickToTime(mine.Tick);
            output.Add(new ImportHitObject(mine.Tick, mine.Column, startTime, startTime, false, true));
        }

        return output;
    }

    private static void collectImportVisibleObjects(
        IEnumerable<RawCell> notes, ParseState state, BmsTimingMap timingMap,
        List<ImportHitObject> output)
    {
        if (state.LnObjValues.Count == 0)
        {
            foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
                output.Add(createImportHitObject(note, note.Tick, false, timingMap));

            return;
        }

        var pendingByColumn = new Dictionary<int, RawCell>();

        foreach (var note in notes.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
        {
            if (state.LnObjValues.Contains(note.Value))
            {
                if (pendingByColumn.Remove(note.Column, out var start) && note.Tick > start.Tick)
                    output.Add(createImportHitObject(start, note.Tick, true, timingMap));

                continue;
            }

            if (pendingByColumn.TryGetValue(note.Column, out var previous))
                output.Add(createImportHitObject(previous, previous.Tick, false, timingMap));

            pendingByColumn[note.Column] = note;
        }

        foreach (var pending in pendingByColumn.Values.OrderBy(n => n.Tick).ThenBy(n => n.Sequence))
            output.Add(createImportHitObject(pending, pending.Tick, false, timingMap));
    }

    private static void collectImportLnType1Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap,
        List<ImportHitObject> output)
    {
        var openByColumn = new Dictionary<int, RawCell>();

        foreach (var cell in lnCells.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
        {
            if (openByColumn.Remove(cell.Column, out var start))
            {
                if (cell.Tick > start.Tick)
                    output.Add(createImportHitObject(start, cell.Tick, true, timingMap));
            }
            else
            {
                openByColumn[cell.Column] = cell;
            }
        }
    }

    private static void collectImportLnType2Objects(
        IEnumerable<RawCell> lnCells, BmsTimingMap timingMap,
        List<ImportHitObject> output)
    {
        foreach (var channelGroup in lnCells.GroupBy(c => c.Channel))
        {
            RawCell? openRun = null;

            foreach (var cell in channelGroup.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
            {
                if (cell.Value != 0)
                {
                    openRun ??= cell;
                    continue;
                }

                if (openRun is { } start && cell.Tick > start.Tick)
                {
                    output.Add(createImportHitObject(start, cell.Tick, true, timingMap));
                    openRun = null;
                }
            }
        }
    }

    private static ImportHitObject createImportHitObject(RawCell start, long endTick, bool isLongNote, BmsTimingMap timingMap)
    {
        var startTime = timingMap.ProjectTickToTime(start.Tick);
        var endTime = timingMap.ProjectTickToTime(endTick);
        return new ImportHitObject(start.Tick, start.Column, startTime, isLongNote ? Math.Max(startTime, endTime) : startTime, isLongNote, false);
    }

    private readonly record struct ImportHitObject(
        long Tick,
        int Column,
        double StartTime,
        double EndTime,
        bool IsLongNote,
        bool IsMine);

    private struct ImportHitObjectComparer : IComparer<ImportHitObject>
    {
        public int Compare(ImportHitObject a, ImportHitObject b)
        {
            var cmp = a.StartTime.CompareTo(b.StartTime);
            if (cmp != 0) return cmp;

            cmp = a.Tick.CompareTo(b.Tick);
            if (cmp != 0) return cmp;

            return a.Column.CompareTo(b.Column);
        }
    }
}
