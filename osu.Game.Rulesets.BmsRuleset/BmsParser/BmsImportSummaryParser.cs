using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Game.Rulesets.BmsRuleset.Difficulty;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private const ushort ch_02 = 2;

    public static BmsImportSummary ParseImportSummary(
        byte[] content,
        string? path = null,
        Func<int, int>? randomValueSelector = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = decodeText(content);
        var state = new ImportParseState(randomValueSelector ?? selectRandomValue, cancellationToken);
        var offset = 0;

        // Raw channel slices retain the decoded text, avoiding a string allocation for every
        // WAV/BMP definition and BGM/BGA line that import will immediately discard.
        while (offset < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newline = source.AsSpan(offset).IndexOfAny('\r', '\n');
            var end = newline < 0 ? source.Length : offset + newline;
            parseImportLine(source, offset, end, state);
            offset = end + 1;
            if (offset < source.Length && source[end] == '\r' && source[offset] == '\n')
                offset++;
        }

        return createImportSummary(state, path);
    }

    public static BmsImportSummary ParseImportSummary(
        IEnumerable<string> lines,
        string? path = null,
        Func<int, int>? randomValueSelector = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = new ImportParseState(randomValueSelector ?? selectRandomValue, cancellationToken);
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parseImportLine(line, 0, line.Length, state);
        }

        return createImportSummary(state, path);
    }

    private static BmsImportSummary createImportSummary(ImportParseState import, string? path)
    {
        var state = import.Chart;
        state.CancellationToken.ThrowIfCancellationRequested();
        var tickResolution = calculateTickResolution(state);
        var measures = calculateMeasures(state, tickResolution);
        var measureStarts = measures.ToDictionary(m => m.Index, m => m.StartTick);
        var timingEvents = collectTimingEvents(state, measureStarts, tickResolution);
        var stopEvents = collectStopEvents(state, measureStarts, timingEvents);
        timingEvents = applyStopOffsetsToTimingEvents(timingEvents, stopEvents);
        state.CancellationToken.ThrowIfCancellationRequested();

        var layoutVariant = BmsLayout.InferVariant(state.ChannelLines.Select(l => l.Channel).Concat(import.InvisibleLayoutChannels), path);
        var totalColumns = BmsLayout.GetTotalColumns(layoutVariant);
        var timing = new BmsTickTimeConverter(tickResolution, timingEvents, stopEvents);
        var hitObjects = collectImportHitObjects(state, totalColumns, measureStarts, timing);

        hitObjects.Sort(default(ImportHitObjectComparer));
        state.CancellationToken.ThrowIfCancellationRequested();

        var noteTimings = new List<BmsNoteTiming>(hitObjects.Count);
        var longNoteCount = 0;
        var scratchCount = 0;
        foreach (var hitObject in hitObjects)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            if (!hitObject.IsMine)
                noteTimings.Add(new BmsNoteTiming(hitObject.Column, hitObject.StartTime, hitObject.EndTime));
            if (hitObject.IsLongNote)
                longNoteCount++;
            if (BmsLayout.IsScratchColumn(hitObject.Column, layoutVariant))
                scratchCount++;
        }

        var length = hitObjects.Count == 0 ? 0 : Math.Max(0, hitObjects[^1].EndTime);

        return new BmsImportSummary(
            extractImportMetadata(state, totalColumns, path),
            computeImportBpm(timingEvents, length),
            length,
            hitObjects.Count,
            longNoteCount,
            scratchCount,
            noteTimings);
    }

    private static void parseImportLine(string source, int start, int end, ImportParseState import)
    {
        var span = source.AsSpan(start, end - start).TrimStart();
        if (span.IsEmpty || span[0] != '#')
            return;

        start = end - span.Length;
        var state = import.Chart;
        if (span.Length >= 7 && span[1] is >= '0' and <= '9'
                             && span[2] is >= '0' and <= '9'
                             && span[3] is >= '0' and <= '9' && span[6] == ':')
        {
            if (!isActive(import.Frames))
                return;

            var channel = EncodePairCi(span[4], span[5]);
            var invisible = Hi(channel) is 3 or 4;
            if (channel != ch_02 && !invisible && !isImportChannel(channel))
                return;

            var payloadStart = start + 7;
            while (end > payloadStart && source[end - 1] is ' ' or '\t') end--;
            var payloadLength = end - payloadStart;

            if (invisible)
            {
                // These channel IDs affect key mode, but their payloads never affect import statistics.
                if (payloadLength >= 2)
                    import.InvisibleLayoutChannels.Add(channel);
                return;
            }

            var measure = (span[1] - '0') * 100 + (span[2] - '0') * 10 + span[3] - '0';
            if (channel == ch_02)
            {
                if (tryParseDouble(source.AsSpan(payloadStart, payloadLength), out var length) && length > 0)
                {
                    state.MeasureLengths[measure] = length;
                    state.MaxMeasure = Math.Max(state.MaxMeasure, measure);
                }

                return;
            }

            if (payloadLength >= 2)
            {
                state.ChannelLines.Add(new RawChannelLine(measure, channel, source, payloadStart, payloadLength, state.NextSequence++ * 4096));
                state.MaxMeasure = Math.Max(state.MaxMeasure, measure);
            }

            return;
        }

        if (tryReadControlCommand(span, out var command, out var value))
            applyControlCommand(command, value, import.Frames, import.RandomValueSelector, null);
        else if (isActive(import.Frames))
            applyCommandLine(span, state, CommandParseMode.ImportSummary);
    }

    private static bool isImportChannel(ushort channel) =>
        channel is CH_03 or CH_08 or CH_09
        || (Hi(channel) is 1 or 2 or 5 or 6 or 13 or 14 && Lo(channel) is >= 1 and <= 9);

    private sealed class ImportParseState(Func<int, int> randomValueSelector, CancellationToken cancellationToken)
    {
        public ParseState Chart { get; } = new() { CancellationToken = cancellationToken };

        public HashSet<ushort> InvisibleLayoutChannels { get; } = [];

        public List<ControlFrame> Frames { get; } = [];

        public Func<int, int> RandomValueSelector { get; } = randomValueSelector;
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
            LockedLongNoteMode: state.LnMode,
            ExRank: state.DefaultExRank);
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
        ParseState state, int totalColumns, IReadOnlyDictionary<int, long> measureStarts, BmsTickTimeConverter timingMap)
    {
        var (notes, lnCells, mines) = collectPlayableCells(state, totalColumns, measureStarts, importOnly: true);

        var output = new List<ImportHitObject>(notes.Count + lnCells.Count / 2 + mines.Count);

        if (state.LnType == 2)
            collectImportLnType2Objects(lnCells, timingMap, output, state.CancellationToken);
        else
            collectImportLnType1Objects(lnCells, timingMap, output, state.CancellationToken);

        collectImportVisibleObjects(notes, state, timingMap, output);

        foreach (var mine in mines)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            var startTime = timingMap.ProjectTickToTime(mine.Tick);
            output.Add(new ImportHitObject(mine.Tick, mine.Column, startTime, startTime, false, true));
        }

        return output;
    }

    private static void collectImportVisibleObjects(
        List<RawCell> notes, ParseState state, BmsTickTimeConverter timingMap,
        List<ImportHitObject> output)
    {
        if (state.LnObjValues.Count == 0)
        {
            foreach (var note in notes)
            {
                state.CancellationToken.ThrowIfCancellationRequested();
                output.Add(createImportHitObject(note, note.Tick, false, timingMap));
            }

            return;
        }

        var pendingByColumn = new Dictionary<int, RawCell>();

        foreach (var note in sortCells(notes))
        {
            state.CancellationToken.ThrowIfCancellationRequested();
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

        foreach (var pending in pendingByColumn.Values)
            output.Add(createImportHitObject(pending, pending.Tick, false, timingMap));
    }

    private static void collectImportLnType1Objects(
        List<RawCell> lnCells, BmsTickTimeConverter timingMap,
        List<ImportHitObject> output, CancellationToken cancellationToken)
    {
        var openByColumn = new Dictionary<int, RawCell>();

        foreach (var cell in sortCells(lnCells))
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        IEnumerable<RawCell> lnCells, BmsTickTimeConverter timingMap,
        List<ImportHitObject> output, CancellationToken cancellationToken)
    {
        foreach (var channelGroup in lnCells.GroupBy(c => c.Channel))
        {
            RawCell? openRun = null;

            foreach (var cell in channelGroup.OrderBy(c => c.Tick).ThenBy(c => c.Sequence))
            {
                cancellationToken.ThrowIfCancellationRequested();
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

    private static ImportHitObject createImportHitObject(RawCell start, long endTick, bool isLongNote, BmsTickTimeConverter timingMap)
    {
        var startTime = timingMap.ProjectTickToTime(start.Tick);
        var endTime = isLongNote ? Math.Max(startTime, timingMap.ProjectTickToTime(endTick)) : startTime;
        return new ImportHitObject(start.Tick, start.Column, startTime, endTime, isLongNote, false);
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
