using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

static internal partial class BmsChartParser
{
    public static IEnumerable<string> ScanResourceReferences(IEnumerable<string> lines) =>
        from rawLine in lines
        select stripComments(rawLine).Trim()
        into line
        where line.Length != 0 && line.StartsWith('#')
        select resourceDefinitionRegex().Match(line)
        into match
        where match.Success
        select match.Groups[1].Value.Trim().Trim('"')
        into value
        where value.Length > 0
        select value;

    public static BmsChartMetadata ScanMetadata(IEnumerable<string> lines, string? path = null)
    {
        var title = path == null ? string.Empty : Path.GetFileNameWithoutExtension(path);
        var artist = string.Empty;
        var channels = new List<string>();
        int? playerMode = null;

        foreach (var rawLine in lines)
        {
            var line = stripComments(rawLine).Trim();

            if (line.Length == 0 || !line.StartsWith('#'))
                continue;

            var channelMatch = channelLineRegex().Match(line);

            if (channelMatch.Success)
            {
                channels.Add(channelMatch.Groups[2].Value.ToUpperInvariant());
                continue;
            }

            var commandMatch = commandLineRegex().Match(line);

            if (!commandMatch.Success)
                continue;

            var command = commandMatch.Groups[1].Value.ToUpperInvariant();
            var value = commandMatch.Groups[2].Value.Trim();

            switch (command)
            {
                case "TITLE":
                    title = value;
                    break;

                case "ARTIST":
                    artist = value;
                    break;

                case "PLAYER" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPlayerMode):
                    playerMode = parsedPlayerMode;
                    break;
            }
        }

        return new BmsChartMetadata(title, artist, inferDifficultyName(title, path), BmsLayout.InferTotalColumns(channels, path, playerMode));
    }

    private static string inferDifficultyName(string title, string? path)
    {
        var start = title.LastIndexOf('[');
        var end = title.LastIndexOf(']');

        if (start >= 0 && end > start)
            return title[(start + 1)..end];

        return path == null ? title : Path.GetFileNameWithoutExtension(path);
    }

    [GeneratedRegex(@"^#(?:WAV[0-9A-Z]{2}|BMP[0-9A-Z]{2}|BGA[0-9A-Z]{2}|STAGEFILE|BANNER|BACKBMP|MOVIE)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex resourceDefinitionRegex();
}
