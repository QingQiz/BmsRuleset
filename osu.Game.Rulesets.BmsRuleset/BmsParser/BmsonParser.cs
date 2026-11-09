using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static class BmsonParser
{
    internal static BmsParseResult Parse(byte[] content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(content.AsMemory(content.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0));
        var root = document.RootElement;
        if (text(root, "version") != "1.0.0")
            throw new InvalidDataException("Only BMSON 1.0.0 is supported; legacy charts must be converted first.");

        var info = root.GetProperty("info");
        var initialBpm = positive(number(info, "init_bpm"), "init_bpm");
        var resolution = Math.Abs(number(info, "resolution", 240));
        if (resolution == 0)
            resolution = 240;
        if (resolution != Math.Truncate(resolution) || resolution > int.MaxValue / 4)
            throw new InvalidDataException("Invalid BMSON resolution.");

        var tickResolution = checked((int)resolution * 4);
        var layout = text(info, "mode_hint", "beat-7k") switch
        {
            "beat-5k" => BmsLayoutVariant.Bms5K,
            "beat-7k" => BmsLayoutVariant.Bme7K,
            "beat-10k" => BmsLayoutVariant.Bms5KDouble,
            "beat-14k" => BmsLayoutVariant.Bme7KDouble,
            "popn-9k" => BmsLayoutVariant.Pms9K,
            var mode => throw new InvalidDataException($"Unsupported BMSON mode_hint: {mode}."),
        };
        foreach (var extension in new[] { "scroll_events", "mine_channels", "key_channels" })
        {
            if (array(root, extension).Any())
                throw new InvalidDataException($"Unsupported BMSON extension: {extension}.");
        }

        if (number(info, "ln_type", 0) != 0)
            throw new InvalidDataException("The BMSON ln_type extension is not supported.");

        var (bpmEvents, stopEvents) = readTiming(root, initialBpm, tickResolution, cancellationToken);
        var timing = new BmsTickTimeConverter(tickResolution, bpmEvents, stopEvents);
        var sampleDefinitions = new Dictionary<int, BmsSampleDefinition>();
        var objects = new Dictionary<(int Column, long Pulse), BmsParsedHitObject>();
        var background = new List<BmsSampleEvent>();
        var nextSampleId = 1;
        var judgeRank = number(info, "judge_rank", 100);
        if (judgeRank < 0)
            throw new InvalidDataException("BMSON judge_rank must be non-negative.");

        var exRank = judgeRank / BmsJudgementProfileProvider.RateForRank(layout, 2);
        long lastPulse = 0;

        foreach (var channel in array(root, "sound_channels", true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samplePath = resource(channel, "name");
            var groups = array(channel, "notes", true).Select(readNote).GroupBy(note => note.Pulse).OrderBy(group => group.Key).ToArray();
            double sourceOffset = 0;
            for (var i = 0; i < groups.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = groups[i];
                var time = timing.ProjectTickToTime(group.Key);
                if (group.Any(note => !note.Continue))
                    sourceOffset = 0;
                double? endOffset = i + 1 < groups.Length
                    ? sourceOffset + timing.ProjectTickToTime(groups[i + 1].Key) - time
                    : null;
                var sampleId = checked(nextSampleId++);
                sampleDefinitions.Add(sampleId, new BmsSampleDefinition(samplePath, sourceOffset, endOffset, ResumeInPreview: true));

                var hasPlayableNote = false;
                foreach (var note in group)
                {
                    var endPulse = checked(note.Pulse + note.Length);
                    lastPulse = Math.Max(lastPulse, endPulse);
                    if (note.Lane == 0)
                        continue;

                    hasPlayableNote = true;
                    var column = mapColumn(note.Lane, layout);
                    var key = (column, note.Pulse);
                    var duration = timing.ProjectTickToTime(endPulse) - time;
                    if (objects.TryGetValue(key, out var existing))
                    {
                        if (existing.IsLongNote != (note.Length > 0)
                            || existing.Duration != duration)
                            throw new InvalidDataException($"Conflicting BMSON note lengths at x={note.Lane}, y={note.Pulse}.");

                        if (existing.SampleKey != sampleId && !existing.AdditionalSampleKeys.Contains(sampleId))
                            objects[key] = existing with { AdditionalSampleKeys = [.. existing.AdditionalSampleKeys, sampleId] };
                    }
                    else
                    {
                        objects.Add(key, new BmsParsedHitObject(note.Pulse, time, duration,
                            column, sampleId, note.Length > 0, false, 0, null, JudgementRate: double.NaN));
                    }
                }

                if (!hasPlayableNote)
                    background.Add(new BmsSampleEvent(time, sampleId, 100));
                sourceOffset = endOffset ?? sourceOffset;
            }
        }

        var hitObjects = objects.Values.OrderBy(note => note.Tick).ThenBy(note => note.Column).ToArray();
        foreach (var column in hitObjects.GroupBy(note => note.Column))
        {
            double previousEnd = -1;
            foreach (var note in column)
            {
                if (note.StartTime < previousEnd)
                    throw new InvalidDataException($"Overlapping BMSON long notes in column {note.Column}.");

                previousEnd = note.StartTime + note.Duration;
            }
        }

        var bga = readBga(root, timing, cancellationToken);
        lastPulse = Math.Max(lastPulse, bga.Events.Select(evt => evt.Tick).DefaultIfEmpty(0).Max());
        var explicitLines = root.TryGetProperty("lines", out var lines) && lines.ValueKind != JsonValueKind.Null;
        var barLines = explicitLines
            ? array(root, "lines").Select(line => pulse(line, "y")).Distinct().Order().ToArray()
            : regularLines(lastPulse, tickResolution);
        var measureStarts = explicitLines
            ? barLines.Length > 0 ? barLines.Prepend(0).Distinct().ToArray() : regularLines(lastPulse, tickResolution)
            : barLines;
        var measures = measureStarts.Select((start, index) =>
        {
            var length = index + 1 < measureStarts.Length ? measureStarts[index + 1] - start : tickResolution;
            return new BmsMeasureInfo(index, start, length, length / (double)tickResolution);
        }).ToArray();
        var mainBpm = BmsTimingMap.FindMainBpm(bpmEvents, hitObjects.Select(note => note.Tick));
        var timingMap = BmsTimingMap.FromSortedEvents(tickResolution, measures, bpmEvents, stopEvents, [], [], mainBpm, barLines: barLines);
        var total = BmsGaugeCalculator.CalculateDefaultTotal(hitObjects.Length) * Math.Abs(number(info, "total", 100)) / 100;
        var credits = array(info, "subartists").Select(value => value.GetString() ?? string.Empty).ToArray();
        var maker = credits.Select(credit => credit.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(credit => credit.Length == 2 && credit[0] == "chart").Select(credit => credit[1]);
        return new BmsParseResult(text(info, "title"), text(info, "artist"), (float)pulse(info, "level", 0), 2, total,
            timingMap, layout, BmsLayout.GetTotalColumns(layout), sampleDefinitions, background.OrderBy(evt => evt.Time).ToArray(), hitObjects,
            [], new BmsTextEvents(null, []), bga, resource(info, "preview_music"), text(info, "genre"), text(info, "subtitle"),
            string.Join(", ", credits), string.Join(", ", maker), LockedLongNoteMode: BmsLongNoteMode.LongNote,
            StageFile: resource(info, "eyecatch_image"), BackBmp: resource(info, "back_image"), Banner: resource(info, "banner_image"), DefaultExRank: exRank)
        {
            ExplicitDifficultyName = text(info, "chart_name"),
        };
    }

    internal static BmsImportSummary ParseImportSummary(byte[] content, CancellationToken cancellationToken = default)
    {
        var result = Parse(content, cancellationToken);
        var length = result.HitObjects.Select(note => note.StartTime + note.Duration).DefaultIfEmpty(0).Max();
        return new BmsImportSummary(new BmsChartMetadata(result.Artist ?? string.Empty,
                result.ExplicitDifficultyName ?? string.Empty,
                result.TotalColumns, result.Title ?? string.Empty, result.Rank, result.Total, result.PlayLevel, result.LockedLongNoteMode, result.DefaultExRank),
            result.TimingMap.BpmEvents.First().Bpm, length, result.HitObjects.Count,
            result.HitObjects.Count(note => note.IsLongNote), result.HitObjects.Count(note => BmsLayout.IsScratchColumn(note.Column, result.LayoutVariant)),
            result.HitObjects.Select(note => new BmsNoteTiming(note.Column, note.StartTime, note.StartTime + note.Duration)).ToArray());
    }

    private static (BmsBpmEvent[] Bpms, BmsStopEvent[] Stops) readTiming(
        JsonElement root, double initialBpm, int resolution, CancellationToken cancellationToken)
    {
        var bpms = array(root, "bpm_events").Select(evt => (Pulse: pulse(evt, "y"), Bpm: positive(number(evt, "bpm"), "bpm")))
            .GroupBy(evt => evt.Pulse).ToDictionary(group => group.Key, group => group.Last().Bpm);
        var stops = array(root, "stop_events").Select(evt => (Pulse: pulse(evt, "y"), Duration: pulse(evt, "duration")))
            .GroupBy(evt => evt.Pulse).ToDictionary(group => group.Key, group => group.Sum(evt => evt.Duration));
        var bpmEvents = new List<BmsBpmEvent> { new(0, initialBpm, 0) };
        var stopEvents = new List<BmsStopEvent>();
        var bpm = initialBpm;
        double time = 0;
        long previousPulse = 0;
        foreach (var position in bpms.Keys.Concat(stops.Keys).Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            time += (position - previousPulse) * 240000d / bpm / resolution;
            if (bpms.TryGetValue(position, out var nextBpm))
            {
                bpm = nextBpm;
                bpmEvents.Add(new BmsBpmEvent(position, bpm, time, bpmEvents.Count));
            }

            if (stops.TryGetValue(position, out var stop))
            {
                var duration = stop * 240000d / bpm / resolution;
                stopEvents.Add(new BmsStopEvent(position, duration, stop, bpm, stopEvents.Count));
                time += duration;
            }

            previousPulse = position;
        }

        return (bpmEvents.ToArray(), stopEvents.ToArray());
    }

    private static BmsBgaTimeline readBga(JsonElement root, BmsTickTimeConverter timing, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<ushort, string>();
        var ids = new Dictionary<long, ushort>();
        var events = new List<BmsBgaEvent>();
        if (root.TryGetProperty("bga", out var bga) && bga.ValueKind != JsonValueKind.Null)
        {
            foreach (var header in array(bga, "bga_header"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = checked((ushort)(definitions.Count + 1));
                ids.Add(pulse(header, "id"), key);
                definitions.Add(key, resource(header, "name"));
            }

            foreach (var (name, layer) in new[] { ("bga_events", BmsBgaLayer.Base), ("layer_events", BmsBgaLayer.Layer1), ("poor_events", BmsBgaLayer.Poor) })
            {
                foreach (var evt in array(bga, name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = pulse(evt, "y");
                    if (!ids.TryGetValue(pulse(evt, "id"), out var key))
                        throw new InvalidDataException("BMSON BGA event references an undefined image.");

                    events.Add(new BmsBgaEvent(timing.ProjectTickToTime(position), position, key, layer, events.Count));
                }
            }
        }

        return new BmsBgaTimeline(definitions, new Dictionary<ushort, BmsBgaDefinition>(), events.OrderBy(evt => evt.Tick).ThenBy(evt => evt.Sequence).ToArray(), [], BmsPoorBgaMode.Replace);
    }

    private static Note readNote(JsonElement value)
    {
        if ((value.TryGetProperty("up", out var up) && up.GetBoolean()) || number(value, "t", 0) != 0)
            throw new InvalidDataException("BMSON note type and release-sound extensions are not supported.");

        return new Note(checked((int)pulse(value, "x", 0)), pulse(value, "y"), pulse(value, "l", 0),
            value.TryGetProperty("c", out var continuation) && continuation.GetBoolean());
    }

    private static int mapColumn(int lane, BmsLayoutVariant layout) => layout switch
    {
        BmsLayoutVariant.Pms9K when lane is >= 1 and <= 9 => lane - 1,
        BmsLayoutVariant.Bms5K when lane == 8 => 0,
        BmsLayoutVariant.Bms5K when lane is >= 1 and <= 5 => lane,
        BmsLayoutVariant.Bme7K when lane == 8 => 0,
        BmsLayoutVariant.Bme7K when lane is >= 1 and <= 7 => lane,
        BmsLayoutVariant.Bms5KDouble when lane == 8 => 0,
        BmsLayoutVariant.Bms5KDouble when lane == 16 => 11,
        BmsLayoutVariant.Bms5KDouble when lane is >= 1 and <= 5 => lane,
        BmsLayoutVariant.Bms5KDouble when lane is >= 9 and <= 13 => lane - 3,
        BmsLayoutVariant.Bme7KDouble when lane == 8 => 0,
        BmsLayoutVariant.Bme7KDouble when lane == 16 => 15,
        BmsLayoutVariant.Bme7KDouble when lane is >= 1 and <= 7 => lane,
        BmsLayoutVariant.Bme7KDouble when lane is >= 9 and <= 15 => lane - 1,
        _ => throw new InvalidDataException($"Unsupported BMSON lane {lane} for {layout}."),
    };

    private static long[] regularLines(long lastPulse, int resolution)
    {
        var count = checked((int)(lastPulse / resolution + 2));
        return Enumerable.Range(0, count).Select(index => (long)index * resolution).ToArray();
    }

    private static IEnumerable<JsonElement> array(JsonElement parent, string name, bool required = false)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
                throw new InvalidDataException($"Missing BMSON {name} array.");

            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"BMSON {name} must be an array.");

        return value.EnumerateArray();
    }

    private static string text(JsonElement parent, string name, string fallback = "") =>
        parent.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() ?? fallback : fallback;

    private static double number(JsonElement parent, string name, double? fallback = null)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return fallback ?? throw new InvalidDataException($"Missing BMSON {name}.");

        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new InvalidDataException($"Invalid BMSON {name}.");

        return result;
    }

    private static long pulse(JsonElement parent, string name, long? fallback = null)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return fallback ?? throw new InvalidDataException($"Missing BMSON {name}.");

        if (!value.TryGetInt64(out var result) || result < 0)
            throw new InvalidDataException($"BMSON {name} must be a non-negative integer.");

        return result;
    }

    private static double positive(double value, string name) => value > 0 ? value : throw new InvalidDataException($"BMSON {name} must be positive.");

    private static string resource(JsonElement parent, string name)
    {
        var path = text(parent, name).Replace('\\', '/');
        if (path.IndexOfAny(['\0', ':']) >= 0 || path.StartsWith('/') || path.Split('/').Contains(".."))
            throw new InvalidDataException($"Unsafe BMSON resource path: {path}.");

        return path;
    }

    private readonly record struct Note(int Lane, long Pulse, long Length, bool Continue);
}
