using System;
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

/// <summary>
/// The decoder is a lightweight beatmap parser that does not require the beatmap to be fully parsed
/// it is only for filtering/display purposes. For actual gameplay, a converter is needed to complete the remaining tasks.
/// Decoder does not preserve all branches as an AST.
/// Decoder keeps RawLines, so all original branch text is still available for later playable conversion.
/// Decoder also does a deterministic branch-1 materialisation to create a usable cached BmsDecodedBeatmap preview/metadata objec
/// </summary>
/// <param name="randomValueSelector"></param>
/// <param name="referenceBpmMode"></param>
public class BmsBeatmapDecoder(Func<int, int>? randomValueSelector = null, BmsReferenceBpmMode? referenceBpmMode = null) : Decoder<Beatmap>
{
    private static readonly object registration_lock = new();

    // The decoded beatmap is cached before play starts; only playable conversion should roll runtime branches.
    private Func<int, int> decodeBranchSelector => randomValueSelector ?? (_ => 1);

    private BmsReferenceBpmMode effectiveReferenceBpmMode => referenceBpmMode ?? BmsRuleset.CurrentReferenceBpmMode;

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
    internal static void RegisterOnAssemblyLoad() => Register();

    internal static BmsHitObject CreateHitObject(BmsParsedHitObject parsedObject, IBmsBeatmap beatmap)
    {
        var hitObject = BmsHitObject.CreateForKind(parsedObject.IsLongNote, parsedObject.IsMine);
        hitObject.Beatmap = beatmap;

        hitObject.TickInfo = new BmsTickInfo
        {
            Tick = parsedObject.Tick,
            EndTick = parsedObject.EndTick,
        };
        hitObject.StartTime = parsedObject.StartTime;
        hitObject.Column = parsedObject.Column;
        hitObject.SourceChannel = parsedObject.SourceChannel;
        hitObject.SampleKey = parsedObject.SampleKey;
        hitObject.SamplePath = parsedObject.SamplePath;
        if (hitObject is BmsLandmine mine)
            mine.LandmineDamagePercent = parsedObject.LandmineDamagePercent;
        if (hitObject is BmsLongNote ln)
        {
            ln.Duration = parsedObject.Duration;
            ln.TailSampleKey = parsedObject.TailSampleKey;
            ln.TailSamplePath = parsedObject.TailSamplePath;
        }

        return hitObject;
    }

    internal static void PopulateTiming(Beatmap output, IEnumerable<BmsBpmEvent> timingEvents)
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

    protected override Beatmap CreateTemplateObject() => new BmsDecodedBeatmap();

    protected override void ParseStreamInto(LineBufferedReader stream, bool _, Beatmap output)
    {
        var lines = readLines(stream, output.BeatmapInfo.Path);
        var parseResult = BmsChartParser.Parse(lines, output.BeatmapInfo.Path, decodeBranchSelector, effectiveReferenceBpmMode);

        applyMetadata(output, parseResult);
        PopulateTiming(output, parseResult.TimingMap.BpmEvents);

        if (output is BmsDecodedBeatmap bmsOutput)
        {
            bmsOutput.CopyFrom(parseResult);
            bmsOutput.RawLines = lines;
        }

        if (output is IBmsBeatmap bmsBeatmap)
        {
            foreach (var parsedObject in parseResult.HitObjects)
                output.HitObjects.Add(CreateHitObject(parsedObject, bmsBeatmap));
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
            output.Metadata.Title = !string.IsNullOrWhiteSpace(parseResult.Subtitle)
                ? $"{parseResult.Title} - {parseResult.Subtitle}"
                : parseResult.Title;
        }

        if (parseResult.Artist != null)
        {
            output.Metadata.Artist = !string.IsNullOrWhiteSpace(parseResult.SubArtist)
                ? $"{parseResult.Artist} ({parseResult.SubArtist})"
                : parseResult.Artist;
        }

        // Source may already be set to the chart directory by the importer (external-audio mode).
        // Only set a fallback if it's still null.
        if (string.IsNullOrWhiteSpace(output.Metadata.Source))
            output.Metadata.Source = "BMS";

        if (!string.IsNullOrWhiteSpace(parseResult.Maker))
            output.Metadata.Author.Username = parseResult.Maker;

        if (string.IsNullOrWhiteSpace(output.Metadata.BackgroundFile))
            output.Metadata.BackgroundFile = firstSongSelectBackground(parseResult) ?? string.Empty;

        var tags = string.Join(" ",
            new[] { parseResult.Genre, parseResult.Url, parseResult.Email, parseResult.Comment }
                .Where(t => !string.IsNullOrWhiteSpace(t)));

        if (!string.IsNullOrWhiteSpace(tags))
            output.Metadata.Tags = string.IsNullOrWhiteSpace(output.Metadata.Tags)
                ? tags
                : $"{output.Metadata.Tags} {tags}";

        BmsDifficultyInfo.FromParseResult(parseResult).WriteToOsuDifficulty(output);
    }

    private static string? firstSongSelectBackground(BmsParseResult parseResult)
    {
        if (!string.IsNullOrWhiteSpace(parseResult.StageFile))
            return parseResult.StageFile;

        if (!string.IsNullOrWhiteSpace(parseResult.BackBmp))
            return parseResult.BackBmp;

        return !string.IsNullOrWhiteSpace(parseResult.Banner)
            ? parseResult.Banner
            : null;
    }
}
