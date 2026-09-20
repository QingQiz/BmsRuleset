using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Database;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.IO.Import;

public partial class BmsFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null, BeatmapManager? beatmaps = null) : ICanAcceptFiles
{
    // Each star-rating workspace can occupy tens of MB. Share the budget across import requests,
    // including single directories with many charts, without multiplying nested parallel loops.
    private static readonly int preparation_concurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
    private static readonly SemaphoreSlim preparation_slots = new(preparation_concurrency);

    public IEnumerable<string> HandledExtensions => Constant.BMS_EXTENSIONS;

    public Task Import(params string[] paths)
    {
        if (paths.Length == 0)
            return Task.CompletedTask;

        var notification = new ProgressNotification
        {
            Text = BmsStrings.ImportInitialising,
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

    /// <summary>
    ///     Scans all BMS beatmap sets and marks those whose <c>Source</c> directory no longer exists
    ///     on disk as <see cref="BeatmapSetInfo.DeletePending" />.  These are "orphaned" sets — the
    ///     user deleted or moved the original BMS pack directory, so audio resources are inaccessible.
    /// </summary>
    /// <remarks>
    ///     Only affects sets imported in external-audio mode (where <c>Metadata.Source</c> is set to
    ///     the original chart directory).  Sets imported before this feature (empty <c>Source</c>)
    ///     are skipped.
    /// </remarks>
    /// <returns>The number of sets marked as orphaned.</returns>
    public int CleanupOrphanedSets()
    {
        var notification = new ProgressNotification
        {
            Text = BmsStrings.ScanningOrphans,
            State = ProgressNotificationState.Active,
        };
        notifications?.Post(notification);

        try
        {
            var deleted = realm.Write(r =>
            {
                var orphans = r.All<BeatmapSetInfo>()
                    .AsEnumerable()
                    .Where(s => !s.DeletePending)
                    .Where(s => s.Beatmaps.Any(b => b.Ruleset.ShortName == Constant.SHORT_NAME))
                    .Where(s => s.Beatmaps.Any(b =>
                    {
                        var src = b.Metadata.Source;
                        return string.IsNullOrEmpty(src) || !Directory.Exists(src);
                    }))
                    .ToArray();

                foreach (var set in orphans)
                {
                    var title = set.Metadata.Title;
                    var source = set.Beatmaps.FirstOrDefault(b => !string.IsNullOrEmpty(b.Metadata.Source))?.Metadata.Source ?? "(unknown)";
                    BmsLogger.Log($"BMS cleanup: marking orphan set \"{title}\" (source: {source})");
                    set.DeletePending = true;
                }

                return orphans.Length;
            });

            notification.CompletionText = deleted > 0
                ? BmsStrings.CleanupComplete(deleted)
                : BmsStrings.NoOrphansFound;
            notification.State = ProgressNotificationState.Completed;

            return deleted;
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, $"BMS cleanup failed: {e.Message}");

            notification.CompletionText = BmsStrings.CleanupFailed;
            notification.State = ProgressNotificationState.Cancelled;
            return 0;
        }
    }

    public void DeleteAllBmsFilesAsync()
    {
        if (beatmaps == null)
        {
            BmsLogger.Log("BMS delete: BeatmapManager is unavailable; cannot delete BMS beatmaps.");
            return;
        }

        var notification = new ProgressNotification
        {
            Text = BmsStrings.DeletingBeatmaps,
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
                    foreach (var set in r.All<BeatmapSetInfo>().Where(x => !x.DeletePending))
                    {
                        notification.CancellationToken.ThrowIfCancellationRequested();

                        if (set.Beatmaps.Any(x => x.Ruleset.ShortName == Constant.SHORT_NAME))
                        {
                            r.Write(() => beatmaps.Delete(set));
                            cnt += 1;
                            notification.Text = BmsStrings.DeletedSets(cnt);
                        }
                    }
                });

                notification.CompletionText = BmsStrings.DeletedSets(cnt);
                notification.State = ProgressNotificationState.Completed;
            }
            catch (OperationCanceledException)
            {
                BmsLogger.Log($"BMS delete: cancelled after {cnt} sets");
                notification.CompletionText = cnt > 0
                    ? BmsStrings.DeleteCancelled(cnt)
                    : BmsStrings.DeleteWasCancelled;
                notification.State = ProgressNotificationState.Cancelled;
            }
            catch (Exception e)
            {
                BmsLogger.Error(e, $"BMS delete failed: {e.Message}");

                notification.CompletionText = BmsStrings.DeleteFailed;
                notification.State = ProgressNotificationState.Cancelled;
            }
        });
    }

    /// <summary>
    /// Fast discovery pass: walk the input paths, collect chart files, and group them by directory.
    /// Performs no file reading or hashing, so it returns quickly and yields an accurate total count.
    /// </summary>
    private static ImportGroup[] discoverChartGroups(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var chartPaths = paths.AsParallel()
            .WithCancellation(cancellationToken)
            .SelectMany(path => expandPathToCharts(path, cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return chartPaths
            .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new ImportGroup(g.Key!, g.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();
    }

    private static IEnumerable<string> expandPathToCharts(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Constant.IsChartFile(file))
                    yield return file;
            }
        }
        else if (File.Exists(path) && Constant.IsChartFile(path))
            yield return path;
    }

    /// <summary>Compute a deterministic set hash from sorted unique beatmap MD5 hashes.</summary>
    private static string calculateSetHash(IEnumerable<string> md5Hashes) => string.Concat(md5Hashes
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) =>
        calculateSetHash(beatmapSetInfo.Beatmaps.Select(b => b.MD5Hash));

    private ConcurrentDictionary<string, byte> createImportedBeatmapMd5Lookup()
    {
        var hashes = realm.Run(r =>
        {
            var result = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            foreach (var beatmap in r.All<BeatmapInfo>().Filter("Ruleset.ShortName == $0 && BeatmapSet.DeletePending == false", Constant.SHORT_NAME))
            {
                if (!string.IsNullOrWhiteSpace(beatmap.MD5Hash))
                    result.TryAdd(beatmap.MD5Hash, 0);
            }

            return result;
        });

        return new ConcurrentDictionary<string, byte>(hashes, StringComparer.OrdinalIgnoreCase);
    }

    private static ChartImport? readPreparedChart(
        string path,
        byte[]? content,
        string md5,
        Realm workerRealm,
        RealmFileStore fileStore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content == null)
            return null;

        var summary = BmsChartParser.ParseImportSummary(content, path, _ => 1, cancellationToken);
        var starRating = computeStarRating(summary, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryBackedFileStream(path, content);
        var fileHash = fileStore.Add(stream, workerRealm, addToRealm: false, preferHardLinks: true).Hash;
        return new ChartImport(path, md5, fileHash, summary.Metadata, starRating, summary.Bpm, summary.Length,
            summary.TotalObjectCount, summary.EndTimeObjectCount, summary.ScratchObjectCount);
    }

    /// <summary>
    ///     Compute star rating from an already-parsed chart.  Returns 0 on failure
    ///     (malformed chart, unsupported layout) — the import continues regardless.
    /// </summary>
    private static double computeStarRating(BmsImportSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            if (summary.StarRatingNoteTimings.Count == 0)
                return 0;

            var layout = BmsLayout.VariantFromTotalColumns(summary.Metadata.KeyCount);
            var judgementRate = summary.Metadata.ExRank is { } exRank
                ? BmsJudgementProfileProvider.RateForExRank(layout, exRank)
                : BmsJudgementProfileProvider.RateForRank(layout, summary.Metadata.Rank);

            return new BmsStarRatingProcessor()
                .Compute(summary.StarRatingNoteTimings, summary.Metadata.KeyCount, summary.Metadata.Rank, layout: layout, judgementRate: judgementRate, cancellationToken: cancellationToken)
                .StarRating;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            BmsLogger.Log($"BMS import: SR computation failed: {e.Message}");
            return 0;
        }
    }

    private static void applyCompletionState(ProgressNotification notification, ImportResult result)
    {
        if (!result.RulesetAvailable)
        {
            notification.CompletionText = BmsStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        if (result.Imported == 0 && result.Processed > 0)
        {
            notification.CompletionText = result.Processed == 1
                ? BmsStrings.SetAlreadyImported
                : BmsStrings.AllSetsAlreadyImported(result.Processed);
            notification.State = ProgressNotificationState.Completed;
            return;
        }

        if (result.Imported == 0)
        {
            notification.CompletionText = BmsStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
            return;
        }

        notification.CompletionText = result.Imported == result.TotalSets
            ? BmsStrings.ImportedSets(result.Imported)
            : BmsStrings.ImportedSetsProgress(result.Imported, result.TotalSets);
        notification.State = ProgressNotificationState.Completed;
    }

    /// <summary>Validate that the BMS ruleset is available; set notification state if not.</summary>
    private bool checkRulesetAvailable(ProgressNotification notification) => realm.Run(r =>
    {
        if (r.Find<RulesetInfo>(Constant.SHORT_NAME)?.Available == true) return true;

        notification.CompletionText = BmsStrings.RulesetUnavailable;
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
        using var bulkUpdate = BmsBulkBeatmapUpdate.Begin(realm);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(notification.CancellationToken);
        try
        {
            var fileStore = new RealmFileStore(realm, storage);

            notification.Text = BmsStrings.ScanningFiles;
            var groups = discoverChartGroups(paths, cancellation.Token);

            if (groups.Length == 0)
            {
                notification.CompletionText = BmsStrings.NoChartsFound;
                notification.State = ProgressNotificationState.Cancelled;
                return;
            }

            notification.Text = BmsStrings.PreparingImport;

            using var pool = new BlockingCollection<PreparedDirectory?>(preparation_concurrency * 2);
            var importedBeatmapMd5Hashes = createImportedBeatmapMd5Lookup();
            var producer = launchProducer(groups, fileStore, pool, importedBeatmapMd5Hashes, cancellation.Token);
            (int imported, int processed, bool cancelled) result;
            try
            {
                result = drainConsumer(notification, groups, pool, cancellation.Token);
            }
            finally
            {
                // A consumer failure must also release producers blocked on a full queue. Do not
                // dispose the queue or finish the bulk-update scope while workers can still write.
                cancellation.Cancel();
                producer.GetAwaiter().GetResult();
            }

            var (imported, processed, cancelled) = result;

            if (cancelled) return; // drainConsumer already set the notification state

            applyCompletionState(notification,
                new ImportResult(RulesetAvailable: true, TotalSets: groups.Length, Imported: imported, Processed: processed));
        }
        catch (OperationCanceledException)
        {
            BmsLogger.Log("BMS import: cancelled");
            notification.CompletionText = BmsStrings.ImportWasCancelled;
            notification.State = ProgressNotificationState.Cancelled;
        }
        catch (Exception e)
        {
            BmsLogger.Log($"BMS import: scan failed: {e.Message}");
            BmsLogger.Log(e.ToString());
            notification.CompletionText = BmsStrings.ImportFailed;
            notification.State = ProgressNotificationState.Cancelled;
        }
    }

    private Task launchProducer(
        ImportGroup[] groups,
        RealmFileStore fileStore,
        BlockingCollection<PreparedDirectory?> pool,
        ConcurrentDictionary<string, byte> importedBeatmapMd5Hashes,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            try
            {
                // Admit hashes in filename order so identical files with different names/extensions
                // retain the same metadata winner. No buffering keeps only worker-owned bytes alive.
                var charts = Partitioner.Create(readCharts(), EnumerablePartitionerOptions.NoBuffering);
                var partitions = charts.GetPartitions(preparation_concurrency);
                try
                {
                    // Realm contexts are thread-bound and costly to reopen for every chart.
                    Parallel.ForEach(partitions, new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = preparation_concurrency,
                    }, partition => realm.Run(workerRealm =>
                    {
                        while (partition.MoveNext())
                        {
                            var chart = partition.Current;
                            preparation_slots.Wait(cancellationToken);
                            try
                            {
                                chart.directory.Charts[chart.index] = readPreparedChart(chart.path, chart.content, chart.md5, workerRealm, fileStore, cancellationToken);
                            }
                            finally
                            {
                                preparation_slots.Release();
                            }

                            if (Interlocked.Decrement(ref chart.directory.Remaining) == 0)
                            {
                                var preparedCharts = chart.directory.Charts.OfType<ChartImport>().ToArray();
                                pool.Add(preparedCharts.Length == 0 ? null : new PreparedDirectory(chart.directory.Group.Directory, preparedCharts), cancellationToken);
                            }
                        }
                    }));
                }
                finally
                {
                    foreach (var partition in partitions)
                        partition.Dispose();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception e)
            {
                BmsLogger.Log($"BMS import: producer error: {e.Message}");
            }
            finally
            {
                pool.CompleteAdding();
            }

            IEnumerable<(DirectoryPreparation directory, int index, string path, byte[]? content, string md5)> readCharts()
            {
                foreach (var group in groups)
                {
                    var directory = new DirectoryPreparation(group);
                    for (var index = 0; index < group.ChartPaths.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var path = group.ChartPaths[index];
                        var content = File.ReadAllBytes(path);
                        cancellationToken.ThrowIfCancellationRequested();
                        var md5 = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();
                        yield return (directory, index, path, importedBeatmapMd5Hashes.TryAdd(md5, 0) ? content : null, md5);
                    }
                }
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
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var processed = 0;

        try
        {
            foreach (var prepared in pool.GetConsumingEnumerable(cancellationToken))
            {
                realm.Run(r =>
                {
                    var rulesetInfo = r.Find<RulesetInfo>(Constant.SHORT_NAME)!;
                    notification.CancellationToken.ThrowIfCancellationRequested();

                    // Directories containing only known hashes still count towards progress.
                    if (prepared == null)
                    {
                        processed++;
                        reportProgress(notification, imported, groups.Length, processed);
                        return;
                    }

                    var ok = importPreparedDirectory(r, prepared, rulesetInfo);

                    if (ok)
                        imported++;

                    processed++;
                    reportProgress(notification, imported, groups.Length, processed);
                });
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            BmsLogger.Log($"BMS import: cancelled after {imported} of {groups.Length} sets");
            notification.CompletionText = imported > 0
                ? BmsStrings.ImportCancelledProgress(imported, groups.Length)
                : BmsStrings.ImportWasCancelled;
            notification.State = ProgressNotificationState.Cancelled;
            return (imported, processed, true);
        }

        return (imported, processed, false);

        static void reportProgress(ProgressNotification n, int imp, int total, int proc)
        {
            n.Text = BmsStrings.ImportedSetsProgress(imp, total);
            n.Progress = (float)proc / total;
        }
    }

    /// <summary>
    /// Creates <see cref="RealmFile"/> + <see cref="BeatmapSetInfo"/> objects for all charts in a
    /// prepared directory, in one transaction. Files were already written to disk by the producer,
    /// so this transaction only touches realm metadata — no disk I/O.
    /// Star rating is pre-computed during the producer phase and written directly.
    /// </summary>
    private bool importPreparedDirectory(
        Realm r,
        PreparedDirectory prepared,
        RulesetInfo rulesetInfo)
    {
        try
        {
            var chartImports = prepared.Charts;

            // Collect all unique file hashes for batch RealmFile creation.
            var allHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in chartImports) allHashes.Add(c.FileHash);

            using var transaction = r.BeginWrite();

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
            foreach (var chart in chartImports)
            {
                var fileName = Path.GetFileName(chart.Path);
                if (seenFilenames.Add(fileName))
                    beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFileByHash[chart.FileHash], fileName));
            }

            // The clean common set title. Serves double duty: it's the displayed
            // set/beatmap title (osu! has no separate set-level metadata —
            // BeatmapSetInfo.Metadata is Beatmaps.FirstOrDefault().Metadata), and it's
            // the authoritative base for splitting each chart's raw #TITLE into (base,
            // difficulty suffix). The raw #TITLE stays in BmsChartMetadata.RawTitle for
            // that split.
            var setTitle = BmsChartParser.InferCommonSetTitle(chartImports.Select(c => c.Metadata.RawTitle).ToArray());

            // Create beatmap infos.
            foreach (var chart in chartImports)
            {
                var metadata = chart.Metadata with { DifficultyName = resolveDifficultyName(chart, setTitle) };

                var beatmapInfo = new BeatmapInfo
                {
                    DifficultyName = metadata.DifficultyName,
                    Ruleset = rulesetInfo,
                    Metadata = new BeatmapMetadata
                    {
                        Title = setTitle,
                        Artist = metadata.Artist,
                        Author = new RealmUser { Username = Constant.AUTHOR },
                        Source = prepared.Directory,
                        PreviewTime = 0,
                    },
                    Difficulty = new BeatmapDifficulty(),
                    Hash = chart.FileHash,
                    MD5Hash = chart.Md5Hash,
                    StarRating = chart.StarRating,
                    BPM = chart.Bpm,
                    Length = chart.Length,
                    TotalObjectCount = chart.TotalObjectCount,
                    EndTimeObjectCount = chart.EndTimeObjectCount,
                };
                var diff = BmsDifficultyInfo.FromChartMetadata(metadata);
                diff.WriteToOsuDifficulty(beatmapInfo);
                BmsBeatmapStatistics.WriteScratchObjectCount(beatmapInfo, chart.ScratchObjectCount);

                DifficultyNameUpdater.GetDifficultyName(beatmapInfo, out var markerStr);
                if (!string.IsNullOrWhiteSpace(markerStr))
                {
                    beatmapInfo.DifficultyName = DifficultyNameUpdater.AddMarkerSuffix(beatmapInfo.DifficultyName, markerStr);
                }

                beatmapSetInfo.Beatmaps.Add(beatmapInfo);
                beatmapInfo.BeatmapSet = beatmapSetInfo;
            }

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            return true;
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, $"BMS import: failed to import {prepared.Directory}: {e.Message}");
            return false;
        }

        // Derive DifficultyName for one chart, given the set's common base title.
        // Priority: #SUBTITLE (author's explicit label) > InferDifficultyName
        // (set-relative suffix; outliers that don't share the base use their raw
        // title; single-chart sets fall back to a per-chart InferTitle split) > filename.
        static string resolveDifficultyName(ChartImport chart, string setTitle)
        {
            if (!string.IsNullOrEmpty(chart.Metadata.DifficultyName))
                return chart.Metadata.DifficultyName;

            var inferred = BmsChartParser.InferDifficultyName(chart.Metadata.RawTitle, setTitle);
            if (!string.IsNullOrEmpty(inferred))
                return inferred;

            return Path.GetFileNameWithoutExtension(chart.Path);
        }
    }

    // ── Private records (data flowing through the pipeline) ──

    private sealed record ImportResult(bool RulesetAvailable, int TotalSets, int Imported, int Processed);

    private sealed record ImportGroup(string Directory, string[] ChartPaths);

    private sealed class DirectoryPreparation(ImportGroup group)
    {
        public ImportGroup Group { get; } = group;

        public ChartImport?[] Charts { get; } = new ChartImport?[group.ChartPaths.Length];

        public int Remaining = group.ChartPaths.Length;
    }

    private sealed record ChartImport(
        string Path,
        string Md5Hash,
        string FileHash, // SHA-256 for RealmFile (computed during file write)
        BmsChartMetadata Metadata,
        double StarRating,
        double Bpm,
        double Length,
        int TotalObjectCount,
        int EndTimeObjectCount,
        int ScratchObjectCount);

    private sealed record PreparedDirectory(
        string Directory,
        ChartImport[] Charts);

    /// <summary>
    /// A <see cref="FileStream"/> whose contents are served from an in-memory buffer rather than disk,
    /// while still reporting the real file path through <see cref="FileStream.Name"/>.
    /// <para>
    /// This lets <see cref="RealmFileStore.Add"/> take its hard-link fast path (which requires the stream
    /// to be a <see cref="FileStream"/> and reads <c>Name</c>) without re-reading the file from disk for
    /// hashing — the bytes were already read for MD5 and parsing.
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
