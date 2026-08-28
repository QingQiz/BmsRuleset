using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsBeatmapCarouselFilterMatchingTest
{
    private readonly RulesetInfo bmsRuleset = new() { ShortName = Constant.SHORT_NAME, OnlineID = -1 };

    [Test]
    public void TestShowConvertsAllowsMania7KForBms()
    {
        _ = new BmsRuleset();

        var criteria = new FilterCriteria { Ruleset = bmsRuleset, AllowConvertedBeatmaps = true };
        Assert.That(BmsBeatmapCarouselFilterMatching.Matches(createManiaBeatmapInfo(7), criteria), Is.True);
    }

    [TestCase(4)]
    [TestCase(6)]
    [TestCase(8)]
    public void TestShowConvertsRejectsOtherManiaKeyCounts(int keyCount)
    {
        Assert.That(
            BmsBeatmapCarouselFilterMatching.Matches(createManiaBeatmapInfo(keyCount), new FilterCriteria { Ruleset = bmsRuleset, AllowConvertedBeatmaps = true }),
            Is.False);
    }

    [Test]
    public void TestDisabledShowConvertsStillRejectsMania7K()
    {
        Assert.That(
            BmsBeatmapCarouselFilterMatching.Matches(createManiaBeatmapInfo(7), new FilterCriteria { Ruleset = bmsRuleset, AllowConvertedBeatmaps = false }),
            Is.False);
    }

    [Test]
    public void TestOtherTargetRulesetIsUnchanged()
    {
        var osuRuleset = new RulesetInfo { ShortName = "osu", OnlineID = 0 };

        Assert.That(
            BmsBeatmapCarouselFilterMatching.Matches(createManiaBeatmapInfo(7), new FilterCriteria { Ruleset = osuRuleset, AllowConvertedBeatmaps = true }),
            Is.False);
    }

    private static BeatmapInfo createManiaBeatmapInfo(int keyCount) => new(
        new RulesetInfo { ShortName = "mania", OnlineID = 3 },
        new BeatmapDifficulty { CircleSize = keyCount });
}
