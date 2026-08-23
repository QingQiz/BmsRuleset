using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public static class BmsTableJsonParser
{

    public static bool IsValidMd5(string? s) => isHexOfLength(s, 32);

    public static bool IsValidSha256(string? s) => isHexOfLength(s, 64);

    public static string? PickHash(string? md5, string? sha256)
    {
        if (IsValidMd5(md5)) return md5!.ToLowerInvariant();
        if (IsValidSha256(sha256)) return sha256!.ToLowerInvariant();

        return null;
    }

    /// <summary>
    /// Parse JSON once, returning header + data in one pass.
    /// </summary>
    public static ParseResult? Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;

            // Avoid calling TryGetProperty on non-object types (e.g. plain arrays)
            RawTableData? header = root.ValueKind == JsonValueKind.Object ? parseHeader(root) : null;
            var charts = parseData(root);

            if (charts == null && header?.DataUrl != null)
                return new ParseResult(header, null);

            if (header == null && charts == null)
                return null;

            return new ParseResult(header, charts);
        }
    }

    public static DifficultyTable? Merge(string sourcePath, TableSource source, RawTableData? header, List<RawChartItem>? charts)
    {
        var name = header?.Name;
        var symbol = header?.Symbol;

        if (string.IsNullOrEmpty(name))
            name = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(symbol))
            symbol = string.Empty;

        if (string.IsNullOrEmpty(name)
            || (charts is not { Count: > 0 } && header?.Courses is not { Count: > 0 }))
            return null;

        var validCharts = new List<(RawChartItem chart, string hash, string level)>();

        foreach (var chart in charts ?? [])
        {
            var hash = PickHash(chart.Md5, chart.Sha256);
            if (hash != null)
                validCharts.Add((chart, hash, chart.Level ?? string.Empty));
        }

        var levelOrder = header?.LevelOrder;

        if (levelOrder == null || levelOrder.Length == 0)
        {
            levelOrder = validCharts
                .Select(c => c.level)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(level => level, LevelOrderComparer.INSTANCE)
                .ToArray();
        }

        var levelIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < levelOrder.Length; i++)
            levelIndexMap[levelOrder[i]] = i;

        var entries = new List<TableEntry>();
        foreach (var (chart, hash, level) in validCharts)
        {
            var levelIndex = levelIndexMap.GetValueOrDefault(level, int.MaxValue);

            entries.Add(new TableEntry
            {
                Level = level,
                LevelIndex = levelIndex,
                Md5Hash = hash,
                Sha256Hash = IsValidSha256(chart.Sha256) ? chart.Sha256!.ToLowerInvariant() : null,
                Title = chart.Title,
                Artist = chart.Artist,
            });
        }

        return new DifficultyTable
        {
            Name = name,
            Symbol = symbol,
            LevelOrder = levelOrder,
            Source = source,
            SourcePath = sourcePath,
            Entries = entries,
            Courses = header?.Courses?
                .Where(course => !string.IsNullOrWhiteSpace(course.Name) && course.Hashes.Length > 0)
                .Select(course => new TableCourse
                {
                    Name = course.Name!,
                    Hashes = course.Hashes,
                    Constraints = course.Constraints,
                })
                .ToList() ?? [],
        };
    }

    private static bool isHexOfLength(string? s, int len) =>
        s != null && s.Length == len && s.All(static c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    private sealed class LevelOrderComparer : IComparer<string>
    {
        public static readonly LevelOrderComparer INSTANCE = new();

        public int Compare(string? x, string? y)
        {
            var xHasNumber = tryParseLevelNumber(x, out var xNumber);
            var yHasNumber = tryParseLevelNumber(y, out var yNumber);

            if (xHasNumber != yHasNumber)
                return xHasNumber ? -1 : 1;

            if (xHasNumber)
            {
                var numberComparison = xNumber.CompareTo(yNumber);
                if (numberComparison != 0)
                    return numberComparison;
            }

            var lexicalComparison = StringComparer.OrdinalIgnoreCase.Compare(x, y);
            return lexicalComparison != 0 ? lexicalComparison : StringComparer.Ordinal.Compare(x, y);
        }

        private static bool tryParseLevelNumber(string? level, out decimal number)
        {
            if (decimal.TryParse(level, NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                return true;

            if (level != null)
            {
                for (var numericStart = 1; numericStart < level.Length; numericStart++)
                {
                    if (char.IsAsciiDigit(level[numericStart - 1]))
                        break;

                    if (decimal.TryParse(level.AsSpan(numericStart), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                        return true;
                }
            }

            number = 0;
            return false;
        }
    }

    private static RawTableData parseHeader(JsonElement root)
    {
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var symbol = root.TryGetProperty("symbol", out var s) ? s.GetString() : null;
        var dataUrl = root.TryGetProperty("data_url", out var d) ? d.GetString() : null;

        string[]? levelOrder = null;
        if (root.TryGetProperty("level_order", out var lo) && lo.ValueKind == JsonValueKind.Array)
            levelOrder = lo.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();

        return new RawTableData
        {
            Name = name,
            Symbol = symbol,
            DataUrl = dataUrl,
            LevelOrder = levelOrder,
            Courses = parseCourses(root),
        };
    }

    private static List<RawCourse>? parseCourses(JsonElement root)
    {
        if (!root.TryGetProperty("course", out var courseElement))
            return null;

        var courses = new List<RawCourse>();
        collectCourses(courseElement, courses);
        return courses.Count > 0 ? courses : null;
    }

    private static void collectCourses(JsonElement element, List<RawCourse> courses)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
                collectCourses(child, courses);

            return;
        }

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            return;

        var md5 = readStringArray(element, "md5");
        var sha256 = readStringArray(element, "sha256");
        var genericHashes = readStringArray(element, "hash");
        var hashes = new List<string>();

        for (var i = 0; i < Math.Max(md5.Length, sha256.Length); i++)
        {
            var hash = PickHash(i < md5.Length ? md5[i] : null, i < sha256.Length ? sha256[i] : null);
            if (hash != null)
                hashes.Add(hash);
        }

        if (hashes.Count == 0)
        {
            hashes.AddRange(genericHashes
                .Where(hash => IsValidMd5(hash) || IsValidSha256(hash))
                .Select(hash => hash.ToLowerInvariant()));
        }

        if (hashes.Count == 0)
            return;

        courses.Add(new RawCourse
        {
            Name = nameElement.GetString(),
            Hashes = hashes.ToArray(),
            Constraints = readStringArray(element, "constraint"),
        });
    }

    private static string[] readStringArray(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var property))
            return [];

        if (property.ValueKind == JsonValueKind.String)
            return property.GetString() is { } value ? [value] : [];

        if (property.ValueKind != JsonValueKind.Array)
            return [];

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToArray();
    }

    private static List<RawChartItem>? parseData(JsonElement root)
    {
        JsonElement array;
        if (root.ValueKind == JsonValueKind.Array)
            array = root;
        else if (root.TryGetProperty("charts", out var c) && c.ValueKind == JsonValueKind.Array)
            array = c;
        else
            return null;

        var charts = new List<RawChartItem>();
        foreach (var item in array.EnumerateArray())
        {
            charts.Add(new RawChartItem
            {
                Level = item.TryGetProperty("level", out var l) ? l.GetString() : null,
                Md5 = item.TryGetProperty("md5", out var m) ? m.GetString() : null,
                Sha256 = item.TryGetProperty("sha256", out var sh) ? sh.GetString() : null,
                Title = item.TryGetProperty("title", out var t) ? t.GetString() : null,
                Artist = item.TryGetProperty("artist", out var a) ? a.GetString() : null,
            });
        }

        return charts;
    }
}

/// <summary>
/// Result of a single-pass JSON parse, containing both header and chart data.
/// </summary>
public class ParseResult(RawTableData? header, List<RawChartItem>? charts)
{
    public RawTableData? Header { get; } = header;

    public List<RawChartItem>? Charts { get; } = charts;
}
