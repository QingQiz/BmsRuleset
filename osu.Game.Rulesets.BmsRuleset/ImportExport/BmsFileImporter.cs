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
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.ImportExport;

public partial class BmsFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null, BeatmapManager? beatmaps = null) : ICanAcceptFiles
{

    public IEnumerable<string> HandledExtensions => Constant.BMS_EXTENSIONS;

    /// <summary>
    /// Fired after a beatmap set is successfully imported, to trigger difficulty recalculation.
    /// A <see cref="Live{T}"/> reference is passed (rather than the thread-confined realm object) so the
    /// handler can process the set on a background thread without blocking the import write loop.
    /// </summary>
    public Action<Live<BeatmapSetInfo>, MetadataLookupScope>? OnImportCompleted { get; init; }

    public Task Import(params string[] paths)
    {
        if (paths.Length == 0)
            return Task.CompletedTask;

        var notification = new ProgressNotification
        {
            Text = "BMS import is initialising...",
            State = ProgressNotificationState.Active,
            CancelRequested = () => true,
        };
        notifications?.Post(notification);

        return !checkRulesetAvailable(notification)
            ? Task.CompletedTask
            : Task.Run(() => runImportPipeline(notification, paths));
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

        var notification = new ProgressNotification
        {
            Text = "Deleting Bms beatmaps...",
            State = ProgressNotificationState.Active,
        };
        notification.CancelRequested = () => true;
        notifications?.Post(notification);

        var cnt = 0;

        Task.Run(() =>
        {
            try
            {
                realm.Run(r =>
                {
                    foreach (var set in r.All<BeatmapSetInfo>())
                    {
                        notification.CancellationToken.ThrowIfCancellationRequested();

                        if (set.Beatmaps.Any(x => x.Ruleset.ShortName == "bms"))
                        {
                            r.Write(() => beatmaps.Delete(set));
                            cnt += 1;
                            notification.Text = $"Deleted {cnt} BMS beatmap sets";
                        }
                    }
                });

                notification.CompletionText = $"Deleted {cnt} BMS beatmap sets";
                notification.State = ProgressNotificationState.Completed;
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"BMS delete: cancelled after {cnt} sets");
                notification.CompletionText = cnt > 0
                    ? $"Delete cancelled. {cnt} BMS beatmap sets were deleted."
                    : "BMS delete was cancelled.";
                notification.State = ProgressNotificationState.Cancelled;
            }
            catch (Exception e)
            {
                Logger.Error(e, $"BMS delete failed: {e.Message}");

                notification.CompletionText = "An error occurred while deleting BMS beatmaps. Check logs for more information.";
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

    /// <summary>Compute a deterministic set hash from sorted beatmap MD5 hashes.</summary>
    private static string calculateSetHash(IEnumerable<string> md5Hashes) => string.Concat(md5Hashes
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) =>
        calculateSetHash(beatmapSetInfo.Beatmaps.Select(b => b.MD5Hash));

    /// <summary>
    /// Reads, parses and hashes every chart in a directory, resolves resource references, and
    /// <em>writes all files to disk</em> via <see cref="RealmFileStore.Add"/> with
    /// <c>addToRealm: false</c>. Resource files are read in parallel to saturate disk I/O;
    /// <see cref="RealmFileStore.Add"/> calls are sequential (Realm instances are
    /// thread-confined).
    /// </summary>
    private static PreparedDirectory? readPreparedDirectory(
        ImportGroup group,
        RealmAccess realmAccess,
        RealmFileStore fileStore)
    {
        return realmAccess.Run(r =>
        {
            var index = new DirectoryFileIndex(group.Directory);

            var bytes = group.ChartPaths.AsParallel().Select(File.ReadAllBytes).ToArray();
            var allMd5 = bytes.AsParallel().Select(b => Convert.ToHexString(MD5.HashData(b)).ToLowerInvariant()).ToArray();
            var setHash = calculateSetHash(allMd5);

            var existingSet = r.All<BeatmapSetInfo>()
                .Filter("Hash == $0", setHash)
                .FirstOrDefault();

            if (existingSet != null && !existingSet.DeletePending)
            {
                return null;
            }

            // Parse every chart, extract metadata, and write chart file to disk.
            var charts = group.ChartPaths.Select((path, i) =>
            {
                var content = bytes[i];
                var md5 = allMd5[i];
                var lines = BmsChartParser.PreprocessLines(BmsChartParser.ReadAllLines(content));
                var metadata = BmsChartParser.ScanMetadata(lines, path);
                var resourcePaths = BmsChartParser.ScanResourceReferences(lines)
                    .Select(index.Resolve)
                    .Where(p => p != null)
                    .Select(p => p!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                // Write to disk (hard-link) and obtain the SHA-256 hash.
                // addToRealm: false → no realm transaction needed; disk I/O only.
                using var stream = new MemoryBackedFileStream(path, content);
                var fileHash =
                    fileStore.Add(stream, r, addToRealm: false, preferHardLinks: true).Hash;

                return new ChartImport(path, md5, fileHash, metadata, resourcePaths);
            }).ToArray();

            // Collect unique resource paths.
            var allResourcePaths = charts
                .SelectMany(c => c.ResourcePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Read all resource files in parallel (disk I/O), then write sequentially
            // (Realm.Find + hard-link, which must stay single-threaded per Realm instance).
            var resourceData = allResourcePaths
                .Select(path =>
                {
                    var content = File.ReadAllBytes(path);
                    using var stream = new MemoryBackedFileStream(path, content);
                    var fileHash = fileStore.Add(stream, r, addToRealm: false, preferHardLinks: true).Hash;

                    return new ResourceImport(path, fileHash);
                })
                .ToArray();

            return new PreparedDirectory(group.Directory, charts, resourceData);
        });
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

    /// <summary>Validate that the BMS ruleset is available; set notification state if not.</summary>
    private bool checkRulesetAvailable(ProgressNotification notification) => realm.Run(r =>
    {
        if (r.Find<RulesetInfo>("bms")?.Available == true) return true;

        notification.CompletionText = "Bms ruleset is not available";
        notification.State = ProgressNotificationState.Cancelled;
        return false;
    });

    /// <summary>
    /// Full import pipeline (runs on a thread-pool thread). Orchestrates discovery,
    /// producer launch, and consumer drain. All cancellation and error states are
    /// written back to <paramref name="notification"/>.
    /// </summary>
    private void runImportPipeline(ProgressNotification notification, string[] paths)
    {
        try
        {
            var fileStore = new RealmFileStore(realm, storage);

            notification.Text = "BMS import: scanning files...";
            var groups = discoverChartGroups(paths);
            if (groups.Length == 0)
            {
                notification.CompletionText = "No BMS charts found to import.";
                notification.State = ProgressNotificationState.Cancelled;
                return;
            }

            notification.Text = "BMS import: preparing...";

            var pool = new BlockingCollection<PreparedDirectory?>(32);

            var producer = launchProducer(notification, groups, fileStore, pool);
            var (imported, processed, cancelled) = drainConsumer(notification, groups, pool, producer);

            if (cancelled) return; // drainConsumer already set the notification state

            applyCompletionState(notification,
                new ImportResult(RulesetAvailable: true, TotalSets: groups.Length, Imported: imported, Processed: processed));
        }
        catch (OperationCanceledException)
        {
            Logger.Log("BMS import: cancelled");
            notification.CompletionText = "BMS import was cancelled.";
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception e)
        {
            Logger.Log($"BMS import: scan failed: {e.Message}");
            Logger.Log(e.ToString());
            notification.CompletionText = "BMS import failed! Check logs for more information.";
            notification.State = ProgressNotificationState.Cancelled;
        }
    }

    /// <summary>
    /// Launch producer tasks that read, parse, and write chart/resource files to disk
    /// in parallel, one directory per worker.
    /// </summary>
    private Task launchProducer(
        ProgressNotification notification,
        ImportGroup[] groups,
        RealmFileStore fileStore,
        BlockingCollection<PreparedDirectory?> pool)
    {
        return Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(groups, new ParallelOptions
                {
                    // MaxDegreeOfParallelism = Environment.ProcessorCount * 4,
                    CancellationToken = notification.CancellationToken,
                }, group =>
                {
                    var prepared = readPreparedDirectory(group, realm, fileStore);
                    pool.Add(prepared, notification.CancellationToken);
                });
            }
            catch (OperationCanceledException e)
            {
                // Producer cancelled — items already queued will still be consumed.
                Logger.Log($"BMS import: producer error: {e.Message}");
            }
            catch (Exception e)
            {
                Logger.Log($"BMS import: producer error: {e.Message}");
            }
            finally
            {
                pool.CompleteAdding();
            }
        });
    }

    /// <summary>
    /// Drain the prepared-directory queue, performing per-set dedup and realm writes.
    /// Returns <c>cancelled = true</c> when the user requested cancellation; the caller
    /// should not overwrite the notification state.
    /// </summary>
    private (int imported, int processed, bool cancelled) drainConsumer(
        ProgressNotification notification,
        ImportGroup[] groups,
        BlockingCollection<PreparedDirectory?> pool,
        Task producer)
    {
        var imported = 0;
        var processed = 0;

        try
        {
            Parallel.ForEach(pool.GetConsumingEnumerable(), prepared =>
            {
                realm.Run(r =>
                {
                    var rulesetInfo = r.Find<RulesetInfo>("bms")!;
                    notification.CancellationToken.ThrowIfCancellationRequested();

                    bool exists;
                    // ── Fast path: skip if the set hash already exists ──
                    if (prepared == null)
                        exists = true;
                    else
                    {
                        var setHash = calculateSetHash(prepared.Charts.Select(c => c.Md5Hash));
                        var existingSet = r.All<BeatmapSetInfo>()
                            .Filter("Hash == $0", setHash)
                            .FirstOrDefault();
                        exists = (existingSet != null && !existingSet.DeletePending);
                    }

                    if (exists)
                    {
                        Logger.Log($"BMS import: skipping existing set {Path.GetFileName(prepared?.Directory)} (hash match)");
                        processed++;
                        reportProgress(notification, imported, groups.Length, processed);
                        return;
                    }

                    var md5 = prepared!.Charts.Select(c => c.Md5Hash).ToArray();
                    var existingByMd5 = getExistingBeatmapsByMd5(r, md5);

                    if (importPreparedDirectory(r, prepared, existingByMd5, rulesetInfo))
                        imported++;

                    processed++;
                    reportProgress(notification, imported, groups.Length, processed);
                });
            });

            producer.GetAwaiter().GetResult();
            pool.Dispose();
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"BMS import: cancelled after {imported} of {groups.Length} sets");
            notification.CompletionText = imported > 0
                ? $"Import cancelled. {imported} of {groups.Length} BMS sets were imported."
                : "BMS import was cancelled.";
            notification.State = ProgressNotificationState.Cancelled;
            return (imported, processed, true);
        }

        return (imported, processed, false);

        static void reportProgress(ProgressNotification n, int imp, int total, int proc)
        {
            n.Text = $"Imported {imp} of {total} BMS sets";
            n.Progress = (float)proc / total;
        }
    }

    /// <summary>
    /// Performs dedup / broken-legacy resolution for a single directory's charts (using a pre-queried
    /// realm lookup) and creates all <see cref="RealmFile"/> + <see cref="BeatmapSetInfo"/> objects in
    /// one fast transaction. Files were already written to disk by the producer, so this transaction
    /// only touches realm metadata — no disk I/O.
    /// </summary>
    private bool importPreparedDirectory(
        Realm r,
        PreparedDirectory prepared,
        IReadOnlyDictionary<string, List<BeatmapInfo>> existingByMd5,
        RulesetInfo rulesetInfo)
    {
        try
        {
            var chartImports = prepared.Charts;

            var existingBeatmaps = chartImports
                .SelectMany(c => existingByMd5.TryGetValue(c.Md5Hash, out var list) ? list : [])
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
                : chartImports.Where(c => !duplicateHashes.Contains(c.Md5Hash)).ToArray();

            if (chartsToImport.Length == 0)
            {
                Logger.Log($"BMS import: skipping duplicate set {Path.GetFileName(prepared.Directory)}");
                return false;
            }

            // Collect all unique file hashes (charts + resources) for batch RealmFile creation.
            var allHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chartsToImport) allHashes.Add(c.FileHash);
            foreach (var rsc in prepared.Resources) allHashes.Add(rsc.FileHash);

            using var transaction = r.BeginWrite();

            // Handle broken legacy beatmaps.
            foreach (var brokenLegacy in brokenLegacyBeatmaps)
            {
                if (brokenLegacy.BeatmapSet is { DeletePending: false } legacySet)
                {
                    Logger.Log($"BMS import: replacing legacy broken set {Path.GetFileName(prepared.Directory)}");
                    legacySet.DeletePending = true;
                }
            }

            // Find-or-create RealmFile objects. Files already exist on disk (written by producers);
            // we only need the realm metadata to point at them.
            var realmFileByHash = new Dictionary<string, RealmFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var hash in allHashes)
            {
                var existing = r.Find<RealmFile>(hash);
                if (existing != null)
                    realmFileByHash[hash] = existing;
                else
                {
                    var rf = new RealmFile { Hash = hash };
                    r.Add(rf);
                    realmFileByHash[hash] = rf;
                }
            }

            // ── Build the BeatmapSet ──

            var beatmapSetInfo = new BeatmapSetInfo
            {
                OnlineID = -1,
                DateAdded = DateTimeOffset.UtcNow,
            };

            // Attach file usages (dedupe by filename).
            var seenFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chart in chartsToImport)
            {
                var fileName = Path.GetFileName(chart.Path);
                if (seenFilenames.Add(fileName))
                    beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFileByHash[chart.FileHash], fileName));
            }

            foreach (var resource in prepared.Resources)
            {
                var fileName = Path.GetFileName(resource.Path);
                if (seenFilenames.Add(fileName))
                    beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFileByHash[resource.FileHash], fileName));
            }

            // Compute the common set title from all chart raw titles via LCP.
            var commonSetTitle = BmsChartParser.InferCommonSetTitle(
                chartsToImport.Select(c => c.Metadata.RawTitle).ToArray());

            // Create beatmap infos.
            foreach (var chart in chartsToImport)
            {
                var beatmapInfo = new BeatmapInfo
                {
                    DifficultyName = chart.Metadata.DifficultyName,
                    Ruleset = rulesetInfo,
                    Metadata = new BeatmapMetadata
                    {
                        Title = commonSetTitle.Length > 0 ? commonSetTitle : chart.Metadata.SetTitle,
                        Artist = chart.Metadata.Artist,
                        Author = new RealmUser { Username = Constant.AUTHOR },
                    },
                    Difficulty = new BeatmapDifficulty(),
                    Hash = chart.FileHash,
                    MD5Hash = chart.Md5Hash,
                };
                var diff = BmsDifficultyInfo.FromChartMetadata(chart.Metadata);
                diff.WriteToOsuDifficulty(beatmapInfo);

                DifficultyNameUpdater.GetDifficultyName(beatmapInfo, out var markerStr);
                if (!string.IsNullOrWhiteSpace(markerStr))
                {
                    beatmapInfo.DifficultyName += $" [{markerStr}]";
                }

                beatmapSetInfo.Beatmaps.Add(beatmapInfo);
                beatmapInfo.BeatmapSet = beatmapSetInfo;
            }

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            Logger.Log($"BMS import: imported {Path.GetFileName(prepared.Directory)} ({chartsToImport.Length} charts, {prepared.Resources.Length} resources)");

            // Fire AFTER transaction commit — handlers can safely start their own realm operations
            // without nesting inside this transaction.
            OnImportCompleted?.Invoke(beatmapSetInfo.ToLive(realm), MetadataLookupScope.None);

            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, $"BMS import: failed to import {prepared.Directory}: {e.Message}");
            return false;
        }
    }

    // ── Private records (data flowing through the pipeline) ──

    private sealed record ImportResult(bool RulesetAvailable, int TotalSets, int Imported, int Processed);

    private sealed record ImportGroup(string Directory, string[] ChartPaths);

    private sealed record ChartImport(
        string Path,
        string Md5Hash,
        string FileHash, // SHA-256 for RealmFile (computed during file write)
        BmsChartMetadata Metadata,
        string[] ResourcePaths);

    private sealed record ResourceImport(
        string Path,
        string FileHash); // SHA-256 for RealmFile (computed during file write)

    private sealed record PreparedDirectory(
        string Directory,
        ChartImport[] Charts,
        ResourceImport[] Resources);

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

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => content.Length;

        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        private int position;

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
