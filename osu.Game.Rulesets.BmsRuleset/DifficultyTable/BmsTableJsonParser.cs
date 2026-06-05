using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public class BmsTableJsonParser
{
    private static readonly JsonSerializerOptions json_options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Returns true if the string looks like a valid MD5 hash (32 lowercase hex chars).
    /// </summary>
    private static bool isHexOfLength(string? s, int len) =>
        s != null && s.Length == len && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

    /// <summary>
    /// Returns true if the string looks like a valid MD5 hash (32 hex chars).
    /// </summary>
    public static bool IsValidMd5(string? s) => isHexOfLength(s, 32);

    /// <summary>
    /// Returns true if the string looks like a valid SHA256 hash (64 hex chars).
    /// </summary>
    public static bool IsValidSha256(string? s) => isHexOfLength(s, 64);

    /// <summary>
    /// Pick the best available hash: prefer md5 if valid, fall back to sha256.
    /// Returns null if neither is valid, lowercased otherwise.
    /// </summary>
    public static string? PickHash(string? md5, string? sha256)
    {
        if (IsValidMd5(md5)) return md5.ToLowerInvariant();
        if (IsValidSha256(sha256)) return sha256.ToLowerInvariant();
        return null;
    }

    /// <summary>
    /// Parse a header JSON string. Returns null if required fields are missing.
    /// </summary>
    public RawTableData? ParseHeader(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var symbol = root.TryGetProperty("symbol", out var s) ? s.GetString() : null;
        var dataUrl = root.TryGetProperty("data_url", out var d) ? d.GetString() : null;

        string[]? levelOrder = null;
        if (root.TryGetProperty("level_order", out var lo) && lo.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in lo.EnumerateArray())
                list.Add(item.GetString() ?? string.Empty);
            levelOrder = list.ToArray();
        }

        return new RawTableData
        {
            Name = name,
            Symbol = symbol,
            DataUrl = dataUrl,
            LevelOrder = levelOrder,
        };
    }

    /// <summary>
    /// Parse a data JSON string (either a plain array or wrapped in { "charts": [...] }).
    /// </summary>
    public List<RawChartItem>? ParseData(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

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

    /// <summary>
    /// Merge header + charts into a DifficultyTable.
    /// header and/or charts may come from the same file (combined JSON).
    /// </summary>
    public DifficultyTable? Merge(string sourcePath, TableSource source, RawTableData? header, List<RawChartItem>? charts)
    {
        var name = header?.Name;
        var symbol = header?.Symbol;

        // If no header, try to infer name/symbol from source path.
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
}
