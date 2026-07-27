using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.BmsRuleset.SongSelect;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsConvertedBeatmapFilterPatcherTest
{
    private readonly RulesetInfo bmsRuleset = new() { ShortName = Constant.SHORT_NAME, OnlineID = -1 };

    [Test]
    public void TestShowConvertsAllowsMania7KForBms()
    {
        _ = new BmsRuleset();

        Assert.That(createManiaBeatmapInfo(7).AllowGameplayWithRuleset(bmsRuleset, true), Is.True);
    }

    [TestCase(4)]
    [TestCase(6)]
    [TestCase(8)]
    public void TestShowConvertsRejectsOtherManiaKeyCounts(int keyCount)
    {
        Assert.That(
            BmsConvertedBeatmapFilterPatcher.ApplyConversionAllowance(false, createManiaBeatmapInfo(keyCount), bmsRuleset, true),
            Is.False);
    }

    [Test]
    public void TestDisabledShowConvertsStillRejectsMania7K()
    {
        Assert.That(
            BmsConvertedBeatmapFilterPatcher.ApplyConversionAllowance(false, createManiaBeatmapInfo(7), bmsRuleset, false),
            Is.False);
    }

    [Test]
    public void TestOtherTargetRulesetIsUnchanged()
    {
        var maniaRuleset = new RulesetInfo { ShortName = "mania", OnlineID = 3 };

        Assert.That(
            BmsConvertedBeatmapFilterPatcher.ApplyConversionAllowance(false, createManiaBeatmapInfo(7), maniaRuleset, true),
            Is.False);
    }

    private static BeatmapInfo createManiaBeatmapInfo(int keyCount) => new(
        new RulesetInfo { ShortName = "mania", OnlineID = 3 },
        new BeatmapDifficulty { CircleSize = keyCount });
}
