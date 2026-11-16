
// ReSharper disable InconsistentNaming

using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementProfileTest
{
    [TestCase(BmsLayoutVariant.Bme7K, 0, 0)]
    [TestCase(BmsLayoutVariant.Bme7K, .1, .00075)]
    [TestCase(BmsLayoutVariant.Bme7K, 75, .5625)]
    [TestCase(BmsLayoutVariant.Bme7K, 99.9, .74925)]
    [TestCase(BmsLayoutVariant.Bme7K, 134, 1.005)]
    [TestCase(BmsLayoutVariant.Bme7K, 150.5, 1.12875)]
    [TestCase(BmsLayoutVariant.Popn9K, 0, 0)]
    [TestCase(BmsLayoutVariant.Popn9K, 75, .525)]
    public void ExRankUsesContinuousPercentages(BmsLayoutVariant layout, double exRank, double expected)
        => Assert.That(BmsEngineRules.RateForExRank(layout, exRank), Is.EqualTo(expected).Within(1e-12));

    [Test]
    public void FractionalExRankPreservesThePgreatBoundary()
    {
        var rate = BmsEngineRules.RateForExRank(BmsLayoutVariant.Bme7K, 75);
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, rate, false);
        Assert.That(table.HitAt(11.25), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.HitAt(11.251), Is.EqualTo(HitResult.Great));
    }

    [Test]
    public void FractionalAndZeroExRankEventsKeepTheirTimingAndPrecedence()
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#DEFEXRANK 150.5", "#EXRANK01 134.25", "#EXRANK02 0",
            "#00011:01", "#001A0:01", "#00111:01", "#002A0:02", "#00211:01",
        ]);
        Assert.That(parsed.DefaultExRank, Is.EqualTo(150.5));
        Assert.That(parsed.HitObjects[0].ExRank, Is.Null);
        Assert.That(parsed.HitObjects[1].ExRank, Is.EqualTo(134.25));
        Assert.That(parsed.HitObjects[1].StartTime, Is.EqualTo(2000));
        Assert.That(parsed.HitObjects[2].ExRank, Is.Zero);
        Assert.That(parsed.HitObjects[2].StartTime, Is.EqualTo(4000));
    }

    [Test]
    public void TestDoublePlayScratchColumnsUseScratchRows()
    {
        var p1 = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7KDouble, column: 0, rank: 3, tail: false);
        var p2 = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7KDouble, column: 15, rank: 3, tail: false);

        Assert.That(p1.HitAt(230), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.HitAt(230), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.HitAt(231), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.HitAt(231), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.HitAt(290), Is.EqualTo(HitResult.Ok));
        Assert.That(p2.HitAt(290), Is.EqualTo(HitResult.Ok));
        Assert.That(p1.HitAt(291), Is.EqualTo(HitResult.None));
        Assert.That(p2.HitAt(291), Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestFiveKeysAndSevenKeysUseDifferentGoodWidths()
    {
        var fiveKey = BmsEngineRules.GetTiming(BmsLayoutVariant.Bms5K, column: 1, rank: 3, tail: false);
        var sevenKey = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: false);

        Assert.That(fiveKey.HitAt(100), Is.EqualTo(HitResult.Good));
        Assert.That(fiveKey.HitAt(101), Is.EqualTo(HitResult.Ok));
        Assert.That(sevenKey.HitAt(150), Is.EqualTo(HitResult.Good));
        Assert.That(sevenKey.HitAt(151), Is.EqualTo(HitResult.Ok));
    }

    [Test]
    public void TestLongNoteTailUsesTailRows()
    {
        var normalTail = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);
        var scratchTail = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: true);

        Assert.That(normalTail.HitAt(220), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.HitAt(221), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.HitAt(280), Is.EqualTo(HitResult.Ok));
        Assert.That(normalTail.HitAt(281), Is.EqualTo(HitResult.None));
        Assert.That(scratchTail.HitAt(230), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.HitAt(231), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.HitAt(290), Is.EqualTo(HitResult.Ok));
        Assert.That(scratchTail.HitAt(291), Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestPms9k2pUsesPmsProfile()
    {
        var pms9k2p = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K2P, column: 1, rank: 0, tail: false);
        Assert.That(pms9k2p.HitAt(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPms9kDoubleUsesPmsProfile()
    {
        var pms9kD = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9KDouble, column: 1, rank: 0, tail: false);
        Assert.That(pms9kD.HitAt(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPms9kUsesPmsProfile()
    {
        var pms9k = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 0, tail: false);
        // PMS PGREAT fixed at 20 -> offset 10 is Perfect
        Assert.That(pms9k.HitAt(10), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsBadAndPoorUseFixedRows()
    {
        var rank0 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 0, tail: false);
        var rank4 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 4, tail: false);

        // BAD: (-183, 183) fixed, not rank-scaled
        Assert.That(rank0.HitAt(183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank0.HitAt(-183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank4.HitAt(183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank4.HitAt(-183), Is.EqualTo(HitResult.Ok));
        Assert.That(rank0.HitAt(184), Is.EqualTo(HitResult.None));
        Assert.That(rank4.HitAt(184), Is.EqualTo(HitResult.None));

        // POOR: (-175, 500) fixed, empty-poor zone on fast side
        Assert.That(rank0.EmptyPoorAt(-200), Is.True);
        Assert.That(rank4.EmptyPoorAt(-200), Is.True);
    }

    [Test]
    public void TestPmsGreatAndGoodScaleWithPmsRankRates()
    {
        // Rank 0 (pms rate=0.33): GREAT=(-16.5,16.5), GOOD=(-38.61,38.61)
        var rank0 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 0, tail: false);
        // Offset 22 > PGREAT(20) but within GOOD(38.61)
        Assert.That(rank0.HitAt(22), Is.EqualTo(HitResult.Good));
        Assert.That(rank0.HitAt(-22), Is.EqualTo(HitResult.Good));

        // Rank 2 (pms rate=0.70): PGREAT=20, GREAT=(-35,35), GOOD=(-81.9,81.9)
        var rank2 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 2, tail: false);
        Assert.That(rank2.HitAt(21), Is.EqualTo(HitResult.Great));
        Assert.That(rank2.HitAt(-21), Is.EqualTo(HitResult.Great));
        Assert.That(rank2.HitAt(36), Is.EqualTo(HitResult.Good));
        Assert.That(rank2.HitAt(-36), Is.EqualTo(HitResult.Good));

        // Rank 4 (pms rate=1.33): PGREAT=20, GREAT=(-66.5,66.5), GOOD=(-155.61,155.61)
        var rank4 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 4, tail: false);
        Assert.That(rank4.HitAt(30), Is.EqualTo(HitResult.Great));
        Assert.That(rank4.HitAt(-30), Is.EqualTo(HitResult.Great));
        Assert.That(rank4.HitAt(100), Is.EqualTo(HitResult.Good));
        Assert.That(rank4.HitAt(-100), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestPmsPerfectFixedAt20RegardlessOfRank()
    {
        var rank0 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 0, tail: false);
        var rank2 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 2, tail: false);
        var rank4 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 4, tail: false);

        Assert.That(rank0.HitAt(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank0.HitAt(-20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank2.HitAt(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank2.HitAt(-20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank4.HitAt(20), Is.EqualTo(HitResult.Perfect));
        Assert.That(rank4.HitAt(-20), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsScratchUsesSameWindowsAsNormal()
    {
        var normal = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 5, rank: 2, tail: false);
        var scratch = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 0, rank: 2, tail: false);

        Assert.That(scratch.HitAt(21), Is.EqualTo(HitResult.Great));
        Assert.That(normal.HitAt(21), Is.EqualTo(HitResult.Great));
        Assert.That(scratch.HitAt(36), Is.EqualTo(HitResult.Good));
        Assert.That(normal.HitAt(36), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestPmsTailPerfectIsFixed()
    {
        // PMS fixes PGREAT for both heads and tails.
        var rank0 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 0, rank: 0, tail: true);
        var rank4 = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 0, rank: 4, tail: true);

        // Both ranks retain the 120ms tail PGREAT window.
        Assert.That(rank0.HitAt(50), Is.EqualTo(HitResult.Perfect));

        Assert.That(rank4.HitAt(50), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsTailUsesCorrectValues()
    {
        // Rank 2: PGREAT=120, GREAT clamped to 120, GOOD=151.9, BAD=283.
        var tail = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 2, tail: true);

        Assert.That(tail.HitAt(120), Is.EqualTo(HitResult.Perfect));
        Assert.That(tail.HitAt(121), Is.EqualTo(HitResult.Good));
        Assert.That(tail.HitAt(200), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(283), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(284), Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestPmsUsesDifferentWindowsThanSevenKeys()
    {
        var pms = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, column: 1, rank: 2, tail: false);
        var seven = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: false);

        // PMS GOOD = 81.9 < 100, 7K GOOD = 112.5 >= 100
        Assert.That(pms.HitAt(100), Is.EqualTo(HitResult.Ok));
        Assert.That(seven.HitAt(100), Is.EqualTo(HitResult.Good));
    }

    [Test]
    public void TestSevenKeysRankScalingUsesBeatorajaNormalRows()
    {
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: false);

        Assert.That(table.HitAt(-15), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.HitAt(-45), Is.EqualTo(HitResult.Great));
        Assert.That(table.HitAt(-112.5), Is.EqualTo(HitResult.Good));
        Assert.That(table.HitAt(-165), Is.EqualTo(HitResult.Ok));
        Assert.That(table.HitAt(-166), Is.EqualTo(HitResult.None));
        Assert.That(table.HitAt(-221), Is.EqualTo(HitResult.None));
        Assert.That(table.EmptyPoorAt(-221), Is.True);
        Assert.That(table.EmptyPoorAt(-300), Is.True);
        Assert.That(table.EmptyPoorAt(-501), Is.False);
        Assert.That(table.PastDeadline(210), Is.False);
        Assert.That(table.PastDeadline(210.001), Is.True);
    }

    [Test]
    public void TestSevenKeysScratchUsesScratchRows()
    {
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: false);

        Assert.That(table.HitAt(30), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.HitAt(70), Is.EqualTo(HitResult.Great));
        Assert.That(table.HitAt(160), Is.EqualTo(HitResult.Good));
        Assert.That(table.HitAt(230), Is.EqualTo(HitResult.Ok));
        Assert.That(table.HitAt(231), Is.EqualTo(HitResult.Ok));
        Assert.That(table.HitAt(290), Is.EqualTo(HitResult.Ok));
        Assert.That(table.HitAt(291), Is.EqualTo(HitResult.None));
        Assert.That(table.EmptyPoorAt(-400), Is.True);
        Assert.That(table.EmptyPoorAt(-501), Is.False);
    }

    [TestCase(-27.160, HitResult.Perfect)]
    [TestCase(-27.161, HitResult.Great)]
    [TestCase(27.160, HitResult.Perfect)]
    [TestCase(27.161, HitResult.Great)]
    public void TestRankMultiplicationTruncatesToMicroseconds(double offset, HitResult expected)
    {
        // JudgeProperty at 9cddf911 casts microsecond windows to long: 20000 * 1.3580245 = 27160.49.
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 1.3580245, false);
        Assert.That(table.HitAt(offset), Is.EqualTo(expected));
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
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, 1, rank, true);
        Assert.That(table.HitAt(offset), Is.EqualTo(expected));
    }

    [TestCase(120, HitResult.Great)]
    [TestCase(-120, HitResult.Great)]
    [TestCase(120.001, HitResult.Ok)]
    [TestCase(-120.001, HitResult.Ok)]
    public void TestFiveKeyScratchTailGoodCannotBeNarrowerThanGreat(double offset, HitResult expected)
    {
        // JudgeProperty's unusual 5K scratch tail GOOD row is clamped to GREAT at rank 2.
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bms5K, 0, 2, true);
        Assert.That(table.HitAt(offset), Is.EqualTo(expected));
    }
}
