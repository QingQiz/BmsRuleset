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
                notification.CompletionText = "BMS import failed! Check logs for more information.";
                notification.State = ProgressNotificationState.Cancelled;
            }
        });
    }

    public Task Import(ImportTask[] tasks, ImportParameters parameters = default)
        => Import(tasks.Select(t => t.Path).ToArray());

    public void DeleteAllBmsFiles()
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
        var chartPaths = paths.SelectMany(expandPathToCharts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in chartPaths.GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                continue;

            var charts = group.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
            var im = charts.Select(p => new ChartImport(group.Key, p)).ToArray();

            yield return new ImportSet(group.Key, im, true);
        }
    }

    private static IEnumerable<string> expandPathToCharts(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => Constant.IsChartFile(f));
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

    private static RealmFile addFileUsage(BeatmapSetInfo beatmapSetInfo, string path, byte[] content, RealmFileStore fileStore, Realm realm)
    {
        var fileName = Path.GetFileName(path);

        if (beatmapSetInfo.Files.FirstOrDefault(f => string.Equals(f.Filename, fileName, StringComparison.OrdinalIgnoreCase)) is { } existingUsage)
            return existingUsage.File;

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

        var beatmapsByMd5 = chartHashes.ToDictionary(h => h, _ => new List<BeatmapInfo>(), StringComparer.OrdinalIgnoreCase);

        foreach (var beatmap in realm.All<BeatmapInfo>().AsEnumerable()
            .Where(b => b.MD5Hash != null && chartHashes.Contains(b.MD5Hash))
            .ToArray())
        {
            beatmapsByMd5[beatmap.MD5Hash].Add(beatmap);
        }

        return beatmapsByMd5;
    }

    private static BeatmapInfo[] getExistingBeatmaps(IReadOnlyDictionary<string, List<BeatmapInfo>> existingBeatmapsByMd5, IEnumerable<string> chartHashes)
    {
        var beatmaps = new List<BeatmapInfo>();

        foreach (var hash in chartHashes)
        {
            if (existingBeatmapsByMd5.TryGetValue(hash, out var matches))
                beatmaps.AddRange(matches);
        }

        return beatmaps.Distinct().ToArray();
    }

    private static void addImportedBeatmaps(IReadOnlyDictionary<string, List<BeatmapInfo>> existingBeatmapsByMd5, IEnumerable<BeatmapInfo> beatmaps)
    {
        foreach (var beatmap in beatmaps)
        {
            if (!existingBeatmapsByMd5.TryGetValue(beatmap.MD5Hash, out var matches))
                continue;

            matches.Add(beatmap);
        }
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

            using var transaction = r.BeginWrite();

            var rulesetInfo = r.Find<RulesetInfo>("bms");
            if (rulesetInfo?.Available != true)
            {
                Logger.Log("BMS import: ruleset 'bms' is not available in realm");
                return false;
            }

            var existingBeatmaps = getExistingBeatmaps(existingBeatmapsByMd5, chartHashes);

            var existingActiveBeatmaps = existingBeatmaps
                .Where(b => b.BeatmapSet is { DeletePending: false })
                .ToArray();

            var brokenLegacyBeatmaps = existingBeatmaps
                .Where(isBrokenLegacyBmsImport)
                .ToArray();

            foreach (var brokenLegacy in brokenLegacyBeatmaps)
            {
                Logger.Log($"BMS import: replacing legacy broken set {Path.GetFileName(importSet.Directory)}");
                brokenLegacy.BeatmapSet!.DeletePending = true;
            }

            var existingDirectorySet = importSet.CanMergeWithActiveSet
                ? existingActiveBeatmaps.Select(b => b.BeatmapSet).FirstOrDefault(s => s?.Beatmaps.Count > 1)
                : null;
            var chartsToImport = brokenLegacyBeatmaps.Length > 0 || importSet.CanMergeWithActiveSet && existingDirectorySet == null
                ? importSet.ChartImports
                : existingDirectorySet != null
                    ? importSet.ChartImports.Where(c => existingDirectorySet.Beatmaps.All(b => b.MD5Hash != c.Md5Hash)).ToArray()
                    : importSet.ChartImports.Where(c => existingActiveBeatmaps.All(b => b.MD5Hash != c.Md5Hash)).ToArray();

            if (chartsToImport.Length == 0)
            {
                Logger.Log($"BMS import: skipping duplicate set {Path.GetFileName(importSet.Directory)}");
                return true;
            }

            var beatmapSetInfo = brokenLegacyBeatmaps.Length == 0 ? existingDirectorySet : null;

            beatmapSetInfo ??= new BeatmapSetInfo
            {
                OnlineID = -1,
                DateAdded = DateTimeOffset.UtcNow,
            };

            var isNewSet = !beatmapSetInfo.IsManaged;
            var importedBeatmaps = new List<BeatmapInfo>();

            foreach (var chart in chartsToImport)
            {
                var metadata = new BeatmapMetadata
                {
                    Title = chart.Metadata.SetTitle,
                    Artist = chart.Metadata.Artist,
                    Author = new RealmUser { Username = Constant.AUTHOR },
                };

                var chartFile = addFileUsage(beatmapSetInfo, chart.Path, chart.Content, fileStore, r);

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
                importedBeatmaps.Add(beatmapInfo);
            }

            var resourcePaths = importSet.ChartImports
                .SelectMany(c => c.Resources).DistinctBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var resourcePath in resourcePaths)
                addFileUsage(beatmapSetInfo, resourcePath, File.ReadAllBytes(resourcePath), fileStore, r);

            beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);

            if (isNewSet)
                r.Add(beatmapSetInfo);

            transaction.Commit();
            addImportedBeatmaps(existingBeatmapsByMd5, importedBeatmaps);

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
        ChartImport[] ChartImports,
        bool CanMergeWithActiveSet);

    private sealed class ChartImport
    {

        public string Path { get; }

        public byte[] Content { get; }

        public string[] Lines { get; }

        public string Md5Hash { get; }

        public string[] Resources { get; }

        public BmsChartMetadata Metadata { get; }

        public ChartImport(string directory, string path)
        {
            Path = path;
            Content = File.ReadAllBytes(path);
            Lines = BmsChartParser.ReadAllLines(Content);
            Md5Hash = BitConverter.ToString(MD5.HashData(Content)).Replace("-", string.Empty).ToLowerInvariant();
            Metadata = BmsChartParser.ScanMetadata(BmsChartParser.ReadAllLines(Content), path);
            Resources = BmsChartParser.ScanResourceReferences(Lines)
                .Select(resource => resolveResourcePath(directory, resource))
                .Where(File.Exists)
                .ToArray();
        }
    }
}
