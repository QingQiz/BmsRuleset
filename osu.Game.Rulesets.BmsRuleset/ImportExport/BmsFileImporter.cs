using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

public partial class BmsFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null, BeatmapManager? beatmaps = null) : ICanAcceptFiles
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

                notification.Text = "BMS import: preparing...";

                // Phase 2: bounded producer/consumer pipeline. Producers read + parse + hash directories on the
                // thread pool and push into a capacity-limited queue. A single consumer (running on the realm
                // thread) drains that queue, queries realm for dedup, and writes into the database. No global
                // arrays (metadata, index, or plan) are built — at most `maxConcurrency` directories' worth of
                // data are in memory at any time. Each producer builds its own per-directory file index on demand
                // and discards it when the read completes.
                var result = realm.Run(r =>
                {
                    var rulesetInfo = r.Find<RulesetInfo>("bms");
                    if (rulesetInfo?.Available != true)
                    {
                        Logger.Log("BMS import: ruleset 'bms' is not available in realm");
                        return new ImportResult(RulesetAvailable: false, TotalSets: groups.Length, Imported: 0, Processed: 0);
                    }

                    // Purposefully decoupled:
                    //   - poolCapacity bounds memory (at most N fully-read directories queued for the consumer).
                    //   - maxProducers controls disk I/O concurrency (N threads reading/parsing in parallel).
                    // Backpressure: if the consumer is slower than the producers, pool.Add blocks, memory stays bounded.
                    var maxProducers = Environment.ProcessorCount * 2;
                    var poolCapacity = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

                    var pool = new BlockingCollection<PreparedDirectory>(poolCapacity);

                    // Producer: read entire directories in parallel, one per worker.
                    // Chart bytes are parsed for metadata/identity and retained for writing;
                    // resource bytes are read in parallel per directory to saturate disk throughput.
                    var producer = Task.Run(() =>
                    {
                        try
                        {
                            Parallel.ForEach(groups, new ParallelOptions { MaxDegreeOfParallelism = maxProducers }, group =>
                            {
                                try
                                {
                                    var prepared = readPreparedDirectory(group);
                                    // ReSharper disable once AccessToDisposedClosure
                                    pool.Add(prepared);
                                }
                                catch (Exception e)
                                {
                                    Logger.Log($"BMS import: failed to read {Path.GetFileName(group.Directory)}: {e.Message}");
                                }
                            });
                        }
                        finally
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            pool.CompleteAdding();
                        }
                    });

                    // Consumer: per-directory dedup + realm write. Runs on the realm thread.
                    var imported = 0;
                    var processed = 0;

                    foreach (var prepared in pool.GetConsumingEnumerable())
                    {
                        var md5 = prepared.Charts.Select(c => c.Chart.Md5Hash).ToArray();
                        var existingByMd5 = getExistingBeatmapsByMd5(r, md5);

                        if (importPreparedDirectory(r, prepared, existingByMd5, fileStore, rulesetInfo))
                            imported++;

                        processed++;
                        notification.Text = $"Imported {imported} of {groups.Length} BMS sets";
                        notification.Progress = (float)processed / groups.Length;
                    }

                    producer.GetAwaiter().GetResult();
                    pool.Dispose();

                    return new ImportResult(RulesetAvailable: true, TotalSets: groups.Length, Imported: imported, Processed: processed);
                });

                applyCompletionState(notification, result);
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
        if (beatmaps == null)
        {
            Logger.Log("BMS delete: BeatmapManager is unavailable; cannot delete BMS beatmaps.");
            return;
        }

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

                    beatmaps.Delete(bmsSets);
                });
            }
            catch (Exception e)
            {
                Logger.Log($"BMS delete failed: {e.Message}");
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

    private static IEnumerable<string> expandPathToCharts(string path)
    {
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(Constant.IsChartFile);

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

    private static bool isBrokenLegacyBmsImport(BeatmapInfo beatmap) =>
        beatmap.Ruleset.ShortName == "bms" && beatmap.BeatmapSet != null && beatmap.File == null;

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) => string.Concat(beatmapSetInfo.Beatmaps
        .Select(b => b.MD5Hash)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    /// <summary>
    /// Reads, parses and hashes every chart in a directory, resolves resource references through a
    /// locally-built file index (built once here and GC'd when this method returns), and reads all
    /// referenced resource files into memory (in parallel). Runs on the thread pool inside the
    /// producer; never touches realm.
    /// </summary>
    private static PreparedDirectory readPreparedDirectory(ImportGroup group)
    {
        var index = new DirectoryFileIndex(group.Directory);

        // Parse all charts in the directory, retaining chart bytes for writing.
        var charts = group.ChartPaths.Select(path =>
        {
            var content = File.ReadAllBytes(path);
            var md5 = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();
            var lines = BmsChartParser.PreprocessLines(BmsChartParser.ReadAllLines(content));
            var metadata = BmsChartParser.ScanMetadata(lines, path);
            var resourcePaths = BmsChartParser.ScanResourceReferences(lines)
                .Select(index.Resolve)
                .Where(p => p != null)
                .Select(p => p!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return (Chart: new ChartImport(path, md5, metadata, resourcePaths), Content: content);
        }).ToArray();

        // Read every referenced resource in parallel so a single large set still saturates disk throughput.
        var resourceFiles = charts
            .SelectMany(c => c.Chart.ResourcePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .AsParallel()
            .Select(path => (Path: path, Content: File.ReadAllBytes(path)))
            .ToArray();

        return new PreparedDirectory(group.Directory, charts, resourceFiles);
    }

    /// <summary>
    /// Performs dedup / broken-legacy resolution for a single directory's charts (using a pre-queried
    /// realm lookup) and writes any charts + resources that survive the check into the realm database in
    /// one transaction. Runs on the realm thread only.
    /// </summary>
    private bool importPreparedDirectory(
        Realm r,
        PreparedDirectory prepared,
        IReadOnlyDictionary<string, List<BeatmapInfo>> existingByMd5,
        RealmFileStore fileStore,
        RulesetInfo rulesetInfo)
    {
        try
        {
            var chartImports = prepared.Charts;

            var existingBeatmaps = chartImports
                .SelectMany(c => existingByMd5.TryGetValue(c.Chart.Md5Hash, out var list) ? list : [])
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
                ? chartImports
                : chartImports.Where(c => !duplicateHashes.Contains(c.Chart.Md5Hash)).ToArray();

            if (chartsToImport.Length == 0)
            {
                Logger.Log($"BMS import: skipping duplicate set {Path.GetFileName(prepared.Directory)}");
                return false;
            }

            using var transaction = r.BeginWrite();
            var addedFiles = new Dictionary<string, RealmFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var brokenLegacy in brokenLegacyBeatmaps)
            {
                if (brokenLegacy.BeatmapSet is { DeletePending: false } legacySet)
                {
                    Logger.Log($"BMS import: replacing legacy broken set {Path.GetFileName(prepared.Directory)}");
                    legacySet.DeletePending = true;
                }
            }

            var beatmapSetInfo = new BeatmapSetInfo
            {
                OnlineID = -1,
                DateAdded = DateTimeOffset.UtcNow,
            };

            foreach (var (chart, content) in chartsToImport)
            {
                var metadata = new BeatmapMetadata
                {
                    Title = chart.Metadata.SetTitle,
                    Artist = chart.Metadata.Artist,
                    Author = new RealmUser { Username = Constant.AUTHOR },
                };

                var chartFile = addFileUsage(beatmapSetInfo, addedFiles, chart.Path, content, fileStore, r);

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

            foreach (var (path, content) in prepared.Resources)
                addFileUsage(beatmapSetInfo, addedFiles, path, content, fileStore, r);

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            Logger.Log($"BMS import: imported {Path.GetFileName(prepared.Directory)} ({chartsToImport.Length} charts, {prepared.Resources.Length} resources)");

            OnImportCompleted?.Invoke(beatmapSetInfo.ToLive(realm), MetadataLookupScope.None);

            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"BMS import: failed to import {prepared.Directory}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Queries realm for any existing beatmaps whose MD5 hash matches one of the given candidates.
    /// Batching is preserved (max 200 hashes per Filter call) to keep individual queries compact.
    /// </summary>
    private static Dictionary<string, List<BeatmapInfo>> getExistingBeatmapsByMd5(Realm realm, string[] md5Hashes)
    {
        if (md5Hashes.Length == 0)
            return new Dictionary<string, List<BeatmapInfo>>(StringComparer.OrdinalIgnoreCase);

        const int max_hashes_per_query = 200;

        var result = new Dictionary<string, List<BeatmapInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var hash in md5Hashes)
            result[hash] = [];

        foreach (var batch in md5Hashes.Chunk(max_hashes_per_query))
        {
            var clauses = batch.Select(h => $"MD5Hash == '{h.Replace("'", "''")}'");
            var filterString = string.Join(" OR ", clauses);
            var beatmaps = realm.All<BeatmapInfo>().Filter(filterString).ToArray();

            foreach (var b in beatmaps)
            {
                if (result.TryGetValue(b.MD5Hash, out var list))
                    list.Add(b);
            }
        }

        return result;
    }

    private static RealmFile addFileUsage(BeatmapSetInfo beatmapSetInfo, Dictionary<string, RealmFile> addedFiles, string path, byte[] content, RealmFileStore fileStore, Realm realm)
    {
        var fileName = Path.GetFileName(path);

        if (addedFiles.TryGetValue(fileName, out var existingFile))
            return existingFile;

        RealmFile realmFile;
        using (var stream = new MemoryBackedFileStream(path, content))
            realmFile = fileStore.Add(stream, realm, preferHardLinks: true);

        beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFile, fileName));
        addedFiles[fileName] = realmFile;
        return realmFile;
    }

    private static void applyCompletionState(ProgressNotification notification, ImportResult result)
    {
        if (!result.RulesetAvailable)
        {
            notification.CompletionText = "BMS import failed! Check logs for more information.";
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        if (result.Imported == 0 && result.Processed > 0)
        {
            notification.CompletionText = result.Processed == 1
                ? "BMS set is already imported."
                : $"All {result.Processed} BMS sets are already imported.";
            notification.State = ProgressNotificationState.Completed;
            return;
        }

        if (result.Imported == 0)
        {
            notification.CompletionText = "BMS import failed! Check logs for more information.";
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        notification.CompletionText = result.Imported == result.TotalSets
            ? $"Imported {result.Imported} BMS sets!"
            : $"Imported {result.Imported} of {result.TotalSets} BMS sets.";
        notification.State = ProgressNotificationState.Completed;
    }

    // ── Private records (data flowing through the pipeline) ──

    private sealed record ImportResult(bool RulesetAvailable, int TotalSets, int Imported, int Processed);

    private sealed record ImportGroup(string Directory, string[] ChartPaths);

    private sealed record ChartImport(string Path, string Md5Hash, BmsChartMetadata Metadata, string[] ResourcePaths);

    private sealed record PreparedDirectory(
        string Directory,
        (ChartImport Chart, byte[] Content)[] Charts,
        (string Path, byte[] Content)[] Resources);

    /// <summary>
    /// A one-shot index of a chart directory's top-level files, built with a single enumeration. Replaces
    /// per-resource <see cref="Directory.EnumerateFiles(string,string,SearchOption)"/> calls during resource
    /// resolution, including the legacy extension-swap rule (a declared <c>.wav</c> resolving to a <c>.ogg</c>).
    /// </summary>
    private sealed class DirectoryFileIndex
    {
        private readonly string directory;
        private readonly Dictionary<string, string> byFileName;
        private readonly Dictionary<string, string> byBaseName;

        public DirectoryFileIndex(string directory)
        {
            this.directory = directory;
            byFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            byBaseName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(directory))
                return;

            foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                byFileName.TryAdd(name, path);

                var baseName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(baseName))
                    byBaseName.TryAdd(baseName, path);
            }
        }

        public string? Resolve(string resource)
        {
            if (resource.Contains('/') || resource.Contains('\\'))
            {
                var combined = Path.Combine(directory, resource);
                if (File.Exists(combined))
                    return combined;

                var withoutExtension = Path.Combine(directory, Path.ChangeExtension(resource, null));
                return findExistingResourceWithAnyExtension(withoutExtension);
            }

            if (byFileName.TryGetValue(resource, out var exact))
                return exact;

            var baseNameOnly = Path.GetFileNameWithoutExtension(resource);
            return byBaseName.GetValueOrDefault(baseNameOnly);
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
        }
    }
}
