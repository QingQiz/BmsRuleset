using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        var legacy = !root.TryGetProperty("version", out var version) || version.ValueKind == JsonValueKind.String && version.GetString() == "0.21";
        if (legacy)
            root = BmsonLegacyReader.Normalize(root, cancellationToken);
        else if (version.ValueKind != JsonValueKind.String || !Regex.IsMatch(version.GetString()!, @"^1\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$"))
            throw new InvalidDataException($"Unsupported BMSON version {version.GetRawText()}; expected a stable 1.x version or legacy 0.21.");

        var info = root.GetProperty("info");
        var initialBpm = positive(number(info, "init_bpm"), "init_bpm");
        var resolution = Math.Abs(number(info, "resolution", 240));
        if (resolution == 0)
            resolution = 240;
        if (resolution != Math.Truncate(resolution) || resolution > int.MaxValue / 4)
            throw new InvalidDataException($"Invalid BMSON resolution {info.GetProperty("resolution").GetRawText()}; expected an integer with absolute value no greater than {int.MaxValue / 4}.");

        var tickResolution = checked((int)resolution * 4);
        var modeHint = text(info, "mode_hint", "beat-7k");
        var layout = BmsLayout.ParseModeHint(modeHint) ?? throw new InvalidDataException($"Unsupported BMSON mode_hint: {modeHint}.");
        var longNoteMode = readLongNoteMode(info, "ln_type", BmsLongNoteMode.LongNote);

        var (bpmEvents, stopEvents) = readTiming(root, initialBpm, tickResolution, cancellationToken);
        var timing = new BmsTickTimeConverter(tickResolution, bpmEvents, stopEvents);
        var sampleDefinitions = new Dictionary<int, BmsSampleDefinition>();
        var objects = new Dictionary<(int Column, long Pulse), (long EndPulse, BmsLongNoteMode Mode, List<int> Samples)>();
        var background = new List<BmsSampleEvent>();
        var releases = new Dictionary<(int Column, long Pulse), List<int>>();
        var invisible = new Dictionary<(int Column, long Pulse), BmsParsedHitObject>();
        var nextSampleId = 1;
        var judgeRank = number(info, "judge_rank", 100);
        if (judgeRank < 0)
            throw new InvalidDataException($"BMSON judge_rank must be non-negative; received {info.GetProperty("judge_rank").GetRawText()}.");

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
                    var column = mapColumn(note.Lane, layout, note.Pulse, samplePath);
                    var key = (column, note.Pulse);
                    if (note.Release)
                    {
                        if (!releases.TryGetValue(key, out var releaseSamples))
                            releases[key] = releaseSamples = [];
                        if (!releaseSamples.Contains(sampleId))
                            releaseSamples.Add(sampleId);
                        continue;
                    }

                    var mode = note.Length > 0 ? note.Mode : BmsLongNoteMode.Undefined;
                    if (objects.TryGetValue(key, out var existing))
                    {
                        if (existing.EndPulse != endPulse
                            || (existing.Mode == BmsLongNoteMode.Undefined ? longNoteMode : existing.Mode)
                            != (mode == BmsLongNoteMode.Undefined ? longNoteMode : mode))
                            throw new InvalidDataException($"Conflicting BMSON note lengths at x={note.Lane}, y={note.Pulse}.");

                        // IDs increase as channels are read; duplicate uses in this group share the last ID.
                        if (existing.Samples[^1] != sampleId)
                            existing.Samples.Add(sampleId);
                    }
                    else
                        objects.Add(key, (endPulse, mode, [sampleId]));
                }

                if (!hasPlayableNote)
                    background.Add(new BmsSampleEvent(time, sampleId, 100));
                sourceOffset = endOffset ?? sourceOffset;
            }
        }

        var parsedObjects = new List<BmsParsedHitObject>(objects.Count);
        foreach (var (key, note) in objects)
        {
            var time = timing.ProjectTickToTime(key.Pulse);
            var isLongNote = note.EndPulse > key.Pulse;
            var tailSamples = isLongNote && releases.Remove((key.Column, note.EndPulse), out var samples) ? samples : null;
            parsedObjects.Add(new BmsParsedHitObject(key.Pulse, time, timing.ProjectTickToTime(note.EndPulse) - time,
                key.Column, note.Samples[0], isLongNote, false, 0, tailSamples?[0], JudgementRate: double.NaN)
            {
                LongNoteMode = note.Mode,
                AdditionalSampleKeys = note.Samples.Skip(1).ToArray(),
                AdditionalTailSampleKeys = tailSamples?.Skip(1).ToArray() ?? [],
            });
        }

        if (releases.Count > 0)
            throw new InvalidDataException("BMSON release sound has no matching long-note endpoint.");

        readAuxiliaryChannels("key_channels", false);
        readAuxiliaryChannels("mine_channels", true);
        var hitObjects = parsedObjects.OrderBy(note => note.Tick).ThenBy(note => note.Column).ToArray();
        foreach (var column in hitObjects.GroupBy(note => note.Column))
        {
            double previousEnd = -1;
            long previousPulse = -1;
            foreach (var note in column)
            {
                if (note.Tick == previousPulse || note.StartTime < previousEnd)
                    throw new InvalidDataException($"Overlapping BMSON long notes in column {note.Column}.");

                previousEnd = note.StartTime + note.Duration;
                previousPulse = note.Tick;
            }
        }

        var bga = readBga(root, timing, hitObjects.Where(note => !note.IsMine).Select(note => note.StartTime).DefaultIfEmpty(0).Min(), cancellationToken);
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
        var mainBpm = BmsTimingMap.FindMainBpm(bpmEvents, hitObjects.Where(note => !note.IsMine).Select(note => note.Tick));
        var scrollEvents = array(root, "scroll_events").Select((evt, index) => new BmsScrollEvent(pulse(evt, "y"), number(evt, "rate", 1), index))
            .OrderBy(evt => evt.Tick).ThenBy(evt => evt.Sequence).ToArray();
        var timingMap = BmsTimingMap.FromSortedEvents(tickResolution, measures, bpmEvents, stopEvents, scrollEvents, [], mainBpm, barLines: barLines);
        double? total = legacy
            ? info.TryGetProperty("total", out var legacyTotal) && legacyTotal.ValueKind != JsonValueKind.Null
                ? Math.Abs(number(info, "total"))
                : null
            : BmsGaugeCalculator.CalculateDefaultTotal(hitObjects.Where(note => !note.IsMine)
                    .Sum(note => note.IsLongNote
                                 && (note.LongNoteMode == BmsLongNoteMode.Undefined ? longNoteMode : note.LongNoteMode)
                                 is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote
                        ? 2
                        : 1),
                BmsGaugeProfileFamilyProvider.FromLayout(layout)) * Math.Abs(number(info, "total", 100)) / 100;
        var credits = array(info, "subartists").Select(value => value.GetString() ?? string.Empty).ToArray();
        var maker = credits.Select(credit => credit.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(credit => credit.Length == 2 && credit[0] == "chart").Select(credit => credit[1]);
        return new BmsParseResult(text(info, "title"), text(info, "artist"), (float)pulse(info, "level", 0), 2, total,
            timingMap, layout, BmsLayout.GetTotalColumns(layout), sampleDefinitions, background.OrderBy(evt => evt.Time).ToArray(), hitObjects,
            [], new BmsTextEvents(null, []), bga, resource(info, "preview_music"), text(info, "genre"), text(info, "subtitle"),
            string.Join(", ", credits), string.Join(", ", maker), LockedLongNoteMode: longNoteMode,
            StageFile: resource(info, "eyecatch_image"), BackBmp: resource(info, "back_image"), Banner: resource(info, "banner_image"), DefaultExRank: exRank)
        {
            ExplicitDifficultyName = text(info, "chart_name"),
            InvisibleNotes = invisible.Values.OrderBy(note => note.Tick).ThenBy(note => note.Column).ToArray(),
        };

        void readAuxiliaryChannels(string field, bool mine)
        {
            foreach (var channel in array(root, field))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sampleId = checked(nextSampleId++);
                var samplePath = resource(channel, "name");
                sampleDefinitions.Add(sampleId, new BmsSampleDefinition(samplePath));
                foreach (var note in array(channel, "notes", true))
                {
                    var position = pulse(note, "y");
                    var lane = checked((int)pulse(note, "x"));
                    var column = mapColumn(lane, layout, position, samplePath);
                    var damage = mine ? number(note, "damage", 0) : 0;
                    if (damage < 0)
                        throw new InvalidDataException($"BMSON {field}.damage must be non-negative; received {note.GetProperty("damage").GetRawText()} at x={lane}, y={position}, sample=\"{samplePath}\".");

                    var parsed = new BmsParsedHitObject(position, timing.ProjectTickToTime(position), 0, column, sampleId,
                        false, mine, damage, null, JudgementRate: double.NaN);
                    if (mine)
                        parsedObjects.Add(parsed);
                    else
                        invisible[(column, position)] = parsed;
                    lastPulse = Math.Max(lastPulse, position);
                }
            }
        }
    }

    internal static BmsImportSummary ParseImportSummary(byte[] content, CancellationToken cancellationToken = default)
    {
        var result = Parse(content, cancellationToken);
        var length = result.HitObjects.Select(note => note.StartTime + note.Duration).DefaultIfEmpty(0).Max();
        return new BmsImportSummary(new BmsChartMetadata(result.Artist ?? string.Empty,
                result.ExplicitDifficultyName ?? string.Empty,
                result.LayoutVariant, result.Title ?? string.Empty, result.Rank, result.Total, result.PlayLevel, result.LockedLongNoteMode, result.DefaultExRank),
            result.TimingMap.BpmEvents.First().Bpm, length, result.HitObjects.Count,
            result.HitObjects.Count(note => note.IsLongNote), result.HitObjects.Count(note => BmsLayout.IsScratchColumn(note.Column, result.LayoutVariant)),
            result.HitObjects.Where(note => !note.IsMine).Select(note => new BmsNoteTiming(note.Column, note.StartTime, note.StartTime + note.Duration)).ToArray());
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

    private static BmsBgaTimeline readBga(JsonElement root, BmsTickTimeConverter timing, double firstNoteTime, CancellationToken cancellationToken)
    {
        var definitions = new Dictionary<int, string>();
        var ids = new Dictionary<long, int>();
        var events = new List<BmsBgaEvent>();
        if (root.TryGetProperty("bga", out var bga) && bga.ValueKind != JsonValueKind.Null)
        {
            foreach (var header in array(bga, "bga_header"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = pulse(header, "id");
                if (!ids.TryGetValue(id, out var key))
                    ids[id] = key = definitions.Count + 1;
                definitions[key] = resource(header, "name");
            }

            var sequences = new Dictionary<long, BmsBgaFrame[]>();
            foreach (var sequence in array(bga, "bga_sequence"))
            {
                sequences[pulse(sequence, "id")] = array(sequence, "sequence", true).Select(frame => new BmsBgaFrame(
                    pulse(frame, "time"), frame.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null
                        ? resolveId(pulse(frame, "id"))
                        : null)).OrderBy(frame => frame.Offset).ToArray();
            }

            foreach (var (name, layer) in new[] { ("bga_events", BmsBgaLayer.Base), ("layer_events", BmsBgaLayer.Layer1), ("poor_events", BmsBgaLayer.Poor) })
            {
                foreach (var evt in array(bga, name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var position = pulse(evt, "y");
                    var sourceIds = evt.TryGetProperty("id_set", out var set) && set.ValueKind != JsonValueKind.Null && set.GetArrayLength() > 0
                        ? set.EnumerateArray().Select(value => value.GetInt64()).ToArray()
                        : [pulse(evt, "id")];
                    var condition = text(evt, "condition");
                    var trigger = condition switch
                    {
                        "" => layer == BmsBgaLayer.Poor ? BmsBgaTrigger.Miss : BmsBgaTrigger.Always,
                        "play" => BmsBgaTrigger.Play,
                        "miss" => BmsBgaTrigger.Miss,
                        _ => throw new InvalidDataException($"Unsupported BMSON BGA condition: {condition}."),
                    };
                    var extended = sourceIds.Length > 1 || sourceIds.Any(sequences.ContainsKey) || condition.Length > 0;
                    BmsBgaAnimation? animation = null;
                    var key = 0;
                    if (extended)
                    {
                        var variants = sourceIds.Select(id => sequences.TryGetValue(id, out var frames)
                            ? frames
                            : new[] { new BmsBgaFrame(0, resolveId(id)), new BmsBgaFrame(500, null) }).ToArray();
                        animation = new BmsBgaAnimation(variants, trigger, checked((int)Math.Max(1, pulse(evt, "interval", 1))));
                    }
                    else
                        key = resolveId(sourceIds[0]);

                    events.Add(new BmsBgaEvent(timing.ProjectTickToTime(position), position, key, layer, events.Count) { Animation = animation });
                }
            }

            int resolveId(long id) => ids.TryGetValue(id, out var key)
                ? key
                : throw new InvalidDataException("BMSON BGA event references an undefined image.");
        }

        var info = root.GetProperty("info");
        addStaticImage(resource(info, "back_image"), BmsBgaLayer.Background, double.PositiveInfinity);
        addStaticImage(resource(info, "title_image"), BmsBgaLayer.Title, firstNoteTime);

        void addStaticImage(string path, BmsBgaLayer layer, double until)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var key = definitions.Count + 1;
            definitions[key] = path;
            events.Add(new BmsBgaEvent(double.MinValue, 0, key, layer, events.Count) { EndTime = until });
        }

        return new BmsBgaTimeline(definitions, new Dictionary<int, BmsBgaDefinition>(), events.OrderBy(evt => evt.Time).ThenBy(evt => evt.Sequence).ToArray(), [], BmsPoorBgaMode.Replace);
    }

    private static Note readNote(JsonElement value)
    {
        return new Note(checked((int)pulse(value, "x", 0)), pulse(value, "y"), pulse(value, "l", 0),
            value.TryGetProperty("c", out var continuation) && continuation.GetBoolean(),
            value.TryGetProperty("up", out var release) && release.GetBoolean(), readLongNoteMode(value, "t", BmsLongNoteMode.Undefined));
    }

    private static BmsLongNoteMode readLongNoteMode(JsonElement parent, string field, BmsLongNoteMode fallback)
    {
        var value = pulse(parent, field, 0);
        return value switch
        {
            0 => fallback,
            >= 1 and <= 3 => (BmsLongNoteMode)value,
            _ => throw new InvalidDataException($"Invalid BMSON {field}={value}; expected 0, 1 (LN), 2 (CN), or 3 (HCN)."),
        };
    }

    private static int mapColumn(int lane, BmsLayoutVariant layout, long position, string samplePath) => layout switch
    {
        BmsLayoutVariant.Popn9K when lane is >= 1 and <= 9 => lane - 1,
        BmsLayoutVariant.Popn5K when lane is >= 1 and <= 5 => lane - 1,
        _ when BmsLayout.IsKeyboard(layout)
               && lane >= 1 && lane <= BmsLayout.GetTotalColumns(layout) => lane - 1,
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
        _ => throw new InvalidDataException($"Unsupported BMSON lane x={lane} at y={position} for {layout} ({BmsLayout.GetTotalColumns(layout)} columns), sample=\"{samplePath}\"."),
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
            throw new InvalidDataException($"BMSON {name} must be a finite number; received {value.GetRawText()}.");

        return result;
    }

    private static long pulse(JsonElement parent, string name, long? fallback = null)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return fallback ?? throw new InvalidDataException($"Missing BMSON {name}.");

        if (!value.TryGetInt64(out var result) || result < 0)
            throw new InvalidDataException($"BMSON {name} must be a non-negative integer; received {value.GetRawText()}.");

        return result;
    }

    private static double positive(double value, string name) => value > 0 ? value : throw new InvalidDataException($"BMSON {name} must be positive; received {value}.");

    private static string resource(JsonElement parent, string name)
    {
        var path = text(parent, name).Replace('\\', '/');
        if (path.IndexOfAny(['\0', ':']) >= 0 || path.StartsWith('/') || path.Split('/').Contains(".."))
            throw new InvalidDataException($"Unsafe BMSON resource path: {path}.");

        return path;
    }

    private readonly record struct Note(int Lane, long Pulse, long Length, bool Continue, bool Release, BmsLongNoteMode Mode);
}
