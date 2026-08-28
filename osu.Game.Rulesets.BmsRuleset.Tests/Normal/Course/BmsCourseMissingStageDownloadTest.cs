using System.IO;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.SongSelect.Course;
using DifficultyTableModel = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course;

#nullable enable

[TestFixture]
public class BmsCourseMissingStageDownloadTest
{
    [Test]
    public void TestAllMissingStagesResolveDownloadUrls()
    {
        var store = createStore([
            new TableEntry { Md5Hash = "hash-1", Url = "https://example.test/pack" },
            new TableEntry { Md5Hash = "hash-2", Url = "https://example.test/pack" },
            new TableEntry { Md5Hash = "hash-3", UrlDiff = "https://example.test/chart" },
        ]);
        BmsCourseStage[] stages =
        [
            new("First", "1", BeatmapHash: "hash-1"),
            new("Second", "2", BeatmapHash: "hash-2"),
            new("Third", "3", BeatmapHash: "hash-3"),
        ];

        var urls = BmsCourseSongSelectController.ResolveMissingStageDownloadUrls(stages, store);
        string[] expected =
        [
            "https://example.test/pack",
            "https://example.test/chart",
        ];

        Assert.That(urls, Is.EqualTo(expected));
    }

    [TestCase("hash-without-url")]
    [TestCase("unknown-hash")]
    [TestCase(null)]
    public void TestAnyMissingDownloadUrlRejectsEntireBatch(string? unavailableHash)
    {
        var store = createStore([
            new TableEntry { Md5Hash = "valid-hash", Url = "https://example.test/pack" },
            new TableEntry { Md5Hash = "hash-without-url" },
        ]);
        BmsCourseStage[] stages =
        [
            new("Valid", "1", BeatmapHash: "valid-hash"),
            new("Unavailable", "2", BeatmapHash: unavailableHash),
        ];

        var urls = BmsCourseSongSelectController.ResolveMissingStageDownloadUrls(stages, store);

        Assert.That(urls, Is.Null);
    }

    private static DifficultyTableStore createStore(TableEntry[] entries)
    {
        var store = new DifficultyTableStore(null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "course-missing-stage-download-test"));
        store.RestoreTable(new DifficultyTableModel
        {
            Name = "Test",
            Entries = [.. entries],
        });
        return store;
    }
}
