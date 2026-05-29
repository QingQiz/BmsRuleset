using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps;

public class BmsBeatmapDecoder : Decoder<Beatmap>
{
    private static readonly object registration_lock = new();
    private static bool registered;

    public static void Register()
    {
        lock (registration_lock)
        {
            if (registered)
                return;

            AddDecoder<Beatmap>("#", _ => new BmsBeatmapDecoder());
            AddDecoder<Beatmap>("*", _ => new BmsBeatmapDecoder());
            registered = true;
        }
    }

#pragma warning disable CA2255 // Ruleset assemblies are discovered before beatmap decode; register BMS formats as soon as the plugin loads.
    [ModuleInitializer]
#pragma warning restore CA2255
    static internal void RegisterOnAssemblyLoad() => Register();

    protected override Beatmap CreateTemplateObject() => new BmsDecodedBeatmap();

    protected override void ParseStreamInto(LineBufferedReader stream, Beatmap output)
    {
        var parseResult = BmsChartParser.Parse(readLines(stream, output.BeatmapInfo?.Path), output.BeatmapInfo?.Path);

        applyMetadata(output, parseResult);
        populateTiming(output, parseResult.TimingMap.BpmEvents);

        if (output is BmsDecodedBeatmap bmsOutput)
            bmsOutput.CopyFrom(parseResult);

        foreach (var parsedObject in parseResult.HitObjects)
        {
            output.HitObjects.Add(new BmsHitObject
            {
                TickInfo = new BmsTickInfo
                {
                    Tick = parsedObject.Tick,
                    EndTick = parsedObject.EndTick,
                },
                StartTime = parsedObject.StartTime,
                Duration = parsedObject.Duration,
                Column = parsedObject.Column,
                SourceChannel = parsedObject.SourceChannel,
                SampleKey = parsedObject.SampleKey,
                SamplePath = parsedObject.SamplePath,
                IsLongNote = parsedObject.IsLongNote,
                IsMine = parsedObject.IsMine,
                LandmineDamagePercent = parsedObject.LandmineDamagePercent,
                LandmineExplosionSamplePath = parsedObject.LandmineExplosionSamplePath,
            });
        }
    }

    private static string[] readLines(LineBufferedReader stream, string? path)
    {
        if (path != null && File.Exists(path))
            return BmsChartParser.ReadAllLines(File.ReadAllBytes(path));

        var lines = new List<string>();

        while (stream.ReadLine() is { } line)
            lines.Add(line);

        return lines.ToArray();
    }

    private static void applyMetadata(Beatmap output, BmsParseResult parseResult)
    {
        if (!string.IsNullOrWhiteSpace(parseResult.Title))
        {
            output.Metadata.Title = parseResult.Title;
            output.BeatmapInfo.DifficultyName = parseResult.Title;
        }

        if (parseResult.Artist != null)
            output.Metadata.Artist = parseResult.Artist;

        if (parseResult.Source != null)
            output.Metadata.Source = parseResult.Source;

        if (parseResult.OverallDifficulty != null)
            output.Difficulty.OverallDifficulty = parseResult.OverallDifficulty.Value;

        output.Difficulty.CircleSize = parseResult.TotalColumns;
        output.BeatmapInfo.Difficulty.CircleSize = parseResult.TotalColumns;
    }

    private static void populateTiming(Beatmap output, IEnumerable<BmsBpmEvent> timingEvents)
    {
        output.ControlPointInfo.Clear();

        foreach (var timingEvent in timingEvents.GroupBy(e => e.Tick).Select(g => g.Last()))
        {
            output.ControlPointInfo.Add(timingEvent.Time, new TimingControlPoint
            {
                BeatLength = 60000 / timingEvent.Bpm,
            });
        }
    }
}
