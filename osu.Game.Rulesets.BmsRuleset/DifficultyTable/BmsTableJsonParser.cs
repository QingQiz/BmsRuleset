using System;
using System.Collections.Generic;
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

        if (string.IsNullOrEmpty(name) || charts == null || charts.Count == 0)
            return null;

        var levelOrder = header?.LevelOrder;
        var levelIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (levelOrder != null)
        {
            for (var i = 0; i < levelOrder.Length; i++)
                levelIndexMap[levelOrder[i]] = i;
        }

        var entries = new List<TableEntry>();
        foreach (var c in charts)
        {
            var hash = PickHash(c.Md5, c.Sha256);
            if (hash == null) continue;

            var level = c.Level ?? string.Empty;
            var levelIndex = levelIndexMap.GetValueOrDefault(level, int.MaxValue);

            entries.Add(new TableEntry
            {
                Level = level,
                LevelIndex = levelIndex,
                Md5Hash = hash,
                Title = c.Title,
                Artist = c.Artist,
            });
        }

        return new DifficultyTable
        {
            Name = name,
            Symbol = symbol,
            LevelOrder = levelOrder ?? [],
            Source = source,
            SourcePath = sourcePath,
            Entries = entries,
        };
    }

    private static bool isHexOfLength(string? s, int len) =>
        s != null && s.Length == len && s.All(static c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

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
        };
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
