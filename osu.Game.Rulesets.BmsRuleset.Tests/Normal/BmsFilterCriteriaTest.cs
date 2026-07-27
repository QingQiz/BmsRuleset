using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsFilterCriteriaTest
{
    [Test]
    public void TestMania7KConvertUsesBme7KVariant()
    {
        var beatmap = createBeatmap(7);
        beatmap.Ruleset = new RulesetInfo { OnlineID = 3, ShortName = "mania" };

        Assert.Multiple(() =>
        {
            Assert.That(BmsFilterCriteria.GetVariant(beatmap), Is.EqualTo(BmsLayoutVariant.Bme7K));
            Assert.That(new BmsRuleset().GetVariantForBeatmap(beatmap), Is.EqualTo((int)BmsLayoutVariant.Bme7K));
        });
    }

    [TestCase(7, 8, true)]
    [TestCase(7, 6, false)]
    [TestCase(14, 16, true)]
    [TestCase(14, 18, false)]
    public void TestKeysUsePlayableKeyCount(int searchKeyCount, int totalColumns, bool expected)
    {
        var criteria = new BmsFilterCriteria(null);

        Assert.That(criteria.TryParseCustomKeywordCriteria("keys", Operator.Equal, searchKeyCount.ToString()), Is.True);
        Assert.That(criteria.Matches(createBeatmap(totalColumns), new FilterCriteria()), Is.EqualTo(expected));
    }

    [TestCase("packs/GENOCIDE", "genocide", true, Operator.Equal)]
    [TestCase("packs/GENOCIDE", "stella", false, Operator.Equal)]
    [TestCase("packs/GENOCIDE", "genocide", false, Operator.NotEqual)]
    public void TestSource(string source, string query, bool expected, Operator op)
    {
        var criteria = new BmsFilterCriteria(null);
        var beatmap = createBeatmap(8);
        beatmap.Metadata.Source = source;

        Assert.That(criteria.TryParseCustomKeywordCriteria("source", op, query), Is.True);
        Assert.That(criteria.Matches(beatmap, new FilterCriteria()), Is.EqualTo(expected));
    }

    [TestCase(25, 100, "25", Operator.Equal, true)]
    [TestCase(25, 100, "25.1", Operator.Equal, false)]
    [TestCase(25, 100, "20", Operator.GreaterOrEqual, true)]
    public void TestLongNotePercentage(int longNotes, int totalNotes, string query, Operator op, bool expected)
    {
        var criteria = new BmsFilterCriteria(null);
        var beatmap = createBeatmap(8, totalNotes, longNotes);

        Assert.That(criteria.TryParseCustomKeywordCriteria("ln", op, query), Is.True);
        Assert.That(criteria.Matches(beatmap, new FilterCriteria()), Is.EqualTo(expected));
    }

    [TestCase(25, 100, "25", Operator.Equal, true)]
    [TestCase(25, 100, "25.1", Operator.Equal, false)]
    [TestCase(25, 100, "20", Operator.GreaterOrEqual, true)]
    public void TestScratchPercentage(int scratches, int totalNotes, string query, Operator op, bool expected)
    {
        var criteria = new BmsFilterCriteria(null);
        var beatmap = createBeatmap(8, totalNotes);
        BmsBeatmapStatistics.WriteScratchObjectCount(beatmap, scratches);

        Assert.That(criteria.TryParseCustomKeywordCriteria("scratch", op, query), Is.True);
        Assert.That(criteria.Matches(beatmap, new FilterCriteria()), Is.EqualTo(expected));
    }

    [Test]
    public void TestScratchFilterRejectsUnknownLegacyStatistics()
    {
        var criteria = new BmsFilterCriteria(null);

        Assert.That(criteria.TryParseCustomKeywordCriteria("scratch", Operator.Equal, "0"), Is.True);
        Assert.That(criteria.Matches(createBeatmap(8, 100), new FilterCriteria()), Is.False);
    }

    [Test]
    public void TestScratchFilterTreatsPmsAsZero()
    {
        var criteria = new BmsFilterCriteria(null);

        Assert.That(criteria.TryParseCustomKeywordCriteria("scratch", Operator.Equal, "0"), Is.True);
        Assert.That(criteria.Matches(createBeatmap(9, 100), new FilterCriteria()), Is.True);
    }

    [TestCase("ln")]
    [TestCase("scratch")]
    public void TestInvalidPercentage(string key)
    {
        var criteria = new BmsFilterCriteria(null);

        Assert.That(criteria.TryParseCustomKeywordCriteria(key, Operator.Equal, "invalid"), Is.False);
    }

    [TestCase("src")]
    [TestCase("source")]
    public void TestSourceAliases(string key)
    {
        var criteria = new BmsFilterCriteria(null);
        var beatmap = createBeatmap(8);
        beatmap.Metadata.Source = "packs/GENOCIDE";

        Assert.That(criteria.TryParseCustomKeywordCriteria(key, Operator.Equal, "genocide"), Is.True);
        Assert.That(criteria.Matches(beatmap, new FilterCriteria()), Is.True);
    }

    [TestCase("ln")]
    [TestCase("lns")]
    public void TestLongNoteAliases(string key)
    {
        var criteria = new BmsFilterCriteria(null);

        Assert.That(criteria.TryParseCustomKeywordCriteria(key, Operator.Equal, "25"), Is.True);
        Assert.That(criteria.Matches(createBeatmap(8, 100, 25), new FilterCriteria()), Is.True);
    }

    [TestCase("sc")]
    [TestCase("scratch")]
    public void TestScratchAliases(string key)
    {
        var criteria = new BmsFilterCriteria(null);
        var beatmap = createBeatmap(8, 100);
        BmsBeatmapStatistics.WriteScratchObjectCount(beatmap, 25);

        Assert.That(criteria.TryParseCustomKeywordCriteria(key, Operator.Equal, "25"), Is.True);
        Assert.That(criteria.Matches(beatmap, new FilterCriteria()), Is.True);
    }

    private static BeatmapInfo createBeatmap(int totalColumns, int totalNotes = 0, int longNotes = 0)
    {
        var beatmap = new BeatmapInfo(difficulty: new BeatmapDifficulty { CircleSize = totalColumns })
        {
            TotalObjectCount = totalNotes,
            EndTimeObjectCount = longNotes,
        };

        return beatmap;
    }
}
