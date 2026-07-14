using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.ImportExport;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.DifficultyTable;

[TestFixture]
public partial class BmsDifficultyTableIntegrationTest
{
    private static void runIntegrationTest(Func<RealmAccess, TemporaryNativeStorage, Task> action)
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(BmsDifficultyTableIntegrationTest)}-{Guid.NewGuid()}");

        Exception exception = null!;

        host.Run(new RealmImportTestGame(async () =>
        {
            try
            {
                // ReSharper disable once AccessToDisposedClosure
                using var storage = new TemporaryNativeStorage($"{nameof(BmsDifficultyTableIntegrationTest)}-{Guid.NewGuid()}", host);
                using var realm = new RealmAccess(storage, "client.realm");

                await action(realm, storage).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                exception = e;
            }
        }));

        if (exception != null)
            throw exception;
    }

    private partial class RealmImportTestGame(Func<Task> work) : Framework.Game
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            // ReSharper disable once AsyncVoidLambda
            Scheduler.Add(async () =>
            {
                await work().ConfigureAwait(true);
                Exit();
            });
        }
    }

    private static void addBmsRuleset(RealmAccess realm)
    {
        var ruleset = new BmsRuleset();
        var info = ruleset.RulesetInfo;

        realm.Write(r => r.Add(new RulesetInfo(info.ShortName, info.Name, info.InstantiationInfo, info.OnlineID)
        {
            Available = true,
        }));
    }

    private static string computeMd5(string path) =>
        BitConverter.ToString(MD5.HashData(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant();

    [Test]
    public void TestDifficultyTableMarkersMatchImportedBeatmaps()
    {
        runIntegrationTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            // Select one chart from each of the three test directories.
            var chartPaths = new[]
            {
                Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Destr0yer (by 削除 feat. Nikki Simmons)", "destr0yer_starhyper.bms"),
                Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms"),
                Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "[Clue]Random", "_random_s2.bms"),
            };

            // ReSharper disable once InconsistentNaming
            var md5s = chartPaths.Select(computeMd5).ToArray();

            // Import the three charts into realm via BmsFileImporter.
            var importer = new BmsFileImporter(realm, storage);
            foreach (var path in chartPaths)
                await importer.Import(path).ConfigureAwait(false);

            // Verify that each chart was imported and we know its MD5.
            realm.Run(r =>
            {
                var bmsBeatmaps = r.All<BeatmapInfo>().AsEnumerable()
                    .Where(b => b.Ruleset.ShortName == Constant.SHORT_NAME)
                    .ToList();

                Assert.That(bmsBeatmaps, Has.Count.EqualTo(3),
                    "Expected exactly 3 imported BMS beatmaps");

                foreach (var md5 in md5s)
                {
                    var match = bmsBeatmaps.FirstOrDefault(b =>
                        b.MD5Hash.Equals(md5, StringComparison.OrdinalIgnoreCase));
                    Assert.That(match, Is.Not.Null,
                        $"Imported beatmap with MD5 {md5} should exist in realm");
                }
            });

            // Build a difficulty table JSON that references those three MD5s.
            var tableJson = $@"{{
    ""name"": ""Integration Test"",
    ""symbol"": ""IT"",
    ""level_order"": [""★1"", ""★2""],
    ""data_url"": """",
    ""charts"": [
        {{ ""level"": ""★1"", ""md5"": ""{md5s[0]}"", ""title"": ""Destr0yer"", ""artist"": ""削除"" }},
        {{ ""level"": ""★2"", ""md5"": ""{md5s[1]}"", ""title"": ""Aleph-0"", ""artist"": ""LeaF"" }},
        {{ ""level"": ""★1"", ""md5"": ""{md5s[2]}"", ""title"": ""Random"", ""artist"": ""Clue"" }}
    ]
}}";

            var tablePath = Path.Combine(storage.GetFullPath(string.Empty), "integration-table.json");
            await File.WriteAllTextAsync(tablePath, tableJson).ConfigureAwait(false);

            var notification = new ProgressNotification();
            // Load the table through DifficultyTableStore.
            var cacheDir = Path.Combine(storage.GetFullPath(string.Empty), "dt-cache");
            var store = new DifficultyTableStore(null, cacheDir);
            var result = await store.ImportAsync(tablePath, notification).ConfigureAwait(false);

            Assert.That(result, Is.Not.Null, "Table should load successfully");
            Assert.That(result!.Table.Entries, Has.Count.EqualTo(3));

            // Set up the DifficultyNameUpdater and refresh markers.
            var updater = new DifficultyNameUpdater(realm, store);
            updater.RefreshAllMarkers();

            // Verify markers are present on all three imported charts.
            realm.Run(r =>
            {
                var bmsBeatmaps = r.All<BeatmapInfo>().AsEnumerable()
                    .Where(b => b.Ruleset.ShortName == Constant.SHORT_NAME)
                    .ToList();

                // Determine expected level for each chart based on the table JSON above.
                var expectations = new (string Md5, string ExpectedLevel)[]
                {
                    (md5s[0], "★1"),
                    (md5s[1], "★2"),
                    (md5s[2], "★1"),
                };

                foreach (var (expectedMd5, expectedLevel) in expectations)
                {
                    var beatmap = bmsBeatmaps.FirstOrDefault(b =>
                        b.MD5Hash.Equals(expectedMd5, StringComparison.OrdinalIgnoreCase));

                    Assert.That(beatmap, Is.Not.Null, $"Beatmap with MD5 {expectedMd5} should exist");

                    var marker = $"[IT{expectedLevel}]";
                    Assert.That(beatmap!.DifficultyName, Does.Contain(marker),
                        $"Beatmap '{beatmap.DifficultyName}' should have marker '{marker}'");
                }
            });
        });
    }

    [Test]
    public void TestRemovingTableRemovesMarkers()
    {
        runIntegrationTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            // Import a single chart.
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
            var md5 = computeMd5(chartPath);

            var importer = new BmsFileImporter(realm, storage);
            await importer.Import(chartPath).ConfigureAwait(false);

            // Load a difficulty table referencing that chart.
            var tableJson = $@"{{
    ""name"": ""Removal Test"",
    ""symbol"": ""RT"",
    ""level_order"": [""★1""],
    ""charts"": [
        {{ ""level"": ""★1"", ""md5"": ""{md5}"", ""title"": ""Aleph-0"" }}
    ]
}}";

            var tablePath = Path.Combine(storage.GetFullPath(string.Empty), "removal-table.json");
            await File.WriteAllTextAsync(tablePath, tableJson).ConfigureAwait(false);

            var notification = new ProgressNotification();
            var cacheDir = Path.Combine(storage.GetFullPath(string.Empty), "dt-cache");
            var store = new DifficultyTableStore(null, cacheDir);
            var importResult = await store.ImportAsync(tablePath, notification).ConfigureAwait(false);
            Assert.That(importResult, Is.Not.Null);

            var updater = new DifficultyNameUpdater(realm, store);
            updater.RefreshAllMarkers();

            // Verify marker was applied.
            realm.Run(r =>
            {
                var beatmap = r.All<BeatmapInfo>().AsEnumerable()
                    .FirstOrDefault(b => b.Ruleset.ShortName == Constant.SHORT_NAME);
                Assert.That(beatmap, Is.Not.Null);
                Assert.That(beatmap!.DifficultyName, Does.Contain("[RT★1]"));
            });

            // Remove the table — markers should vanish.
            store.RemoveTable(importResult!.Table);
            updater.RefreshAllMarkers();

            realm.Run(r =>
            {
                var beatmap = r.All<BeatmapInfo>().AsEnumerable()
                    .FirstOrDefault(b => b.Ruleset.ShortName == Constant.SHORT_NAME);
                Assert.That(beatmap, Is.Not.Null);
                Assert.That(beatmap!.DifficultyName, Does.Not.Contain("[RT"),
                    "Marker should be removed after table removal");
            });
        });
    }

    [Test]
    public void TestRefreshWithoutMarkersPreservesDifficultyName()
    {
        runIntegrationTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
            var importer = new BmsFileImporter(realm, storage);
            await importer.Import(chartPath).ConfigureAwait(false);

            string originalName = null!;
            realm.Run(r => originalName = r.All<BeatmapInfo>().Single().DifficultyName);

            var store = new DifficultyTableStore(null, Path.Combine(storage.GetFullPath(string.Empty), "dt-cache"));
            new DifficultyNameUpdater(realm, store).RefreshAllMarkers();

            realm.Run(r => Assert.That(r.All<BeatmapInfo>().Single().DifficultyName, Is.EqualTo(originalName)));
        });
    }

    [Test]
    public void TestMarkerRefreshPreservesBracketedDifficultyName()
    {
        runIntegrationTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
            var md5 = computeMd5(chartPath);
            var importer = new BmsFileImporter(realm, storage);
            await importer.Import(chartPath).ConfigureAwait(false);

            const string original_name = "Another [7K]";
            realm.Write(r => r.All<BeatmapInfo>().Single().DifficultyName = original_name);

            var store = new DifficultyTableStore(null, Path.Combine(storage.GetFullPath(string.Empty), "dt-cache"));
            var table = new global::osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable
            {
                Name = "Bracket Preservation Test",
                Symbol = "RT",
                SourcePath = "bracket-preservation-test",
                LevelOrder = ["★1"],
                Entries = [new TableEntry { Level = "★1", Md5Hash = md5 }],
            };
            store.AddTable(table);

            var updater = new DifficultyNameUpdater(realm, store);
            updater.RefreshAllMarkers();

            realm.Run(r => Assert.That(r.All<BeatmapInfo>().Single().DifficultyName,
                Is.EqualTo($"{original_name}\u200B [RT★1]")));

            store.RemoveTable(table);
            updater.RefreshAllMarkers();

            realm.Run(r => Assert.That(r.All<BeatmapInfo>().Single().DifficultyName, Is.EqualTo(original_name)));
        });
    }

    [Test]
    public void TestMergingSubdividedCollectionKeepsSurvivingRealmObjectAttached()
    {
        runIntegrationTest((realm, storage) =>
        {
            var syncManager = new CollectionSyncManager();
            var store = new DifficultyTableStore(null, Path.Combine(storage.GetFullPath(string.Empty), "dt-cache"), syncManager, realm);
            var table = new global::osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable
            {
                Name = "Collection Identity Test",
                Symbol = "IT",
                SourcePath = "collection-identity-test",
                LevelOrder = ["1", "2"],
                Entries =
                [
                    new TableEntry { Level = "1", Md5Hash = "11111111111111111111111111111111" },
                    new TableEntry { Level = "2", Md5Hash = "22222222222222222222222222222222" },
                ],
            };

            var previousStore = BmsRulesetRuntime.DifficultyTableStore;
            BmsRulesetRuntime.DifficultyTableStore = store;

            try
            {
                store.AddTable(table);
                syncManager.ToggleSubdivide(realm, table);

                BeatmapCollection survivingCollection = null!;
                Guid survivingId = Guid.Empty;

                realm.Run(r =>
                {
                    survivingCollection = r.All<BeatmapCollection>()
                        .OrderBy(c => c.Name)
                        .First();
                    survivingId = survivingCollection.ID;
                });

                syncManager.ToggleSubdivide(realm, table);

                Assert.DoesNotThrow(() => _ = survivingCollection.ID,
                    "Collection UI may still access the old Realm object while processing the change notification");

                realm.Run(r => Assert.That(r.All<BeatmapCollection>().Single().ID, Is.EqualTo(survivingId)));
            }
            finally
            {
                BmsRulesetRuntime.DifficultyTableStore = previousStore;
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public void TestSubdivideMatchesInferredLevelsIgnoringCase()
    {
        runIntegrationTest((realm, storage) =>
        {
            var table = BmsTableJsonParser.Merge("case-insensitive-levels", TableSource.LocalFile,
                new RawTableData { Name = "Case-insensitive Levels" },
                [
                    new RawChartItem { Level = "st2", Md5 = "11111111111111111111111111111111" },
                    new RawChartItem { Level = "ST2", Md5 = "22222222222222222222222222222222" },
                ]);

            Assert.That(table, Is.Not.Null);
            string[] expectedLevelOrder = ["st2"];
            Assert.That(table!.LevelOrder, Is.EqualTo(expectedLevelOrder));

            var syncManager = new CollectionSyncManager();
            var store = new DifficultyTableStore(null, Path.Combine(storage.GetFullPath(string.Empty), "dt-cache"), syncManager, realm);
            var previousStore = BmsRulesetRuntime.DifficultyTableStore;
            BmsRulesetRuntime.DifficultyTableStore = store;

            try
            {
                store.AddTable(table);
                syncManager.ToggleSubdivide(realm, table);

                string[] expectedHashes =
                [
                    "11111111111111111111111111111111",
                    "22222222222222222222222222222222",
                ];

                realm.Run(r => Assert.That(r.All<BeatmapCollection>().Single().BeatmapMD5Hashes,
                    Is.EquivalentTo(expectedHashes)));
            }
            finally
            {
                BmsRulesetRuntime.DifficultyTableStore = previousStore;
            }

            return Task.CompletedTask;
        });
    }
}
