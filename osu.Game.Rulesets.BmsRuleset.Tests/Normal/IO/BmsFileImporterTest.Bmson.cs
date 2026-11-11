using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using osu.Game.Collections;
using osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

public partial class BmsFileImporterTest
{
    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public void TestBmsonSkippedNotificationIncludesReasonsAndLimitsDetails(bool validSibling)
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var root = Path.Combine(storage.GetFullPath(string.Empty), "invalid-bmson");
            Directory.CreateDirectory(root);
            for (var index = 0; index < 7; index++)
                File.WriteAllBytes(Path.Combine(root, $"invalid-{index}.bmson"), BmsonParserTest.Chart(info: $",\"level\":{-index - 1}"));
            if (validSibling)
                File.WriteAllBytes(Path.Combine(root, "valid.bmson"), BmsonParserTest.Chart());

            using var notifications = new ImportNotificationOverlay();
            var logs = new ImportLogStorage(storage.GetFullPath("diagnostic-logs"));
            var previousLogStorage = Logger.Storage;
            var previousLoggingEnabled = Logger.Enabled;
            Logger.Flush();
            Logger.Storage = logs;
            Logger.Enabled = true;
            try
            {
                await new BmsFileImporter(realm, storage, notifications).Import(root).ConfigureAwait(false);
                var skipped = notifications.Posted.OfType<SimpleNotification>().Single();
                var text = skipped.Text.ToString();
                var progress = notifications.Posted.OfType<ProgressNotification>().Single();
                Assert.Multiple(() =>
                {
                    Assert.That(text, Does.Contain("Skipped 7 unreadable or unsupported charts"));
                    Assert.That(text, Does.Contain(Path.Combine(root, "invalid-0.bmson")));
                    Assert.That(text, Does.Contain("BMSON level must be a non-negative integer; received -1."));
                    Assert.That(text, Does.Contain("received -5."));
                    Assert.That(text, Does.Not.Contain("invalid-5.bmson"));
                    Assert.That(text, Does.Not.Contain("invalid-6.bmson"));
                    Assert.That(text.IndexOf("invalid-0.bmson", StringComparison.Ordinal), Is.LessThan(text.IndexOf("invalid-4.bmson", StringComparison.Ordinal)));
                    Assert.That(progress.State, Is.EqualTo(validSibling ? ProgressNotificationState.Completed : ProgressNotificationState.Cancelled));
                    Assert.That(realm.Run(r => r.All<BeatmapInfo>().Count()), Is.EqualTo(validSibling ? 1 : 0));
                });
                Assert.That(skipped.Activated, Is.Not.Null);
                Assert.That(skipped.Activated!(), Is.True);
                Assert.That(logs.OpenedFile, Is.EqualTo(Logger.GetLogger("bms").Filename));
                Assert.That(File.ReadAllText(logs.GetFullPath(logs.OpenedFile)), Does.Contain("invalid-6.bmson"));
                if (!validSibling)
                {
                    Assert.That(progress.CompletionClickAction, Is.Not.Null);
                    Assert.That(progress.CompletionClickAction!(), Is.True);
                }
            }
            finally
            {
                Logger.Flush();
                Logger.Storage = previousLogStorage;
                Logger.Enabled = previousLoggingEnabled;
            }
        });
    }

    [Test]
    public void TestBmsonLockedFileDoesNotAbortValidSiblings()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var root = Path.Combine(storage.GetFullPath(string.Empty), "locked-bmson");
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "valid.bmson"), BmsonParserTest.Chart());
            using var locked = new FileStream(Path.Combine(root, "locked.bmson"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            locked.Write(BmsonParserTest.Chart(info: ",\"level\":2"));
            var readError = Assert.Throws<IOException>(() => File.ReadAllBytes(locked.Name));

            using var notifications = new ImportNotificationOverlay();
            await new BmsFileImporter(realm, storage, notifications).Import(root).ConfigureAwait(false);

            Assert.That(realm.Run(r => r.All<BeatmapInfo>().Count()), Is.EqualTo(1));
            Assert.That(realm.Run(r => r.All<BeatmapSetInfo>().Count()), Is.EqualTo(1));
            var text = notifications.Posted.OfType<SimpleNotification>().Single().Text.ToString();
            Assert.That(text, Does.Contain("locked.bmson"));
            Assert.That(text, Does.Contain(readError!.Message));
        });
    }

    private sealed class ImportNotificationOverlay : INotificationOverlay, IDisposable
    {
        public List<Notification> Posted { get; } = [];
        public IBindable<int> UnreadCount { get; } = new Bindable<int>();
        public IEnumerable<Notification> AllNotifications => Posted;
        public void Post(Notification notification) => Posted.Add(notification);
        public void Hide()
        {
        }

        public void Dispose()
        {
            foreach (var notification in Posted)
                notification.Dispose();
        }
    }

    private sealed class ImportLogStorage(string path) : NativeStorage(path)
    {
        public string OpenedFile { get; private set; }

        public override bool OpenFileExternally(string filename)
        {
            OpenedFile = filename;
            return true;
        }
    }

    [Test]
    public void TestBmsonInvalidSiblingDoesNotAbortOtherChartsOrDirectories()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var root = Path.Combine(storage.GetFullPath(string.Empty), "mixed-bmson");
            for (var index = 0; index < 3; index++)
            {
                var directory = Path.Combine(root, index.ToString());
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "valid.bmson"), BmsonParserTest.Chart(info: $",\"level\":{index + 1}"));
                File.WriteAllText(Path.Combine(directory, "invalid.bmson"), "{ invalid json }");
            }
            using var notifications = new ImportNotificationOverlay();
            await new BmsFileImporter(realm, storage, notifications).Import(root).ConfigureAwait(false);
            Assert.That(realm.Run(r => r.All<BeatmapInfo>().Count()), Is.EqualTo(3));
            Assert.That(realm.Run(r => r.All<BeatmapSetInfo>().Count()), Is.EqualTo(3));
            var text = notifications.Posted.OfType<SimpleNotification>().Single().Text.ToString();
            Assert.That(text, Does.Contain("invalid.bmson"));
            Assert.That(text, Does.Contain("LineNumber:"));
            Assert.That(text, Does.Contain("BytePositionInLine:"));
        });
    }

    [Test]
    public void TestBmsonShaOnlyTableAssociatesCollectionsMarkersAndInstalledState()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var directory = Path.Combine(storage.GetFullPath(string.Empty), "sha-bmson");
            Directory.CreateDirectory(directory);
            var bytes = BmsonParserTest.Chart("""[{"name":"a.wav","notes":[{"x":1,"y":240}]}]""");
            File.WriteAllBytes(Path.Combine(directory, "chart.bmson"), bytes);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var sync = new CollectionSyncManager();
            var store = new DifficultyTableStore(null, Path.Combine(directory, "table-cache"), sync, realm);
            var table = new global::osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable.DifficultyTable
            {
                Name = "BMSON SHA", Symbol = "S",
                Entries = [new TableEntry { Md5Hash = sha, Sha256Hash = sha, Level = "1" }],
            };
            store.RestoreTable(table);
            var previous = BmsRulesetRuntime.DifficultyTableStore;
            BmsRulesetRuntime.DifficultyTableStore = store;
            try
            {
                await new BmsFileImporter(realm, storage).Import(directory).ConfigureAwait(false);
                var info = realm.Run(r => r.All<BeatmapInfo>().Single().Detach());
                Assert.That(info.DifficultyName, Does.Contain("[S1]"));
                Assert.That(store.GetMarkers(info.MD5Hash, info.Hash), Has.Count.EqualTo(1));
                Assert.That(realm.Run(r => r.All<BeatmapCollection>().Single().BeatmapMD5Hashes.Single()), Is.EqualTo(info.MD5Hash));
                Assert.That(UnavailableTableBeatmapFactory.Create(store, info.Ruleset, [info]), Is.Empty);
                new DifficultyNameUpdater(realm, store).RefreshAllMarkers();
                Assert.That(realm.Run(r => r.All<BeatmapInfo>().Single().DifficultyName), Does.Contain("[S1]"));
            }
            finally
            {
                BmsRulesetRuntime.DifficultyTableStore = previous;
            }
        });
    }

    [TestCase("", "chart")]
    [TestCase(",\"chart_name\":\"ANOTHER\"", "ANOTHER")]
    public void TestBmsonImportUsesChartLabelOrFilenameWithoutSplittingSongTitle(string chartName, string expected)
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var directory = Path.Combine(storage.GetFullPath(string.Empty), "bmson-name");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "chart.bmson");
            var bytes = BmsonParserTest.Chart(info: chartName);
            Assert.That(BmsonParser.Parse(bytes).ExplicitDifficultyName, Is.EqualTo(chartName.Length == 0 ? "" : "ANOTHER"));
            File.WriteAllBytes(path, bytes);
            await new BmsFileImporter(realm, storage).Import(directory).ConfigureAwait(false);
            var info = realm.Run(r => r.All<BeatmapSetInfo>().Single().Beatmaps.Single().Detach());
            Assert.That(info.Metadata.Title, Is.EqualTo("Song [title]"));
            Assert.That(info.DifficultyName, Is.EqualTo(expected));
            var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, info);
            Assert.That(new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert().BeatmapInfo.DifficultyName, Is.EqualTo(expected));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void TestBmsonWorkingBeatmapUsesImportedFileStoreAndOneDecodedChart(bool missingExternal, bool invalidExternal)
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var directory = Path.Combine(storage.GetFullPath(string.Empty), "bmson-loading");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "stage.png"), []);
            File.WriteAllBytes(Path.Combine(directory, "changed.png"), []);
            var path = Path.Combine(directory, "chart.bmson");
            File.WriteAllBytes(path, BmsonParserTest.Chart("""[{"name":"a.wav","notes":[{"x":1,"y":240}]}]""",
                info: ",\"chart_name\":\"ANOTHER\",\"eyecatch_image\":\"stage.png\""));
            await new BmsFileImporter(realm, storage).Import(directory).ConfigureAwait(false);
            var info = realm.Run(r => r.All<BeatmapSetInfo>().Single().Beatmaps.Single().Detach());
            if (missingExternal || invalidExternal)
            {
                // Replacing the external file leaves any hard-linked imported copy intact.
                File.Delete(path);
                if (invalidExternal)
                    File.WriteAllText(path, "{}");
            }

            using var files = new StorageBackedResourceStore(storage.GetStorageForDirectory("files"));
            var cache = new WorkingBeatmapCache(null, null, null, files, new FlatWorkingBeatmap(new Beatmap()), realm: realm);
            var working = new BmsWorkingBeatmap(cache.GetWorkingBeatmap(info), null);
            var decoded = working.Beatmap;
            Assert.That(decoded.HitObjects.Single().StartTime, Is.EqualTo(500));
            Assert.That(decoded.BeatmapInfo.DifficultyName, Is.EqualTo("ANOTHER"));
            Assert.That(decoded.BeatmapInfo.MD5Hash, Is.EqualTo(info.MD5Hash));
            Assert.That(working.GetPlayableBeatmap(new BmsRuleset().RulesetInfo).HitObjects, Has.Count.EqualTo(1));

            File.Delete(path);
            File.WriteAllBytes(path, BmsonParserTest.Chart(info: ",\"eyecatch_image\":\"changed.png\""));
            await working.PrepareBackgroundMetadataAsync().ConfigureAwait(false);
            Assert.That(working.Metadata.BackgroundFile, Is.EqualTo("stage.png"));
            Assert.That(working.Beatmap, Is.SameAs(decoded));
        });
    }

    [Test]
    public void TestBmsonImportsAlongsideBmsWithOriginalHashesAndChartNames()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var directory = Path.Combine(storage.GetFullPath(string.Empty), "bmson-import");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "chart.BMSON");
            var bytes = BmsonParserTest.Chart("""[{"name":"stem.wav","notes":[{"x":1,"y":240},{"x":2,"y":480,"c":true}]}]""",
                info: ""","chart_name":"ANOTHER","judge_rank":150""");
            File.WriteAllBytes(path, bytes);
            File.WriteAllBytes(Path.Combine(directory, "duplicate.bmson"), bytes);
            File.WriteAllText(Path.Combine(directory, "legacy.bms"), "#TITLE Song [title]\n#SUBTITLE HYPER\n#BPM 120\n#00111:01");
            var importer = new BmsFileImporter(realm, storage);
            await importer.Import(directory).ConfigureAwait(false);
            await importer.Import(path).ConfigureAwait(false);
            var imported = realm.Run(r =>
            {
                var set = r.All<BeatmapSetInfo>().AsEnumerable().Single();
                var chart = set.Beatmaps.Single(beatmap => beatmap.MD5Hash == computeMd5(path));
                var difficulty = BmsDifficultyInfo.FromOsuDifficulty(chart.Difficulty);
                return (Count: set.Beatmaps.Count, Title: chart.Metadata.Title, chart.DifficultyName, chart.MD5Hash,
                    chart.Metadata.Source, chart.TotalObjectCount, chart.StarRating,
                    Rate: BmsJudgementProfileProvider.RateForExRank(BmsLayout.VariantFromTotalColumns(difficulty.KeyCount), difficulty.ExRank!.Value));
            });
            Assert.That(imported.Count, Is.EqualTo(2));
            Assert.That(imported.Title, Is.EqualTo("Song [title]"));
            Assert.That(imported.DifficultyName, Does.Contain("ANOTHER"));
            Assert.That(imported.MD5Hash, Is.EqualTo(computeMd5(path)));
            Assert.That(imported.Source, Is.EqualTo(directory));
            Assert.That(imported.TotalObjectCount, Is.EqualTo(2));
            Assert.That(imported.StarRating, Is.GreaterThan(0));
            Assert.That(imported.Rate, Is.EqualTo(1.5));
        });
    }
}
