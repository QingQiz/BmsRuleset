using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.ImportExport;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

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
                    .Where(b => b.Ruleset.ShortName == "bms")
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
                    .Where(b => b.Ruleset.ShortName == "bms")
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
                    .FirstOrDefault(b => b.Ruleset.ShortName == "bms");
                Assert.That(beatmap, Is.Not.Null);
                Assert.That(beatmap!.DifficultyName, Does.Contain("[RT★1]"));
            });

            // Remove the table — markers should vanish.
            store.RemoveTable(importResult!.Table);
            updater.RefreshAllMarkers();

            realm.Run(r =>
            {
                var beatmap = r.All<BeatmapInfo>().AsEnumerable()
                    .FirstOrDefault(b => b.Ruleset.ShortName == "bms");
                Assert.That(beatmap, Is.Not.Null);
                Assert.That(beatmap!.DifficultyName, Does.Not.Contain("[RT"),
                    "Marker should be removed after table removal");
            });
        });
    }
}
