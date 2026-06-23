using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementProfileTest
{
    [Test]
    public void TestSevenKeysRankScalingUsesBeatorajaNormalRows()
    {
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: false);

        Assert.That(table.ResultForOffset(-15), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.ResultForOffset(-45), Is.EqualTo(HitResult.Great));
        Assert.That(table.ResultForOffset(-112.5), Is.EqualTo(HitResult.Good));
        Assert.That(table.ResultForOffset(-165), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(-220), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(-221), Is.EqualTo(HitResult.None));
        Assert.That(table.IsEmptyPoorOffset(-221), Is.True);
        Assert.That(table.IsEmptyPoorOffset(-300), Is.True);
        Assert.That(table.IsEmptyPoorOffset(-501), Is.False);
        Assert.That(table.IsPastPassivePoorOffset(280), Is.False);
        Assert.That(table.IsPastPassivePoorOffset(281), Is.True);
    }

    [Test]
    public void TestSevenKeysScratchUsesScratchRows()
    {
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: false);

        Assert.That(table.ResultForOffset(30), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.ResultForOffset(70), Is.EqualTo(HitResult.Great));
        Assert.That(table.ResultForOffset(160), Is.EqualTo(HitResult.Good));
        Assert.That(table.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(291), Is.EqualTo(HitResult.None));
        Assert.That(table.IsEmptyPoorOffset(-400), Is.True);
        Assert.That(table.IsEmptyPoorOffset(-501), Is.False);
    }

    [Test]
    public void TestFiveKeysAndSevenKeysUseDifferentGoodWidths()
    {
        var fiveKey = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bms5K, column: 1, rank: 3, tail: false);
        var sevenKey = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: false);

        Assert.That(fiveKey.ResultForOffset(100), Is.EqualTo(HitResult.Good));
        Assert.That(fiveKey.ResultForOffset(101), Is.EqualTo(HitResult.Ok));
        Assert.That(sevenKey.ResultForOffset(150), Is.EqualTo(HitResult.Good));
        Assert.That(sevenKey.ResultForOffset(151), Is.EqualTo(HitResult.Ok));
    }

    [Test]
    public void TestDoublePlayScratchColumnsUseScratchRows()
    {
        var p1 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7KDouble, column: 0, rank: 3, tail: false);
        var p2 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7KDouble, column: 15, rank: 3, tail: false);

        Assert.That(p1.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.ResultForOffset(291), Is.EqualTo(HitResult.None));
        Assert.That(p2.ResultForOffset(291), Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestLongNoteTailUsesTailRows()
    {
        var normalTail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);
        var scratchTail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: true);

        Assert.That(normalTail.ResultForOffset(220), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.ResultForOffset(221), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.ResultForOffset(280), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.ResultForOffset(281), Is.EqualTo(HitResult.None));
        Assert.That(scratchTail.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.ResultForOffset(291), Is.EqualTo(HitResult.None));
    }
}
