using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.IO.Stores;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.IO.Import;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.IO;

public partial class BmsFileImporterTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestImportedNamesSurviveExternalAndStoredLoadingAndBranchConversion(bool stored)
    {
        runImportTest(async (realm, storage) =>
        {
            addBmsRuleset(realm);
            var directory = Path.Combine(storage.GetFullPath(string.Empty), "named-charts");
            Directory.CreateDirectory(directory);
            var normalPath = Path.Combine(directory, "normal.bms");
            var hyperPath = Path.Combine(directory, "hyper.bms");
            File.WriteAllText(normalPath, "#TITLE Song [NORMAL]\n#BPM 120\n#00111:01");
            File.WriteAllText(hyperPath, """
                #TITLE Song [HYPER]
                #BPM 120
                #RANDOM 2
                #IF 1
                #RANK 2
                #00111:01
                #ENDIF
                #IF 2
                #SUBTITLE [BRANCH]
                #RANK 4
                #00112:01
                #ENDIF
                #ENDRANDOM
                """);
            await new BmsFileImporter(realm, storage).Import(directory).ConfigureAwait(false);
            var markedName = DifficultyNameUpdater.AddMarkerSuffix("HYPER", "★12");
            realm.Write(r =>
            {
                var set = r.All<BeatmapSetInfo>().Single();
                Assert.That(set.Metadata.Title, Is.EqualTo("Song"));
                Assert.That(set.Beatmaps.Select(chart => chart.DifficultyName), Is.EquivalentTo(new[] { "NORMAL", "HYPER" }));
                set.Beatmaps.Single(chart => chart.Path == "hyper.bms").DifficultyName = markedName;
            });
            var charts = realm.Run(r => r.All<BeatmapSetInfo>().Single().Beatmaps.Detach());
            if (stored)
            {
                File.Delete(normalPath);
                File.Delete(hyperPath);
            }

            using var files = new StorageBackedResourceStore(storage.GetStorageForDirectory("files"));
            var cache = new WorkingBeatmapCache(null, null, null, files, new FlatWorkingBeatmap(new Beatmap()), realm: realm);
            foreach (var chart in charts)
            {
                var expectedName = chart.Path == "hyper.bms" ? markedName : "NORMAL";
                var working = new BmsWorkingBeatmap(cache.GetWorkingBeatmap(chart), null);
                Assert.That(working.Beatmap.BeatmapInfo.DifficultyName, Is.EqualTo(expectedName));
                var ruleset = new BmsRuleset();
                var first = (BmsBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo,
                    chart.Path == "hyper.bms" ? [new BmsModBranchReplay { Decisions = { Value = "2:1" } }] : []);
                Assert.That(first.BeatmapInfo.DifficultyName, Is.EqualTo(expectedName));
                if (chart.Path == "hyper.bms")
                {
                    var second = (BmsBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo,
                        [new BmsModBranchReplay { Decisions = { Value = "2:2" } }]);
                    Assert.That(second.BeatmapInfo.DifficultyName, Is.EqualTo(expectedName));
                    Assert.That(first.Rank, Is.EqualTo(2));
                    Assert.That(second.Rank, Is.EqualTo(4));
                    Assert.That(second.HitObjects.Single().Column, Is.EqualTo(2));
                }
                Assert.That(working.Beatmap.BeatmapInfo.DifficultyName, Is.EqualTo(expectedName));
                Assert.That(chart.DifficultyName, Is.EqualTo(expectedName));
            }
        });
    }
}
