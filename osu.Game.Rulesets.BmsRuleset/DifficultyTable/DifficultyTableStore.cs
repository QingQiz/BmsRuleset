using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public class DifficultyTableStore
{
    public event Action<DifficultyTable>? TableLoaded;

    public event Action<DifficultyTable>? TableRemoved;

    public event Action? TablesChanged;

    public IReadOnlyList<DifficultyTable> Tables => tables;

    private readonly List<DifficultyTable> tables = [];

    /// <summary>
    /// Fire <see cref="TablesChanged"/> so the UI rebuilds (used after subdivision toggle).
    /// </summary>
    public void NotifyTablesChanged() => TablesChanged?.Invoke();

    /// <summary>
    /// MD5 → list of (table, entry) for O(1) lookup.
    /// </summary>
    private Dictionary<string, List<(DifficultyTable table, TableEntry entry)>> md5Index = new(StringComparer.OrdinalIgnoreCase);

    private readonly BmsRulesetConfigManager? config;
    private readonly string cacheDirectory;

    /// <summary>
    /// Create the store. Call <see cref="LoadPersistedTables"/> after construction to reload persisted sources.
    /// </summary>
    public DifficultyTableStore(BmsRulesetConfigManager? config, string cacheDirectory)
    {
        this.config = config;
        this.cacheDirectory = cacheDirectory;
        Directory.CreateDirectory(cacheDirectory);
    }

    /// <summary>
    /// Reload all tables from persisted source list.
    /// </summary>
    public void LoadPersistedTables()
    {
        tables.Clear();
        rebuildIndex();

        var sources = config?.Get<string>(BmsRulesetSetting.DifficultyTableSources) ?? string.Empty;
        if (!string.IsNullOrEmpty(sources))
        {
            foreach (var source in sources.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrEmpty(source))
                    loadFromSource(source);
            }
        }
    }

    /// <summary>
    /// Load a table from a local JSON file path.
    /// Supports combined JSON (header+data in one file) and separate files (header.json + data.json).
    /// </summary>
    public async Task<DifficultyTable?> LoadFromFileAsync(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var parser = new BmsTableJsonParser();

            var header = parser.ParseHeader(json);
            var charts = parser.ParseData(json);

            if (charts == null)
            {
                var dir = Path.GetDirectoryName(path) ?? ".";
                var dataPath = header?.DataUrl != null
                    ? Path.Combine(dir, Path.GetFileName(header.DataUrl))
                    : findDataFile(dir, path);

                if (dataPath != null && File.Exists(dataPath))
                {
                    var dataJson = await File.ReadAllTextAsync(dataPath).ConfigureAwait(false);
                    charts = parser.ParseData(dataJson);
                }
            }

            var table = parser.Merge(path, TableSource.LocalFile, header, charts);
            if (table != null)
                addTable(table);

            return table;
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to load difficulty table from {path}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Load a table from a URL. Downloads and caches locally.
    /// Detects HTML vs JSON by inspecting the response body content,
    /// not by URL extension or Content-Type header.
    /// </summary>
    public async Task<DifficultyTable?> LoadFromUrlAsync(string url)
    {
        try
        {
            using var client = new HttpClient();
            var body = await client.GetStringAsync(url).ConfigureAwait(false);

            // Content-based detection: JSON objects/arrays start with { or [.
            var trimmed = body.TrimStart();
            if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            {
                return await loadJsonContent(body, url, url, client).ConfigureAwait(false);
            }

            // Not JSON — treat as HTML and look for a bmstable meta tag.
            var jsonUrl = resolveUrlFromHtml(body, url);
            if (jsonUrl == null)
            {
                Logger.Log($"Page at {url} does not appear to be JSON and contains no bmstable meta tag");
                return null;
            }

            var json = await client.GetStringAsync(jsonUrl).ConfigureAwait(false);
            return await loadJsonContent(json, url, jsonUrl, client).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to load difficulty table from {url}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Process a JSON body: parse header/charts, fetch separate data if needed, cache, and register.
    /// </summary>
    private async Task<DifficultyTable?> loadJsonContent(string json, string cacheKey, string jsonUrl, HttpClient client)
    {
        var parser = new BmsTableJsonParser();

        var header = parser.ParseHeader(json);
        var charts = parser.ParseData(json);

        if (charts == null && header?.DataUrl != null)
        {
            var dataUrl = header.DataUrl.StartsWith("http", StringComparison.Ordinal)
                ? header.DataUrl
                : new Uri(new Uri(jsonUrl), header.DataUrl).ToString();
            var dataJson = await client.GetStringAsync(dataUrl).ConfigureAwait(false);
            charts = parser.ParseData(dataJson);
        }

        // Cache locally (keyed by original page URL, not the resolved JSON URL)
        var cachePath = cacheFileForUrl(cacheKey);
        await File.WriteAllTextAsync(cachePath, json).ConfigureAwait(false);

        var table = parser.Merge(cacheKey, TableSource.RemoteUrl, header, charts);
        if (table != null)
            addTable(table);

        return table;
    }

    /// <summary>
    /// Parse an HTML page for a <c>&lt;meta name="bmstable" content="URL"&gt;</c> tag
    /// and return the resolved JSON URL. Falls back to <c>bmstable-alt</c> if the primary is not found.
    /// </summary>
    private static string? resolveUrlFromHtml(string html, string pageUrl)
    {
        var content = extractMetaContent(html, "bmstable")
                      ?? extractMetaContent(html, "bmstable-alt");

        if (string.IsNullOrWhiteSpace(content))
            return null;

        if (content.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return content;

        return new Uri(new Uri(pageUrl), content).ToString();
    }

    /// <summary>
    /// Extract the <c>content</c> attribute value from <c>&lt;meta name="X" content="..."&gt;</c>.
    /// </summary>
    private static string? extractMetaContent(string html, string name)
    {
        var pattern = $"<meta name=\"{name}\" content=\"";
        var start = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += pattern.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html[start..end];
    }

    /// <summary>
    /// Remove a table and trigger cleanup.
    /// </summary>
    public void RemoveTable(DifficultyTable table)
    {
        tables.Remove(table);
        rebuildIndex();
        TableRemoved?.Invoke(table);
        TablesChanged?.Invoke();
        persistTableList();
    }

    /// <summary>
    /// Get all markers for a given MD5 hash.
    /// </summary>
    public List<(DifficultyTable table, TableEntry entry)> GetMarkers(string md5Hash)
        => md5Index.TryGetValue(md5Hash, out var markers)
            ? markers
            : [];

    private void addTable(DifficultyTable table)
    {
        tables.Add(table);
        rebuildIndex();
        TableLoaded?.Invoke(table);
        TablesChanged?.Invoke();
        persistTableList();
    }

    private void rebuildIndex()
    {
        var newIndex = new Dictionary<string, List<(DifficultyTable, TableEntry)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            foreach (var entry in table.Entries)
            {
                if (!newIndex.TryGetValue(entry.Md5Hash, out var list))
                    newIndex[entry.Md5Hash] = list = [];
                list.Add((table, entry));
            }
        }

        md5Index = newIndex;
    }

    private void loadFromSource(string source)
    {
        if (source.StartsWith("http://", StringComparison.Ordinal) || source.StartsWith("https://", StringComparison.Ordinal))
            _ = LoadFromUrlAsync(source);
        else if (File.Exists(source))
            _ = LoadFromFileAsync(source);
    }

    private void persistTableList()
    {
        if (config == null) return;

        var sources = string.Join(";", tables.Select(t => t.SourcePath));
        config.SetValue(BmsRulesetSetting.DifficultyTableSources, sources);
    }

    private static string? findDataFile(string dir, string headerPath)
    {
        var headerName = Path.GetFileNameWithoutExtension(headerPath);
        var candidates = new[] { "data.json", "charts.json", $"{headerName}_data.json" };
        foreach (var c in candidates)
        {
            var full = Path.Combine(dir, c);
            if (File.Exists(full)) return full;
        }

        return null;
    }

    private string cacheFileForUrl(string url)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.json");
    }
}
