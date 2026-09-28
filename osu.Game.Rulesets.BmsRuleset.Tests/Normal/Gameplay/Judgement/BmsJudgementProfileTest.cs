using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

// ReSharper disable InconsistentNaming

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementProfileTest
{

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

    [Test]
    public void TestPms9k2pUsesPmsProfile()
    {
        var pms9k2p = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K2P, column: 1, rank: 0, tail: false);
        Assert.That(pms9k2p.ResultForOffset(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPms9kDoubleUsesPmsProfile()
    {
        var pms9kD = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9KDouble, column: 1, rank: 0, tail: false);
        Assert.That(pms9kD.ResultForOffset(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPms9kUsesPmsProfile()
    {
        var pms9k = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 0, tail: false);
        // PMS PGREAT fixed at 20 -> offset 10 is Perfect
        Assert.That(pms9k.ResultForOffset(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsBadAndPoorUseFixedRows()
    {
        var rank0 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 0, tail: false);
        var rank4 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 4, tail: false);

        // BAD: (-183, 183) fixed, not rank-scaled
        Assert.That(rank0.ResultForOffset(183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank0.ResultForOffset(-183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank4.ResultForOffset(183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank4.ResultForOffset(-183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank0.ResultForOffset(184), Is.EqualTo(HitResult.None));
        Assert.That(rank4.ResultForOffset(184), Is.EqualTo(HitResult.None));

        // POOR: (-175, 500) fixed, empty-poor zone on fast side
        Assert.That(rank0.IsEmptyPoorOffset(-200), Is.True);
        Assert.That(rank4.IsEmptyPoorOffset(-200), Is.True);
    }

    [Test]
    public void TestPmsGreatAndGoodScaleWithPmsRankRates()
    {
        // Rank 0 (pms rate=0.33): GREAT=(-16.5,16.5), GOOD=(-38.61,38.61)
        var rank0 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 0, tail: false);
        // Offset 22 > PGREAT(20) but within GOOD(38.61)
        Assert.That(rank0.ResultForOffset(22), Is.EqualTo(HitResult.Good));
        Assert.That(rank0.ResultForOffset(-22), Is.EqualTo(HitResult.Good));

        // Rank 2 (pms rate=0.70): PGREAT=20, GREAT=(-35,35), GOOD=(-81.9,81.9)
        var rank2 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 2, tail: false);
        Assert.That(rank2.ResultForOffset(21), Is.EqualTo(HitResult.Great));
        Assert.That(rank2.ResultForOffset(-21), Is.EqualTo(HitResult.Great));
        Assert.That(rank2.ResultForOffset(36), Is.EqualTo(HitResult.Good));
        Assert.That(rank2.ResultForOffset(-36), Is.EqualTo(HitResult.Good));

        // Rank 4 (pms rate=1.33): PGREAT=20, GREAT=(-66.5,66.5), GOOD=(-155.61,155.61)
        var rank4 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 4, tail: false);
        Assert.That(rank4.ResultForOffset(30), Is.EqualTo(HitResult.Great));
        Assert.That(rank4.ResultForOffset(-30), Is.EqualTo(HitResult.Great));
        Assert.That(rank4.ResultForOffset(100), Is.EqualTo(HitResult.Good));
        Assert.That(rank4.ResultForOffset(-100), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestPmsPerfectFixedAt20RegardlessOfRank()
    {
        var rank0 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 0, tail: false);
        var rank2 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 2, tail: false);
        var rank4 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 4, tail: false);

        Assert.That(rank0.ResultForOffset(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank0.ResultForOffset(-20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank2.ResultForOffset(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank2.ResultForOffset(-20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank4.ResultForOffset(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank4.ResultForOffset(-20), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsScratchUsesSameWindowsAsNormal()
    {
        var normal = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 5, rank: 2, tail: false);
        var scratch = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 0, rank: 2, tail: false);

        Assert.That(scratch.ResultForOffset(21), Is.EqualTo(HitResult.Great));
        Assert.That(normal.ResultForOffset(21), Is.EqualTo(HitResult.Great));
        Assert.That(scratch.ResultForOffset(36), Is.EqualTo(HitResult.Good));
        Assert.That(normal.ResultForOffset(36), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestPmsTailPerfectIsFixed()
    {
        // PMS fixes PGREAT for both heads and tails.
        var rank0 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 0, rank: 0, tail: true);
        var rank4 = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 0, rank: 4, tail: true);

        // Both ranks retain the 120ms tail PGREAT window.
        Assert.That(rank0.ResultForOffset(50), Is.EqualTo(HitResult.Perfect));

        Assert.That(rank4.ResultForOffset(50), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsTailUsesCorrectValues()
    {
        // Rank 2: PGREAT=120, GREAT clamped to 120, GOOD=151.9, BAD=283.
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 2, tail: true);

        Assert.That(tail.ResultForOffset(120), Is.EqualTo(HitResult.Perfect));
        Assert.That(tail.ResultForOffset(121), Is.EqualTo(HitResult.Good));
        Assert.That(tail.ResultForOffset(200), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(283), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(284), Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestPmsUsesDifferentWindowsThanSevenKeys()
    {
        var pms = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, column: 1, rank: 2, tail: false);
        var seven = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: false);

        // PMS GOOD = 81.9 < 100, 7K GOOD = 112.5 >= 100
        Assert.That(pms.ResultForOffset(100), Is.EqualTo(HitResult.Ok));
        Assert.That(seven.ResultForOffset(100), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestSevenKeysRankScalingUsesBeatorajaNormalRows()
    {
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: false);

        Assert.That(table.ResultForOffset(-15), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.ResultForOffset(-45), Is.EqualTo(HitResult.Great));
        Assert.That(table.ResultForOffset(-112.5), Is.EqualTo(HitResult.Good));
        Assert.That(table.ResultForOffset(-165), Is.EqualTo(HitResult.Ok));
        Assert.That(table.ResultForOffset(-166), Is.EqualTo(HitResult.None));
        Assert.That(table.ResultForOffset(-221), Is.EqualTo(HitResult.None));
        Assert.That(table.IsEmptyPoorOffset(-221), Is.True);
        Assert.That(table.IsEmptyPoorOffset(-300), Is.True);
        Assert.That(table.IsEmptyPoorOffset(-501), Is.False);
        Assert.That(table.IsPastPassivePoorOffset(210), Is.False);
        Assert.That(table.IsPastPassivePoorOffset(210.001), Is.True);
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
    [TestCase(-27.160, HitResult.Perfect)]
    [TestCase(-27.161, HitResult.Great)]
    [TestCase(27.160, HitResult.Perfect)]
    [TestCase(27.161, HitResult.Great)]
    public void TestRankMultiplicationTruncatesToMicroseconds(double offset, HitResult expected)
    {
        // JudgeProperty at 9cddf911 casts microsecond windows to long: 20000 * 1.3580245 = 27160.49.
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 1.3580245, false);
        Assert.That(table.ResultForOffset(offset), Is.EqualTo(expected));
    }

    [TestCase(0, -120, HitResult.Perfect)]
    [TestCase(0, 120, HitResult.Perfect)]
    [TestCase(0, -120.001, HitResult.Ok)]
    [TestCase(0, 120.001, HitResult.Ok)]
    [TestCase(4, -120, HitResult.Perfect)]
    [TestCase(4, 120.001, HitResult.Great)]
    [TestCase(4, 283, HitResult.Good)]
    [TestCase(4, 283.001, HitResult.None)]
    public void TestPmsTailFixedAndClampedBoundaries(int rank, double offset, HitResult expected)
    {
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, rank, true);
        Assert.That(table.ResultForOffset(offset), Is.EqualTo(expected));
    }

    [TestCase(120, HitResult.Great)]
    [TestCase(-120, HitResult.Great)]
    [TestCase(120.001, HitResult.Ok)]
    [TestCase(-120.001, HitResult.Ok)]
    public void TestFiveKeyScratchTailGoodCannotBeNarrowerThanGreat(double offset, HitResult expected)
    {
        // JudgeProperty's unusual 5K scratch tail GOOD row is clamped to GREAT at rank 2.
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bms5K, 0, 2, true);
        Assert.That(table.ResultForOffset(offset), Is.EqualTo(expected));
    }

}
