using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.ImportExport;

public partial class BmsFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null) : ICanAcceptFiles
{
    /// <summary>
    /// Fired after a beatmap set is successfully imported, to trigger difficulty recalculation.
    /// A <see cref="Live{T}"/> reference is passed (rather than the thread-confined realm object) so the
    /// handler can process the set on a background thread without blocking the import write loop.
    /// </summary>
    public Action<Live<BeatmapSetInfo>, MetadataLookupScope>? OnImportCompleted { get; init; }

    public IEnumerable<string> HandledExtensions => Constant.BMS_EXTENSIONS;

    public Task Import(params string[] paths)
    {
        if (paths.Length == 0)
            return Task.CompletedTask;

        var notification = new ProgressNotification
        {
            Text = "BMS import is initialising...",
            State = ProgressNotificationState.Active,
        };

        notifications?.Post(notification);

        return Task.Run(() =>
        {
            try
            {
                var fileStore = new RealmFileStore(realm, storage);

                // Phase 1 (fast): discover and group chart files. Paths only — no reading or hashing.
                notification.Text = "BMS import: scanning files...";
                var groups = discoverChartGroups(paths);

                if (groups.Length == 0)
                {
                    notification.CompletionText = "No BMS charts found to import.";
                    notification.State = ProgressNotificationState.Cancelled;
                    return;
                }

                // Phase 2 (heavy, parallel, with progress): read + parse + hash every chart.
                // This is the work that previously hid behind a silent "initialising..." wall.
                var importSets = parseImportSets(groups, notification);

                // Phase 3: realm write loop.
                var imported = realm.Run(r =>
                {
                    // Look up the ruleset once for the whole batch — it cannot change mid-import.
                    var rulesetInfo = r.Find<RulesetInfo>("bms");
                    if (rulesetInfo?.Available != true)
                    {
                        Logger.Log("BMS import: ruleset 'bms' is not available in realm");
                        return 0;
                    }

                    var existingBeatmaps = getExistingBeatmapsByMd5(r, importSets);
                    var count = 0;

                    for (var i = 0; i < importSets.Length; i++)
                    {
                        var set = importSet(r, importSets[i], fileStore, rulesetInfo, existingBeatmaps);
                        if (set != null)
                        {
                            count++;

                            try
                            {
                                // Hand off a thread-safe Live reference so difficulty processing can run
                                // on a background scheduler instead of blocking this realm write loop.
                                OnImportCompleted?.Invoke(set.ToLive(realm), MetadataLookupScope.None);
                            }
                            catch (Exception inner)
                            {
                                Logger.Log($"BMS import: failed to process beatmap set: {inner.Message}");
                            }
                        }

                        notification.Text = $"Imported {count} of {importSets.Length} BMS sets";
                        notification.Progress = 0.5f + (float)(i + 1) / importSets.Length * 0.5f;
                    }

                    return count;
                });

                if (imported == 0)
                {
                    notification.CompletionText = "BMS import failed! Check logs for more information.";
                    notification.State = ProgressNotificationState.Cancelled;
                }
                else
                {
                    notification.CompletionText = imported == importSets.Length
                        ? $"Imported {imported} BMS sets!"
                        : $"Imported {imported} of {importSets.Length} BMS sets.";
                    notification.State = ProgressNotificationState.Completed;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"BMS import: scan failed: {e.Message}");
                Logger.Log(e.ToString());
                notification.CompletionText = "BMS import failed! Check logs for more information.";
                notification.State = ProgressNotificationState.Cancelled;
            }
        });
    }

    public Task Import(ImportTask[] tasks, ImportParameters parameters = default)
        => Import(tasks.Select(t => t.Path).ToArray());

    public void DeleteAllBmsFilesAsync()
    {
        var notification = new ProgressNotification
        {
            Text = "Deleting BMS beatmaps...",
            State = ProgressNotificationState.Active,
        };

        notifications?.Post(notification);

        Task.Run(() =>
        {
            try
            {
                realm.Run(r =>
                {
                    var bmsSets = r.All<BeatmapSetInfo>()
                        .Where(s => !s.DeletePending && !s.Protected)
                        .AsEnumerable()
                        .Where(s => s.Beatmaps.Any(b => b.Ruleset.ShortName == "bms"))
                        .ToList();

                    if (bmsSets.Count == 0)
                    {
                        notification.CompletionText = "No BMS beatmaps found to delete!";
                        notification.State = ProgressNotificationState.Completed;
                        return;
                    }

                    notification.Text = $"Deleting {bmsSets.Count} BMS beatmaps...";
                    notification.Progress = 0;

                    r.Write(() =>
                    {
                        for (var i = 0; i < bmsSets.Count; i++)
                        {
                            bmsSets[i].DeletePending = true;
                            notification.Progress = (float)(i + 1) / bmsSets.Count;
                        }
                    });
                });

                notification.CompletionText = "Deleted all BMS beatmaps!";
                notification.State = ProgressNotificationState.Completed;
            }
            catch (Exception e)
            {
                Logger.Log($"BMS delete failed: {e.Message}");
                notification.Text = $"BMS delete failed: {e.Message}";
                notification.State = ProgressNotificationState.Cancelled;
            }
        });
    }

    /// <summary>
    /// Fast discovery pass: walk the input paths, collect chart files, and group them by directory.
    /// Performs no file reading or hashing, so it returns quickly and yields an accurate total count.
    /// </summary>
    private static ImportGroup[] discoverChartGroups(IEnumerable<string> paths)
    {
        var chartPaths = paths.AsParallel()
            .SelectMany(expandPathToCharts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return chartPaths
            .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new ImportGroup(g.Key!, g.ToArray()))
            .ToArray();
    }

    /// <summary>
    /// Heavy parse pass: reads, decodes and hashes every chart in parallel while reporting progress.
    /// Resource bytes are intentionally not read here — only resolved paths are recorded.
    /// </summary>
    private static ImportSet[] parseImportSets(ImportGroup[] groups, ProgressNotification notification)
    {
        var totalCharts = groups.Sum(g => g.ChartPaths.Length);
        var parsed = 0;

        // Flatten to (directory, chartPath) pairs so parsing is parallelised across all charts rather
        // than only across sets — otherwise a single folder with many difficulties would parse on one core.
        return groups
            .SelectMany(g => g.ChartPaths.Select(p => (g.Directory, Path: p)))
            .AsParallel()
            .Select(x =>
            {
                var chart = new ChartImport(x.Directory, x.Path);

                var done = Interlocked.Increment(ref parsed);

                // Throttle UI updates so we don't spam the notification for every file.
                if (done % 8 == 0 || done == totalCharts)
                {
                    notification.Text = $"BMS import: reading charts ({done}/{totalCharts})";
                    notification.Progress = (float)done / totalCharts * 0.5f;
                }

                return (x.Directory, Chart: chart);
            })
            .GroupBy(x => x.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(grp => new ImportSet(
                grp.Key,
                grp.Select(x => x.Chart)
                    .OrderBy(c => Path.GetFileName(c.Path), StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
    }

    private static IEnumerable<string> expandPathToCharts(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(Constant.IsChartFile);
        }

        return File.Exists(path) && Constant.IsChartFile(path) ? [path] : [];
    }

    private static string? findExistingResourceWithAnyExtension(string pathWithoutExtension)
    {
        var directory = Path.GetDirectoryName(pathWithoutExtension);
        var fileName = Path.GetFileName(pathWithoutExtension);

        if (directory == null || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, fileName + ".*", SearchOption.TopDirectoryOnly).FirstOrDefault();
    }

    private static RealmFile addFileUsage(BeatmapSetInfo beatmapSetInfo, Dictionary<string, RealmFile> addedFiles, string path, byte[] content, RealmFileStore fileStore, Realm realm)
    {
        var fileName = Path.GetFileName(path);

        // Avoid re-adding a file already registered under the same name within this set.
        if (addedFiles.TryGetValue(fileName, out var existingFile))
            return existingFile;

        // Content is already in memory. We wrap it in a FileStream subclass that serves reads from
        // that buffer (so Add's hash read never touches disk) while still exposing the real file path
        // via FileStream.Name, allowing RealmFileStore.Add to hard-link instead of copying the bytes.
        RealmFile realmFile;
        using (var stream = new MemoryBackedFileStream(path, content))
            realmFile = fileStore.Add(stream, realm, preferHardLinks: true);

        beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFile, fileName));
        addedFiles[fileName] = realmFile;
        return realmFile;
    }

    /// <summary>
    /// Reads all resource files into memory in parallel, scoped to the charts actually being imported.
    /// Done outside the realm write transaction so the synchronous <see cref="RealmFileStore.Add"/>
    /// calls inside the transaction only hash from memory rather than waiting on serial disk I/O.
    /// </summary>
    private static (string Path, byte[] Content)[] readResources(IEnumerable<ChartImport> chartsToImport)
        => chartsToImport
            .SelectMany(c => c.ResourcePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .AsParallel()
            .Select(path => (Path: path, Content: File.ReadAllBytes(path)))
            .ToArray();

    private static bool isBrokenLegacyBmsImport(BeatmapInfo beatmap) =>
        beatmap.Ruleset.ShortName == "bms" && beatmap.BeatmapSet != null && beatmap.File == null;

    private static Dictionary<string, List<BeatmapInfo>> getExistingBeatmapsByMd5(Realm realm, ImportSet[] importSets)
    {
        var chartHashes = importSets
            .SelectMany(s => s.ChartImports)
            .Select(c => c.Md5Hash)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (chartHashes.Count == 0)
            return new Dictionary<string, List<BeatmapInfo>>(StringComparer.OrdinalIgnoreCase);

        const int max_hashes_per_query = 200;

        var beatmaps = chartHashes
            .Chunk(max_hashes_per_query)
            .SelectMany(batch =>
            {
                var clauses = batch.Select(h => $"MD5Hash == '{h.Replace("'", "''")}'");
                var filterString = string.Join(" OR ", clauses);
                return realm.All<BeatmapInfo>().Filter(filterString).ToArray();
            })
            .ToArray();

        var beatmapsByMd5 = chartHashes.ToDictionary(h => h, _ => new List<BeatmapInfo>(), StringComparer.OrdinalIgnoreCase);

        foreach (var beatmap in beatmaps)
        {
            if (beatmapsByMd5.TryGetValue(beatmap.MD5Hash, out var list))
                list.Add(beatmap);
        }

        return beatmapsByMd5;
    }

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) => string.Join(string.Empty, beatmapSetInfo.Beatmaps
        .Select(b => b.MD5Hash)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    private static BeatmapSetInfo? importSet(Realm r, ImportSet importSet, RealmFileStore fileStore, RulesetInfo rulesetInfo, IReadOnlyDictionary<string, List<BeatmapInfo>> existingBeatmapsByMd5)
    {
        try
        {
            if (importSet.ChartImports.Length == 0)
                return null;

            var chartHashes = importSet.ChartImports.Select(c => c.Md5Hash).ToArray();

            var existingBeatmaps = chartHashes
                .SelectMany(h => existingBeatmapsByMd5.TryGetValue(h, out var list) ? list : [])
                .Distinct()
                .ToArray();

            var duplicateHashes = existingBeatmaps
                .Where(b => b.BeatmapSet is { DeletePending: false })
                .Select(b => b.MD5Hash)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var brokenLegacyBeatmaps = existingBeatmaps
                .Where(isBrokenLegacyBmsImport)
                .ToArray();

            var chartsToImport = brokenLegacyBeatmaps.Length > 0
                ? importSet.ChartImports
                : importSet.ChartImports.Where(c => !duplicateHashes.Contains(c.Md5Hash)).ToArray();

            if (chartsToImport.Length == 0)
            {
                Logger.Log($"BMS import: skipping duplicate set {Path.GetFileName(importSet.Directory)}");
                return null;
            }

            // Read all resource bytes into memory in parallel, outside the write transaction, so the
            // synchronous Add calls below only hash from memory instead of serialising disk reads.
            var resourceContents = readResources(chartsToImport);

            using var transaction = r.BeginWrite();
            var addedFiles = new Dictionary<string, RealmFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var brokenLegacy in brokenLegacyBeatmaps)
            {
                Logger.Log($"BMS import: replacing legacy broken set {Path.GetFileName(importSet.Directory)}");
                brokenLegacy.BeatmapSet!.DeletePending = true;
            }

            var beatmapSetInfo = new BeatmapSetInfo
            {
                OnlineID = -1,
                DateAdded = DateTimeOffset.UtcNow,
            };

            foreach (var chart in chartsToImport)
            {
                var metadata = new BeatmapMetadata
                {
                    Title = chart.Metadata.SetTitle,
                    Artist = chart.Metadata.Artist,
                    Author = new RealmUser { Username = Constant.AUTHOR },
                };

                var chartFile = addFileUsage(beatmapSetInfo, addedFiles, chart.Path, chart.Content, fileStore, r);

                var beatmapInfo = new BeatmapInfo
                {
                    DifficultyName = chart.Metadata.DifficultyName,
                    Ruleset = rulesetInfo,
                    Metadata = metadata,
                    Difficulty = new BeatmapDifficulty
                    {
                        CircleSize = chart.Metadata.KeyCount,
                    },
                    Hash = chartFile.Hash,
                    MD5Hash = chart.Md5Hash,
                };

                beatmapSetInfo.Beatmaps.Add(beatmapInfo);
                beatmapInfo.BeatmapSet = beatmapSetInfo;
            }

            foreach (var (path, content) in resourceContents)
                addFileUsage(beatmapSetInfo, addedFiles, path, content, fileStore, r);

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            Logger.Log($"BMS import: imported {Path.GetFileName(importSet.Directory)} ({chartsToImport.Length} charts, {resourceContents.Length} resources)");
            return beatmapSetInfo;
        }
        catch (Exception e)
        {
            Logger.Log($"BMS import: failed to import {importSet.Directory}: {e.Message}");
            return null;
        }
    }

    private static string resolveResourcePath(string directory, string resource)
    {
        var path = Path.Combine(directory, resource);

        if (File.Exists(path))
            return path;

        var pathWithoutExtension = Path.Combine(directory, Path.ChangeExtension(resource, null));

        return findExistingResourceWithAnyExtension(pathWithoutExtension) ?? path;
    }

    private sealed record ImportGroup(
        string Directory,
        string[] ChartPaths);

    private sealed record ImportSet(
        string Directory,
        ChartImport[] ChartImports);

    private sealed class ChartImport
    {
        public string Path { get; }

        public byte[] Content { get; }

        public string Md5Hash { get; }

        public IReadOnlyList<string> ResourcePaths { get; }

        public BmsChartMetadata Metadata { get; }

        public ChartImport(string directory, string path)
        {
            Path = path;
            Content = File.ReadAllBytes(path);

            // Decode + split once, then reuse for both metadata and resource scanning.
            var lines = BmsChartParser.ReadAllLines(Content);

            Md5Hash = Convert.ToHexString(MD5.HashData(Content)).ToLowerInvariant();
            Metadata = BmsChartParser.ScanMetadata(lines, path);

            // Resolve resource paths only; bytes are read later, scoped to charts actually imported.
            ResourcePaths = BmsChartParser.ScanResourceReferences(lines)
                .Select(resource => resolveResourcePath(directory, resource))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>
    /// A <see cref="FileStream"/> whose contents are served from an in-memory buffer rather than disk,
    /// while still reporting the real file path through <see cref="FileStream.Name"/>.
    /// <para>
    /// This lets <see cref="RealmFileStore.Add"/> take its hard-link fast path (which requires the stream
    /// to be a <see cref="FileStream"/> and reads <c>Name</c>) without re-reading the file from disk for
    /// hashing — the bytes were already read in parallel before the write transaction.
    /// </para>
    /// </summary>
    private sealed class MemoryBackedFileStream(string path, byte[] content)
        : FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1)
    {
        private int position;

        // Open the real handle (needed so Name/hard-linking work), but with a tiny buffer since we
        // never actually read through the base stream.

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = content.Length - position;
            if (remaining <= 0)
                return 0;

            var toCopy = Math.Min(remaining, count);
            Array.Copy(content, position, buffer, offset, toCopy);
            position += toCopy;
            return toCopy;
        }

        public override int Read(Span<byte> buffer)
        {
            var remaining = content.Length - position;
            if (remaining <= 0)
                return 0;

            var toCopy = Math.Min(remaining, buffer.Length);
            content.AsSpan(position, toCopy).CopyTo(buffer);
            position += toCopy;
            return toCopy;
        }

        public override int ReadByte()
            => position < content.Length ? content[position++] : -1;

        public override long Seek(long offset, SeekOrigin origin)
        {
            position = origin switch
            {
                SeekOrigin.Begin => (int)offset,
                SeekOrigin.Current => position + (int)offset,
                SeekOrigin.End => content.Length + (int)offset,
                _ => position,
            };
            return position;
        }

        public override void Flush()
        {
            // No-op: this stream is read-only and never touches the underlying handle for writes.
        }
    }
}
