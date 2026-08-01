using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Result of an import operation.
/// </summary>
public record ImportResult(DifficultyTable Table);

public partial class DifficultyTableStore
{
    public IReadOnlyList<DifficultyTable> Tables => tables;

    internal CollectionSyncManager? CollectionSyncManager { get; }

    /// <summary>
    /// Optional; set after construction to enable automatic marker refresh when tables change.
    /// </summary>
    public DifficultyNameUpdater? DifficultyNameUpdater { get; set; }

    private static readonly HttpClient http_client = new();

    private readonly List<DifficultyTable> tables = [];

    private readonly BmsRulesetConfigManager? config;
    private readonly string cacheDirectory;

    private readonly Dictionary<string, List<(DifficultyTable table, TableEntry entry)>> md5Index
        = new(StringComparer.OrdinalIgnoreCase);

    public DifficultyTableStore(
        BmsRulesetConfigManager? config, string cacheDirectory,
        CollectionSyncManager? syncManager = null,
        RealmAccess? realm = null)
    {
        this.config = config;
        this.cacheDirectory = cacheDirectory;
        CollectionSyncManager = syncManager;
        Directory.CreateDirectory(cacheDirectory);
        RefreshDiffNameEvent += notification => Task.Factory.StartNew(() =>
        {
            DifficultyNameUpdater?.RefreshAllMarkers(notification);
        }, TaskCreationOptions.LongRunning);

        TableListRebuildEvent += tb => CollectionSyncManager?.SyncInTransaction(realm, tb);
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
    public async Task<ImportResult?> ImportAsync(string source, ProgressNotification notification)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        notification.Progress = 0f;

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
                    notification.CompletionText = BmsStrings.MissingBmstableMeta(source);
                    notification.State = ProgressNotificationState.Cancelled;
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
            if (!File.Exists(source))
            {
                notification.CompletionText = BmsStrings.FileDoesNotExist;
                notification.State = ProgressNotificationState.Cancelled;
                return null;
            }

            effectiveSource = source;
            json = await File.ReadAllTextAsync(source).ConfigureAwait(false);
        }

        notification.Progress = 0.5f;

        var parsed = BmsTableJsonParser.Parse(json);
        if (parsed == null)
        {
            notification.CompletionText = BmsStrings.FailedToParseTable(source);
            notification.State = ProgressNotificationState.Cancelled;
            return null;
        }

        // If header has a separate data_url but no embedded charts, fetch data.
        var charts = parsed.Charts;
        var headerDataUrl = parsed.Header?.DataUrl;
        if (charts == null && headerDataUrl != null && isUrl)
        {
            var dataUrl = headerDataUrl.StartsWith("http", StringComparison.Ordinal)
                ? headerDataUrl
                : new Uri(new Uri(effectiveSource), headerDataUrl).ToString();

            BmsLogger.Log($"Fetching chart data from {dataUrl}");
            var dataJson = await http_client.GetStringAsync(dataUrl).ConfigureAwait(false);

            // Cache data separately so startup can restore without re-downloading.
            var dataCachePath = cacheFileForUrl(dataUrl);
            await File.WriteAllTextAsync(dataCachePath, dataJson).ConfigureAwait(false);

            var dataParsed = BmsTableJsonParser.Parse(dataJson);
            charts = dataParsed?.Charts;
        }

        // ── Stage 2: Merge + Index ──
        var tableSource = isUrl ? TableSource.RemoteUrl : TableSource.LocalFile;
        var table = BmsTableJsonParser.Merge(effectiveSource, tableSource, parsed.Header, charts);

        if (table == null)
        {
            notification.CompletionText = BmsStrings.NoValidTableEntries(source);
            notification.State = ProgressNotificationState.Cancelled;
            return null;
        }

        AddTable(table, notification);
        notification.Progress = 1;
        return new ImportResult(table);
    }

    public void NotifyToRebuildTableList(DifficultyTable? tableRemoved)
    {
        TableListRebuildEvent?.Invoke(tableRemoved);
    }

    public void NotifyToRefreshAllDiffNames(ProgressNotification? notification = null)
    {
        RefreshDiffNameEvent?.Invoke(notification);
    }

    #region Persistence

    private void persistTableList()
    {
        if (config == null) return;

        var sources = string.Join(";", tables.Select(t => t.SourcePath));
        config.SetValue(BmsRulesetSetting.DifficultyTableSources, sources);
    }

    #endregion

    /// <summary>
    /// Fires when a table is added or removed, so the table list in settings can rebuild.
    /// </summary>
    public event Action<DifficultyTable?>? TableListRebuildEvent;

    public event Action<ProgressNotification?>? RefreshDiffNameEvent;


    #region Public CRUD

    /// <summary>
    /// Add a newly-imported table. Fires events for UI updates and triggers marker refresh.
    /// </summary>
    public void AddTable(DifficultyTable table, ProgressNotification? notification = null)
    {
        tables.Add(table);
        NotifyToRebuildTableList(null);
        addToIndex(table);
        NotifyToRefreshAllDiffNames(notification);
        persistTableList();
    }

    /// <summary>
    /// Restore a table at startup. No events fired — markers and collections are in Realm already.
    /// </summary>
    public void RestoreTable(DifficultyTable table)
    {
        // var entries = new List<TableEntry>();
        // DifficultyTable[] dts =
        // [
        //     new() { Name = "A", Symbol = "#", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.RemoteUrl, SourcePath = "http://x/a", Entries = entries },
        //     new() { Name = "Normal Table", Symbol = "★", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.RemoteUrl, SourcePath = "http://x/n", Entries = entries },
        //     new() { Name = "Turbow Difficulty Table (2024 Edition)", Symbol = "TT", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.RemoteUrl, SourcePath = "http://x/t", Entries = entries },
        //     new() { Name = "超絶技巧的難易度表: The Ultimate BMS Difficulty Reference Collection", Symbol = "ULT", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.RemoteUrl, SourcePath = "http://x/u", Entries = entries },
        //     new() { Name = "ThisIsAVeryLongTableNameWithoutAnySpacesThatShouldWrapAtTheContainerEdge", Symbol = "LNG", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.LocalFile, SourcePath = "C:\\t\\l.json", Entries = entries },
        //     new() { Name = "Short last line — this name wraps around but ends early", Symbol = "END", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.LocalFile, SourcePath = "C:\\t\\e.json", Entries = entries },
        //     new() { Name = "This table name wraps perfectly to fill nearly the entire available container width", Symbol = "FULL", LevelOrder = ["★1", "★2", "★3"], Source = TableSource.LocalFile, SourcePath = "C:\\t\\f.json", Entries = entries },
        // ];
        // foreach (var dt in dts)
        //     tables.Add(dt);
        tables.Add(table);
        addToIndex(table);
    }

    /// <summary>
    /// Remove a table and trigger cleanup events, including marker refresh.
    /// </summary>
    public void RemoveTable(DifficultyTable table, ProgressNotification? notification = null)
    {
        tables.Remove(table);
        NotifyToRebuildTableList(table);
        removeFromIndex(table);
        NotifyToRefreshAllDiffNames(notification);
        persistTableList();
    }

    /// <summary>
    /// Replace an existing table with updated data. Removes the old entries from the index,
    /// inserts the new table at the same position, and fires all events including marker refresh.
    /// Handles the case where newTable was already added via ImportAsync -> AddTable.
    /// </summary>
    public void ReplaceTable(DifficultyTable oldTable, DifficultyTable newTable, ProgressNotification? notification = null)
    {
        var oldIndex = tables.IndexOf(oldTable);
        if (oldIndex < 0) return;

        tables.RemoveAt(oldIndex);
        removeFromIndex(oldTable);

        // newTable may already be in the list (added by ImportAsync -> AddTable).
        var newIndex = tables.IndexOf(newTable);
        if (newIndex >= 0)
        {
            // Move it to the old position.
            tables.RemoveAt(newIndex);
            var insertIndex = newIndex < oldIndex ? oldIndex - 1 : oldIndex;
            if (insertIndex >= tables.Count)
                tables.Add(newTable);
            else
                tables.Insert(insertIndex, newTable);
        }
        else
        {
            if (oldIndex >= tables.Count)
                tables.Add(newTable);
            else
                tables.Insert(oldIndex, newTable);
            addToIndex(newTable);
        }

        NotifyToRebuildTableList(null);
        NotifyToRefreshAllDiffNames(notification);
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

            // If the cached header has a data_url, try to load cached data separately.
            var charts = parsed.Charts;
            if (charts == null && parsed.Header?.DataUrl != null)
            {
                var dataUrl = parsed.Header.DataUrl.StartsWith("http", StringComparison.Ordinal)
                    ? parsed.Header.DataUrl
                    : new Uri(new Uri(sourcePath), parsed.Header.DataUrl).ToString();

                var dataCachePath = cacheFileForUrl(dataUrl);
                if (File.Exists(dataCachePath))
                {
                    var dataJson = File.ReadAllText(dataCachePath);
                    var dataParsed = BmsTableJsonParser.Parse(dataJson);
                    charts = dataParsed?.Charts;
                }
            }

            var source = sourcePath.StartsWith("http", StringComparison.Ordinal)
                ? TableSource.RemoteUrl
                : TableSource.LocalFile;

            return BmsTableJsonParser.Merge(sourcePath, source, parsed.Header, charts);
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, $"Failed to read cached difficulty table {cachePath}: {e.Message}");
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
