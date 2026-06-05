using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private static readonly bool encoding_provider_registered = registerEncodingProvider();

    private static readonly Encoding shift_jis_encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(932)
        ?? throw new InvalidOperationException("Shift-JIS encoding is not available.");

    /// <summary>
    /// Strips comments from raw BMS lines in a single pass. Use before calling
    /// <see cref="ScanMetadata"/> and <see cref="ScanResourceReferences"/> to avoid
    /// redundant stripping in each method.
    /// </summary>
    public static string[] PreprocessLines(IEnumerable<string> lines) =>
        lines.Select(l => BmsCommentStripper.StripAll(l).Trim()).ToArray();

    public static IEnumerable<string> ScanResourceReferences(IEnumerable<string> lines) =>
        from line in lines
        where line.Length != 0 && line.StartsWith('#')
        select resourceDefinitionRegex().Match(line)
        into match
        where match.Success
        select match.Groups[1].Value.Trim().Trim('"')
        into value
        where value.Length > 0
        select value;

    public static string[] ReadAllLines(byte[] bytes) => decodeText(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public static BmsChartMetadata ScanMetadata(IEnumerable<string> lines, string? path = null)
    {
        var title = path == null ? string.Empty : Path.GetFileNameWithoutExtension(path);
        var artist = string.Empty;
        var subtitle = string.Empty;
        var channels = new List<string>();

        foreach (var line in lines)
        {
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

                case "SUBTITLE":
                    subtitle = value;
                    break;
            }
        }

        var setTitle = inferSetTitle(title);
        var difficultyName = inferDifficultyName(title, subtitle, path);

        return new BmsChartMetadata(setTitle, artist, difficultyName, BmsLayout.InferTotalColumns(channels, path));
    }

    private static bool registerEncodingProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }

    private static string decodeText(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xef && content[1] == 0xbb && content[2] == 0xbf)
            return Encoding.UTF8.GetString(content);

        var utf8 = new UTF8Encoding(false, true);

        try
        {
            return utf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return shift_jis_encoding.GetString(content);
        }
    }

    private static string inferSetTitle(string title)
    {
        var start = title.LastIndexOf('[');
        var end = title.LastIndexOf(']');

        if (start > 0 && end == title.Length - 1)
            return title[..start].TrimEnd();

        return title;
    }

    private static string inferDifficultyName(string title, string subtitle, string? path)
    {
        var subtitleDifficulty = tryExtractBracketedSuffix(subtitle);

        if (!string.IsNullOrWhiteSpace(subtitleDifficulty))
            return subtitleDifficulty;

        var titleDifficulty = tryExtractBracketedSuffix(title);

        if (!string.IsNullOrWhiteSpace(titleDifficulty))
            return titleDifficulty;

        return path == null ? title : Path.GetFileNameWithoutExtension(path);
    }

    private static string? tryExtractBracketedSuffix(string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end == value.Length - 1 && end > start)
            return value[(start + 1)..end].Trim();

        return null;
    }

    [GeneratedRegex(@"^#(?:WAV[0-9A-Z]{2}|BMP[0-9A-Z]{2}|BGA[0-9A-Z]{2}|STAGEFILE|BANNER|BACKBMP|MOVIE)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex resourceDefinitionRegex();
}
