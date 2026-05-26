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

                var imported = 0;
                for (var i = 0; i < importSets.Length; i++)
                {
                    if (importSet(importSets[i], fileStore))
                        imported++;

                    notification.Text = $"Imported {imported} of {importSets.Length} BMS sets";
                    notification.Progress = (float)(i + 1) / importSets.Length;
                }

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
            var resources = collectResourceFiles(group.Key, charts);

            yield return new ImportSet(group.Key, charts, resources);
        }
    }

    private static IEnumerable<string> expandPathToCharts(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly).Where(isChartFile);
        }

        return File.Exists(path) && isChartFile(path) ? [path] : [];
    }

    private static string[] collectResourceFiles(string directory, IEnumerable<string> chartPaths)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var chartPath in chartPaths)
        {
            foreach (var resource in parseReferencedResources(chartPath))
            {
                var path = resolveResourcePath(directory, resource);

                if (File.Exists(path))
                    referenced.Add(path);
            }
        }

        return referenced.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string resolveResourcePath(string directory, string resource)
    {
        var path = Path.Combine(directory, resource);

        if (File.Exists(path))
            return path;

        var pathWithoutExtension = Path.Combine(directory, Path.ChangeExtension(resource, null));

        return findExistingResourceWithAnyExtension(pathWithoutExtension) ?? path;
    }

    private static IEnumerable<string> parseReferencedResources(string chartPath) => BmsChartParser.ScanResourceReferences(BmsChartParser.ReadAllLines(chartPath));

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

    private static BmsChartMetadata parseChartMetadata(string chartPath) => BmsChartParser.ScanMetadata(BmsChartParser.ReadAllLines(chartPath), chartPath);

    private static bool isChartFile(string path) => Constant.BMS_EXTENSIONS.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool isBrokenLegacyBmsImport(BeatmapInfo beatmap) =>
        beatmap.Ruleset.ShortName == "bms" && beatmap.BeatmapSet != null && beatmap.File == null;

    private static BeatmapSetInfo? findCompatibleActiveSet(Realm realm, string directory, IEnumerable<ChartImport> charts)
    {
        var directoryName = Path.GetFileName(directory);
        var setTitle = charts.Select(c => c.Metadata.SetTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
        var artist = charts.Select(c => c.Metadata.Artist).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        return realm.All<BeatmapSetInfo>().AsEnumerable()
            .Where(s => !s.DeletePending && !s.Protected)
            .Where(s => s.Beatmaps.Any(b => b.Ruleset.ShortName == "bms"))
            .Where(s => s.Files.Any(f => string.Equals(Path.GetDirectoryName(f.Filename), directoryName, StringComparison.OrdinalIgnoreCase))
                        || s.Beatmaps.Any(b => string.Equals(b.Metadata.Title, setTitle, StringComparison.OrdinalIgnoreCase)
                                               && (string.IsNullOrWhiteSpace(artist) || string.Equals(b.Metadata.Artist, artist, StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault();
    }

    private static string calculateSetHash(BeatmapSetInfo beatmapSetInfo) => string.Join(string.Empty, beatmapSetInfo.Beatmaps
        .Select(b => b.MD5Hash)
        .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();

    private bool importSet(ImportSet importSet, RealmFileStore fileStore)
    {
        try
        {
            var charts = importSet.ChartPaths.Select(path => new ChartImport(path, File.ReadAllBytes(path))).ToArray();

            if (charts.Length == 0)
                return false;

            var chartHashes = charts.Select(c => c.Md5Hash).ToArray();

            foreach (var chart in charts)
                chart.Metadata = parseChartMetadata(chart.Path);

            return realm.Run(r =>
            {
                using var transaction = r.BeginWrite();

                var rulesetInfo = r.Find<RulesetInfo>("bms");
                if (rulesetInfo?.Available != true)
                {
                    Logger.Log("BMS import: ruleset 'bms' is not available in realm");
                    return false;
                }

                var existingBeatmaps = r.All<BeatmapInfo>().AsEnumerable()
                    .Where(b => chartHashes.Contains(b.MD5Hash) && b.BeatmapSet is { DeletePending: false })
                    .ToArray();
                var existing = existingBeatmaps.FirstOrDefault();
                var replacingBrokenLegacy = false;

                if (existing != null)
                {
                    if (isBrokenLegacyBmsImport(existing))
                    {
                        Logger.Log($"BMS import: replacing legacy broken set {Path.GetFileName(importSet.Directory)}");
                        existing.BeatmapSet!.DeletePending = true;
                        replacingBrokenLegacy = true;
                    }
                }

                var chartsToImport = replacingBrokenLegacy ? charts : charts.Where(c => !existingBeatmaps.Any(b => b.MD5Hash == c.Md5Hash)).ToArray();

                if (chartsToImport.Length == 0)
                {
                    Logger.Log($"BMS import: skipping duplicate set {Path.GetFileName(importSet.Directory)}");
                    return true;
                }

                var beatmapSetInfo = replacingBrokenLegacy ? null : existing?.BeatmapSet;

                beatmapSetInfo ??= findCompatibleActiveSet(r, importSet.Directory, charts) ?? new BeatmapSetInfo
                {
                    OnlineID = -1,
                    DateAdded = DateTimeOffset.UtcNow,
                };

                var isNewSet = !beatmapSetInfo.IsManaged;

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
                }

                foreach (var resourcePath in importSet.ResourcePaths)
                    addFileUsage(beatmapSetInfo, resourcePath, File.ReadAllBytes(resourcePath), fileStore, r);

                beatmapSetInfo.Hash = calculateSetHash(beatmapSetInfo);

                if (isNewSet)
                    r.Add(beatmapSetInfo);

                transaction.Commit();

                Logger.Log($"BMS import: imported {Path.GetFileName(importSet.Directory)} ({charts.Length} charts, {importSet.ResourcePaths.Count} resources)");
                return true;
            });
        }
        catch (Exception e)
        {
            Logger.Log($"BMS import: failed to import {importSet.Directory}: {e.Message}");
            return false;
        }
    }

    private sealed record ImportSet(string Directory, IReadOnlyList<string> ChartPaths, IReadOnlyList<string> ResourcePaths);

    private sealed class ChartImport(string path, byte[] content)
    {
        public string Path { get; } = path;

        public byte[] Content { get; } = content;

        public string Md5Hash { get; } = BitConverter.ToString(MD5.HashData(content)).Replace("-", string.Empty).ToLowerInvariant();

        public BmsChartMetadata Metadata { get; set; } = new(System.IO.Path.GetFileNameWithoutExtension(path), string.Empty, "BMS", BmsLayout.BMS5_KEY_COLUMNS,
            System.IO.Path.GetFileNameWithoutExtension(path));
    }
}
