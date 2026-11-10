using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsLayoutStorageTest
{
    [TestCase(6, BmsLayoutVariant.Bms5K)]
    [TestCase(8, BmsLayoutVariant.Bme7K)]
    [TestCase(9, BmsLayoutVariant.Popn9K)]
    [TestCase(12, BmsLayoutVariant.Bms5KDouble)]
    [TestCase(16, BmsLayoutVariant.Bme7KDouble)]
    [TestCase(18, BmsLayoutVariant.Popn9KDouble)]
    public void ExistingColumnCountStorageStillLoadsTheSameLayout(int columns, BmsLayoutVariant layout)
    {
        var difficulty = new BeatmapDifficulty { CircleSize = columns };
        var decoded = BmsDifficultyInfo.FromOsuDifficulty(difficulty);
        Assert.That(decoded.LayoutVariant, Is.EqualTo(layout));
        Assert.That(decoded.KeyCount, Is.EqualTo(columns));
        decoded.WriteToOsuDifficulty(difficulty);
        Assert.That(difficulty.CircleSize, Is.EqualTo(columns));
    }

    [TestCase(BmsLayoutVariant.Popn5K)]
    [TestCase(BmsLayoutVariant.Keyboard24K)]
    [TestCase(BmsLayoutVariant.Keyboard24KDouble)]
    [TestCase((BmsLayoutVariant)10008)]
    public void ColumnCountSearchDecodesStoredLayoutIds(BmsLayoutVariant layout)
    {
        var beatmap = new BeatmapInfo(new BmsRuleset().RulesetInfo);
        new BmsDifficultyInfo { LayoutVariant = layout }.WriteToOsuDifficulty(beatmap);
        var columns = BmsLayout.GetTotalColumns(layout);
        var criteria = new FilterCriteria
        {
            CircleSize = new FilterCriteria.OptionalRange<float>
            {
                Min = columns,
                Max = columns,
                IsLowerInclusive = true,
                IsUpperInclusive = true,
            },
        };
        Assert.That(BmsBeatmapCarouselFilterMatching.Matches(beatmap, criteria), Is.True);
        criteria.CircleSize.Min = columns + 1;
        criteria.CircleSize.Max = null;
        Assert.That(BmsBeatmapCarouselFilterMatching.Matches(beatmap, criteria), Is.False);
    }

    [Test]
    public void ConvertedManiaSearchKeepsItsSourceColumnCount()
    {
        var beatmap = new BeatmapInfo(new RulesetInfo { ShortName = "mania", OnlineID = 3 }, new BeatmapDifficulty { CircleSize = 7 });
        var criteria = new FilterCriteria
        {
            CircleSize = new FilterCriteria.OptionalRange<float>
            {
                Min = 7,
                Max = 7,
                IsLowerInclusive = true,
                IsUpperInclusive = true,
            },
        };
        Assert.That(BmsBeatmapCarouselFilterMatching.Matches(beatmap, criteria), Is.True);
    }
}
