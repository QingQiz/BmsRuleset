using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Result of an import operation.
/// </summary>
public record ImportResult(DifficultyTable Table);

public partial class DifficultyTableStore
{

    public IReadOnlyList<DifficultyTable> Tables => tables;

    private static readonly HttpClient http_client = new();

    private readonly List<DifficultyTable> tables = [];

    private readonly BmsRulesetConfigManager? config;
    private readonly string cacheDirectory;
    private readonly CollectionSyncManager? syncManager;
    private readonly RealmAccess? realm;

    private readonly Dictionary<string, List<(DifficultyTable table, TableEntry entry)>> md5Index
        = new(StringComparer.OrdinalIgnoreCase);

    public DifficultyTableStore(
        BmsRulesetConfigManager? config, string cacheDirectory,
        CollectionSyncManager? syncManager = null,
        RealmAccess? realm = null)
    {
        this.config = config;
        this.cacheDirectory = cacheDirectory;
        this.syncManager = syncManager;
        this.realm = realm;
        Directory.CreateDirectory(cacheDirectory);
    }

    /// <summary>
    /// Reload all persisted table sources into memory index only.
    /// Does NOT trigger TableLoaded / TableRemoved.
    /// Remote URLs read from local cache if available; no HTTP unless cache is missing.
    /// </summary>
    public void LoadPersistedTables()
    {
        tables.Clear();
        md5Index.Clear();

        var sources = config?.Get<string>(BmsRulesetSetting.DifficultyTableSources) ?? string.Empty;
        if (string.IsNullOrEmpty(sources)) return;

        foreach (var source in sources.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrEmpty(source)) continue;

            DifficultyTable? table = null;

            if (source.StartsWith("http://", StringComparison.Ordinal) || source.StartsWith("https://", StringComparison.Ordinal))
            {
                // Restore from cache if present; no HTTP at startup
                var cachePath = cacheFileForUrl(source);
                if (File.Exists(cachePath))
                    table = loadFromCache(cachePath, source);
            }
            else if (File.Exists(source))
            {
                table = loadFromCache(source, source);
            }

            if (table != null)
                RestoreTable(table);
        }
    }

    /// <summary>
    /// User-initiated import: download/read, parse, index, update markers + collections.
    /// </summary>
    public async Task<ImportResult?> ImportAsync(string source, IProgress<float>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        progress?.Report(0f);

        // ── Stage 1: Read + Parse ──
        string json;
        string effectiveSource;

        var isUrl = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        if (isUrl)
        {
            effectiveSource = source;
            var body = await http_client.GetStringAsync(source).ConfigureAwait(false);

            var trimmed = body.TrimStart();
            if (trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '['))
            {
                json = body;
            }
            else
            {
                // HTML — look for bmstable meta tag
                var jsonUrl = resolveUrlFromHtml(body, source);
                if (jsonUrl == null)
                {
                    Logger.Log($"Page at {source} does not contain a bmstable meta tag");
                    return null;
                }

                json = await http_client.GetStringAsync(jsonUrl).ConfigureAwait(false);
            }

            // Write to cache
            var cachePath = cacheFileForUrl(source);
            await File.WriteAllTextAsync(cachePath, json).ConfigureAwait(false);
        }
        else
        {
            if (!File.Exists(source)) return null;

            effectiveSource = source;
            json = await File.ReadAllTextAsync(source).ConfigureAwait(false);
        }

        progress?.Report(0.05f);

        var parsed = BmsTableJsonParser.Parse(json);
        if (parsed == null)
        {
            Logger.Log($"Failed to parse difficulty table from {source}");
            return null;
        }

        // ── Stage 2: Merge + Index ──
        var tableSource = isUrl ? TableSource.RemoteUrl : TableSource.LocalFile;
        var table = BmsTableJsonParser.Merge(effectiveSource, tableSource, parsed.Header, parsed.Charts);

        if (table == null)
        {
            Logger.Log($"No valid entries found in difficulty table from {source}");
            return null;
        }

        AddTable(table);
        progress?.Report(0.1f);

        // ── Stage 3: Apply (single realm.Write) ──
        realm?.Write(r =>
        {
            applyMarkersInTransaction(r, table);
            syncManager?.SyncInTransaction(r, table);
            progress?.Report(0.9f);
        });

        progress?.Report(1.0f);
        return new ImportResult(table);
    }

    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();

    private void applyMarkersInTransaction(Realm r, DifficultyTable table)
    {
        foreach (var entry in table.Entries)
        {
            var beatmap = r.All<BeatmapInfo>()
                .Filter("Ruleset.ShortName == 'bms' AND MD5Hash == $0", entry.Md5Hash)
                .FirstOrDefault();

            if (beatmap == null) continue;

            var clean = markerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);
            var markers = GetMarkers(beatmap.MD5Hash);

            if (markers.Count == 0)
                beatmap.DifficultyName = clean;
            else
            {
                var markerStr = string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
                beatmap.DifficultyName = $"{clean} [{markerStr}]";
            }
        }
    }

    #region Persistence

    private void persistTableList()
    {
        if (config == null) return;

        var sources = string.Join(";", tables.Select(t => t.SourcePath));
        config.SetValue(BmsRulesetSetting.DifficultyTableSources, sources);
    }

    #endregion

    public event Action<DifficultyTable>? TableRemoved;

    public event Action? TablesChanged;

    #region Public CRUD

    /// <summary>
    /// Add a newly-imported table. Fires events for UI updates.
    /// </summary>
    public void AddTable(DifficultyTable table)
    {
        tables.Add(table);
        addToIndex(table);
        TablesChanged?.Invoke();
        persistTableList();
    }

    /// <summary>
    /// Restore a table at startup. No events fired — markers and collections are in Realm already.
    /// </summary>
    public void RestoreTable(DifficultyTable table)
    {
        tables.Add(table);
        addToIndex(table);
        // No events
    }

    /// <summary>
    /// Remove a table and trigger cleanup events.
    /// </summary>
    public void RemoveTable(DifficultyTable table)
    {
        tables.Remove(table);
        removeFromIndex(table);
        TableRemoved?.Invoke(table);
        TablesChanged?.Invoke();
        persistTableList();
    }

    public List<(DifficultyTable table, TableEntry entry)> GetMarkers(string md5Hash)
        => md5Index.TryGetValue(md5Hash, out var markers)
            ? markers
            : [];

    #endregion

    #region Index management

    private void addToIndex(DifficultyTable table)
    {
        foreach (var entry in table.Entries)
        {
            if (!md5Index.TryGetValue(entry.Md5Hash, out var list))
                md5Index[entry.Md5Hash] = list = [];
            list.Add((table, entry));
        }
    }

    private void removeFromIndex(DifficultyTable table)
    {
        foreach (var entry in table.Entries)
        {
            if (md5Index.TryGetValue(entry.Md5Hash, out var list))
            {
                list.RemoveAll(t => t.table == table);
                if (list.Count == 0)
                    md5Index.Remove(entry.Md5Hash);
            }
        }
    }

    #endregion

    #region Cache

    private string cacheFileForUrl(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(cacheDirectory, $"{hash}.json");
    }

    private DifficultyTable? loadFromCache(string cachePath, string sourcePath)
    {
        try
        {
            var json = File.ReadAllText(cachePath);
            var parsed = BmsTableJsonParser.Parse(json);
            if (parsed == null) return null;

            var source = sourcePath.StartsWith("http", StringComparison.Ordinal)
                ? TableSource.RemoteUrl
                : TableSource.LocalFile;

            return BmsTableJsonParser.Merge(sourcePath, source, parsed.Header, parsed.Charts);
        }
        catch (Exception e)
        {
            Logger.Log($"Failed to read cached difficulty table {cachePath}: {e.Message}");
            return null;
        }
    }

    #endregion

    #region HTML parsing (unchanged)

    private static string? resolveUrlFromHtml(string html, string pageUrl)
    {
        var content = extractMetaContent(html, "bmstable")
                      ?? extractMetaContent(html, "bmstable-alt");

        if (string.IsNullOrWhiteSpace(content)) return null;

        if (content.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            content.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return content;

        return new Uri(new Uri(pageUrl), content).ToString();
    }

    private static string? extractMetaContent(string html, string name)
    {
        var pattern = $"<meta name=\"{name}\" content=\"";
        var start = html.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += pattern.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html[start..end];
    }

    #endregion

}
