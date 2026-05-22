using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class BmsFileImporterTest
{
    private static void runImportTest(Func<RealmAccess, TemporaryNativeStorage, Task> action)
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(BmsFileImporterTest)}-{Guid.NewGuid()}");

        Exception exception = null!;

        host.Run(new RealmImportTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(BmsFileImporterTest)}-{Guid.NewGuid()}", host);
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

    private sealed record ImportedSetSnapshot(
        int BeatmapCount,
        int FileCount,
        string[] DifficultyNames,
        (string DifficultyName, float CircleSize, int Variant)[] Layouts,
        (string MD5Hash, string Hash, string Path)[] ChartHashes,
        string[] FileNames,
        int DistinctRealmFileHashes);

    private sealed record ImportedBeatmapPathSnapshot(string BeatmapPath, string StoredPath, string MD5Hash);

    [Test]
    public void TestHandledExtensions()
    {
        using var storage = new TemporaryNativeStorage($"{nameof(BmsFileImporterTest)}-{Guid.NewGuid()}");
        using var realm = new RealmAccess(storage, "client.realm");

        var importer = new BmsFileImporter(realm, storage);

        Assert.That(importer.HandledExtensions, Is.EquivalentTo(new[] { ".bms", ".bme", ".bml", ".pms" }));
    }

    [Test]
    public void TestImportedRealSampleFileLoadsHitObjectsFromStoredBeatmapPath()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var importer = new BmsFileImporter(realm, storage);
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");

            await importer.Import(chartPath).ConfigureAwait(false);

            var snapshot = realm.Run(r =>
            {
                var set = r.All<BeatmapSetInfo>().Single();
                var beatmap = set.Beatmaps.Single();
                var storedPath = set.GetPathForFile(beatmap.Path!);

                return new ImportedBeatmapPathSnapshot(beatmap.Path, storedPath, beatmap.MD5Hash);
            });

            Assert.That(snapshot.BeatmapPath, Is.EqualTo("_7NORMAL.bms"));
            Assert.That(snapshot.StoredPath, Is.Not.Null.And.Not.Empty);

            using var stream = storage.GetStream($"files/{snapshot.StoredPath}");
            using var reader = new LineBufferedReader(stream);

            var decoded = new BmsBeatmapDecoder().Decode(reader);

            Assert.That(decoded.HitObjects.OfType<BmsHitObject>().Count(), Is.GreaterThan(100));
            Assert.That(decoded.Metadata.Title, Is.EqualTo("Aleph-0[NORMAL]"));
        });
    }

    [Test]
    public void TestImportRealSampleDirectoryCreatesOneSetWithAllChartsAndResources()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var importer = new BmsFileImporter(realm, storage);
            var directory = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)");

            await importer.Import(directory).ConfigureAwait(false);

            var result = realm.Run(r =>
            {
                var set = r.All<BeatmapSetInfo>().Single();

                return new ImportedSetSnapshot(
                    set.Beatmaps.Count,
                    set.Files.Count,
                    set.Beatmaps.Select(b => b.DifficultyName).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    set.Beatmaps.Select(b => (b.DifficultyName, b.Difficulty.CircleSize, new BmsRuleset().GetVariantForBeatmap(b))).ToArray(),
                    set.Beatmaps.Select(b => (b.MD5Hash, b.Hash, b.Path)).ToArray(),
                    set.Files.Select(f => f.Filename).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                    set.Files.Select(f => f.File.Hash).Distinct().Count());
            });

            var chartPaths = Directory.EnumerateFiles(directory, "*.bms", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
            var expectedChartHashes = chartPaths.Select(computeMd5).OrderBy(h => h, StringComparer.Ordinal).ToArray();
            var importedChartHashes = result.ChartHashes.Select(h => h.MD5Hash).OrderBy(h => h, StringComparer.Ordinal).ToArray();

            Assert.That(result.BeatmapCount, Is.EqualTo(8));
            Assert.That(result.FileCount, Is.GreaterThan(100));
            Assert.That(result.DifficultyNames, Does.Contain("NORMAL"));
            Assert.That(result.DifficultyNames, Does.Contain("14ANOTHER"));
            Assert.That(importedChartHashes, Is.EqualTo(expectedChartHashes));
            Assert.That(result.ChartHashes.All(h => h.MD5Hash != h.Hash), Is.True);
            Assert.That(result.ChartHashes.All(h => h.Path != null), Is.True);
            Assert.That(result.FileNames, Does.Contain("_7NORMAL.bms"));
            Assert.That(result.FileNames, Does.Contain("_14ANOTHER.bms"));
            Assert.That(result.FileNames, Does.Contain("kick_deep2.ogg"));
            Assert.That(result.FileNames, Does.Contain("_title.png"));
            Assert.That(result.DistinctRealmFileHashes, Is.LessThanOrEqualTo(result.FileCount));
            Assert.That(result.Layouts.Single(l => l.DifficultyName == "NORMAL").CircleSize, Is.EqualTo(8));
            Assert.That(result.Layouts.Single(l => l.DifficultyName == "NORMAL").Variant, Is.EqualTo((int)BmsLayoutVariant.Bme7K));
            Assert.That(result.Layouts.Single(l => l.DifficultyName == "14ANOTHER").CircleSize, Is.EqualTo(16));
            Assert.That(result.Layouts.Single(l => l.DifficultyName == "14ANOTHER").Variant, Is.EqualTo((int)BmsLayoutVariant.Bme7KDouble));
        });
    }

    [Test]
    public void TestImportRealSampleFileImportsSiblingSetResources()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var importer = new BmsFileImporter(realm, storage);
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");

            await importer.Import(chartPath).ConfigureAwait(false);

            var result = realm.Run(r =>
            {
                var set = r.All<BeatmapSetInfo>().Single();

                return new ImportedSetSnapshot(
                    set.Beatmaps.Count,
                    set.Files.Count,
                    set.Beatmaps.Select(b => b.DifficultyName).ToArray(),
                    set.Beatmaps.Select(b => (b.DifficultyName, b.Difficulty.CircleSize, new BmsRuleset().GetVariantForBeatmap(b))).ToArray(),
                    set.Beatmaps.Select(b => (b.MD5Hash, b.Hash, b.Path)).ToArray(),
                    set.Files.Select(f => f.Filename).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                    set.Files.Select(f => f.File.Hash).Distinct().Count());
            });

            Assert.That(result.BeatmapCount, Is.EqualTo(1));
            Assert.That(result.DifficultyNames, Is.EqualTo(new[] { "NORMAL" }));
            Assert.That(result.Layouts.Single().CircleSize, Is.EqualTo(8));
            Assert.That(result.Layouts.Single().Variant, Is.EqualTo((int)BmsLayoutVariant.Bme7K));
            Assert.That(result.FileNames, Does.Contain("_7NORMAL.bms"));
            Assert.That(result.FileNames, Does.Contain("kick_deep2.ogg"));
            Assert.That(result.FileNames, Does.Contain("_title.png"));
        });
    }

    [Test]
    public void TestReimportReplacesBrokenLegacyHashImport()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var importer = new BmsFileImporter(realm, storage);
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");

            await importer.Import(chartPath).ConfigureAwait(false);

            realm.Write(r =>
            {
                var beatmap = r.All<BeatmapInfo>().Single();
                beatmap.Hash = beatmap.MD5Hash;
            });

            await importer.Import(chartPath).ConfigureAwait(false);

            var result = realm.Run(r =>
            {
                var sets = r.All<BeatmapSetInfo>().AsEnumerable().ToArray();
                var active = sets.Single(s => !s.DeletePending);
                var old = sets.Single(s => s.DeletePending);
                var beatmap = active.Beatmaps.Single();

                return (SetCount: sets.Length, OldDeleted: old.DeletePending, beatmap.Path, FileResolved: beatmap.File != null,
                    HashEqualsMd5: beatmap.Hash == beatmap.MD5Hash);
            });

            Assert.That(result.SetCount, Is.EqualTo(2));
            Assert.That(result.OldDeleted, Is.True);
            Assert.That(result.Path, Is.EqualTo("_7NORMAL.bms"));
            Assert.That(result.FileResolved, Is.True);
            Assert.That(result.HashEqualsMd5, Is.False);
        });
    }

    [Test]
    public void TestReimportRestoresSoftDeletedSet()
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);

            var importer = new BmsFileImporter(realm, storage);
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");

            await importer.Import(chartPath).ConfigureAwait(false);

            realm.Write(r =>
            {
                var set = r.All<BeatmapSetInfo>().Single();
                set.DeletePending = true;
            });

            await importer.Import(chartPath).ConfigureAwait(false);

            var result = realm.Run(r =>
            {
                var sets = r.All<BeatmapSetInfo>().AsEnumerable().ToArray();
                return (SetCount: sets.Length, ActiveCount: sets.Count(s => !s.DeletePending), FileResolved: sets.Single().Beatmaps.Single().File != null);
            });

            Assert.That(result.SetCount, Is.EqualTo(1));
            Assert.That(result.ActiveCount, Is.EqualTo(1));
            Assert.That(result.FileResolved, Is.True);
        });
    }
}
