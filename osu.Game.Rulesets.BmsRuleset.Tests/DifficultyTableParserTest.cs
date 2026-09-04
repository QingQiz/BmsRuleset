using NUnit.Framework;
using System.Linq;
using System.IO;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Screens.Select;
using osuTK.Graphics;
using DifficultyTableModel = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
#nullable enable
public class DifficultyTableParserTest
{
    [Test]
    public void ParsePreservesDownloadUrls()
    {
        var parsed = BmsTableJsonParser.Parse("""
            {
              "name": "Test",
              "symbol": "T",
              "charts": [{ "level": "1", "md5": "0123456789abcdef0123456789abcdef", "url": "https://example.test/pack", "url_diff": "https://example.test/diff" }]
            }
            """);

        var table = BmsTableJsonParser.Merge("test.json", TableSource.LocalFile, parsed!.Header, parsed.Charts);

        Assert.That(table!.Entries.Single().Url, Is.EqualTo("https://example.test/pack"));
        Assert.That(table.Entries.Single().UrlDiff, Is.EqualTo("https://example.test/diff"));
    }

    [TestCase("https://example.test/pack", "https://example.test/diff", "https://example.test/pack")]
    [TestCase(null, "https://example.test/diff", "https://example.test/diff")]
    [TestCase(null, null, null)]
    public void SelectsPackUrlBeforeDiffUrl(string? url, string? urlDiff, string? expected)
    {
        var entry = new TableEntry { Url = url, UrlDiff = urlDiff };

        Assert.That(UnavailableTableBeatmapFactory.GetDownloadUrl(entry), Is.EqualTo(expected));
    }

    [Test]
    public void UnavailableEntryUsesBmsRulesetWhenLocalLibraryStartsWithAnotherRuleset()
    {
        var store = new DifficultyTableStore(null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "unavailable-table-test"));
        store.RestoreTable(new DifficultyTableModel
        {
            Name = "Test",
            Symbol = "T",
            Entries = [new TableEntry { Level = "1", Md5Hash = "0123456789abcdef0123456789abcdef", Title = "Missing" }],
        });
        var localOsuBeatmap = new BeatmapInfo();

        var result = UnavailableTableBeatmapFactory.Create(store, new BmsRuleset().RulesetInfo, [localOsuBeatmap]).Single().Beatmaps.Single();

        Assert.That(result.Ruleset.ShortName, Is.EqualTo(Constant.SHORT_NAME));
    }

    [Test]
    public void UnavailableEntryPassesBmsCarouselFilter()
    {
        var store = new DifficultyTableStore(null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "unavailable-filter-test"));
        store.RestoreTable(new DifficultyTableModel
        {
            Name = "Test",
            Symbol = "T",
            Entries = [new TableEntry { Level = "1", Md5Hash = "abcdef0123456789abcdef0123456789", Title = "Missing" }],
        });

        var beatmap = UnavailableTableBeatmapFactory.Create(store, new BmsRuleset().RulesetInfo, []).Single().Beatmaps.Single();
        var criteria = new FilterCriteria
        {
            Ruleset = new BmsRuleset().RulesetInfo,
            RulesetCriteria = new BmsFilterCriteria(null),
        };

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.Hidden, Is.False);
            Assert.That(beatmap.BeatmapSet, Is.Not.Null);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, criteria), Is.True);
        });
    }

    [Test]
    public void UnavailableEntryDoesNotAssumeDisabledFiveKeyLayout()
    {
        var ruleset = new BmsRuleset();
        using var config = new BmsRulesetConfigManager(null, ruleset.RulesetInfo);
        config.SetValue(BmsRulesetSetting.ShowBms5K, false);
        config.SetValue(BmsRulesetSetting.ShowBme7K, false);
        config.SetValue(BmsRulesetSetting.ShowPms9K, false);
        config.SetValue(BmsRulesetSetting.ShowBms5KDouble, false);
        config.SetValue(BmsRulesetSetting.ShowBme7KDouble, false);
        config.SetValue(BmsRulesetSetting.ShowPms9KDouble, false);

        var store = new DifficultyTableStore(null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "unavailable-layout-filter-test"));
        store.RestoreTable(new DifficultyTableModel
        {
            Name = "Test",
            Symbol = "T",
            Entries = [new TableEntry { Level = "1", Md5Hash = "9876543210abcdef9876543210abcdef", Title = "Missing" }],
        });
        var beatmap = UnavailableTableBeatmapFactory.Create(store, ruleset.RulesetInfo, []).Single().Beatmaps.Single();

        Assert.That(new BmsFilterCriteria(config).Matches(beatmap, new FilterCriteria()), Is.True);
    }

    [Test]
    public void UnavailableEntryWarningColourReflectsDownloadAvailability()
    {
        Assert.That(BmsUnavailableBeatmapPanel.GetWarningColour(new TableEntry { Md5Hash = "a", Url = "https://example.com/chart" }), Is.EqualTo(Color4.Orange));
        Assert.That(BmsUnavailableBeatmapPanel.GetWarningColour(new TableEntry { Md5Hash = "b" }), Is.EqualTo(Color4.Red));
        Assert.That(BmsUnavailableBeatmapPanel.GetWarningColour(new TableEntry { Md5Hash = "c", Url = "not a url", UrlDiff = "ftp://example.com/chart" }), Is.EqualTo(Color4.Red));
    }
}
