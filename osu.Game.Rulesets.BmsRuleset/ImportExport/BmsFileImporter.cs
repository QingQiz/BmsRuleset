using System;
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

public partial class BmsFileImporter(RealmAccess realm, Storage storage, INotificationOverlay? notifications = null) : ICanAcceptFiles
{
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
                var importSets = collectImportSets(paths).ToArray();

                notification.Progress = 0;

                var imported = realm.Run(r =>
                {
                    var existingBeatmaps = getExistingBeatmapsByMd5(r, importSets);
                    var count = 0;

                    for (var i = 0; i < importSets.Length; i++)
                    {
                        if (importSet(r, importSets[i], fileStore, existingBeatmaps))
                            count++;

                        notification.Text = $"Imported {count} of {importSets.Length} BMS sets";
                        notification.Progress = (float)(i + 1) / importSets.Length;
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

    private static IEnumerable<ImportSet> collectImportSets(IEnumerable<string> paths)
    {
        var chartPaths = paths.AsParallel()
            .SelectMany(expandPathToCharts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return chartPaths
            .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase).Select(g => g)
            .AsParallel()
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g =>
            {
                var k = g.Key!;
                var charts = g.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
                var im = charts.AsParallel().Select(p => new ChartImport(k, p)).ToArray();

                return new ImportSet(k, im);
            });
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

    private static RealmFile addFileUsage(BeatmapSetInfo beatmapSetInfo, string path, byte[] content, RealmFileStore fileStore, Realm realm, string? precomputedSha256 = null)
    {
        var fileName = Path.GetFileName(path);

        if (beatmapSetInfo.Files.FirstOrDefault(f => string.Equals(f.Filename, fileName, StringComparison.OrdinalIgnoreCase)) is { } existingUsage)
            return existingUsage.File;

        if (precomputedSha256 != null)
        {
            var existing = realm.Find<RealmFile>(precomputedSha256);
            if (existing != null)
            {
                if (!existing.IsManaged)
                    realm.Add(existing);

                beatmapSetInfo.Files.Add(new RealmNamedFileUsage(existing, fileName));
                return existing;
            }
        }

        RealmFile realmFile;
        using (var ms = new MemoryStream(content))
            realmFile = fileStore.Add(ms, realm);

        if (!realmFile.IsManaged)
            realm.Add(realmFile);

        beatmapSetInfo.Files.Add(new RealmNamedFileUsage(realmFile, fileName));
        return realmFile;
    }

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

    private static bool importSet(Realm r, ImportSet importSet, RealmFileStore fileStore, IReadOnlyDictionary<string, List<BeatmapInfo>> existingBeatmapsByMd5)
    {
        try
        {
            if (importSet.ChartImports.Length == 0)
                return false;

            var chartHashes = importSet.ChartImports.Select(c => c.Md5Hash).ToArray();

            var rulesetInfo = r.Find<RulesetInfo>("bms");
            if (rulesetInfo?.Available != true)
            {
                Logger.Log("BMS import: ruleset 'bms' is not available in realm");
                return false;
            }

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
                return true;
            }

            var resourcePaths = importSet.ChartImports
                .SelectMany(c => c.Resources).DistinctBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var resourceContents = resourcePaths
                .AsParallel()
                .Select(path =>
                {
                    var content = File.ReadAllBytes(path);
                    var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
                    return (Path: path, Content: content, Sha256: sha256);
                })
                .ToArray();

            using var transaction = r.BeginWrite();

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

                var chartFile = addFileUsage(beatmapSetInfo, chart.Path, chart.Content, fileStore, r, chart.Sha256Hash);

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

            foreach (var (path, content, sha256) in resourceContents)
                addFileUsage(beatmapSetInfo, path, content, fileStore, r, sha256);

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);
            r.Add(beatmapSetInfo);
            transaction.Commit();

            Logger.Log($"BMS import: imported {Path.GetFileName(importSet.Directory)} ({importSet.ChartImports.Length} charts, {resourcePaths.Length} resources)");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"BMS import: failed to import {importSet.Directory}: {e.Message}");
            return false;
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

    private sealed record ImportSet(
        string Directory,
        ChartImport[] ChartImports);

    private sealed class ChartImport
    {

        public string Path { get; }

        public byte[] Content { get; }

        public string[] Lines { get; }

        public string Md5Hash { get; }

        public string Sha256Hash { get; }

        public string[] Resources { get; }

        public BmsChartMetadata Metadata { get; }

        public ChartImport(string directory, string path)
        {
            Path = path;
            Content = File.ReadAllBytes(path);
            Lines = BmsChartParser.ReadAllLines(Content);
            Md5Hash = BitConverter.ToString(MD5.HashData(Content)).Replace("-", string.Empty).ToLowerInvariant();
            Sha256Hash = Convert.ToHexString(SHA256.HashData(Content)).ToLowerInvariant();
            Metadata = BmsChartParser.ScanMetadata(BmsChartParser.ReadAllLines(Content), path);
            Resources = BmsChartParser.ScanResourceReferences(Lines)
                .AsParallel()
                .Select(resource => resolveResourcePath(directory, resource))
                .Where(File.Exists)
                .ToArray();
        }
    }
}
