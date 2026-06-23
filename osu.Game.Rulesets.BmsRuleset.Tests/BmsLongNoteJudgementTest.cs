using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsLongNoteJudgementTest
{
    [Test]
    public void TestEarlyReleaseBeforeTailWindowIsPoor()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(-500), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(-500), Is.False);
    }

    [Test]
    public void TestTailReleaseUsesTailBadWindow()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(220), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(221), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(280), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(281), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(281), Is.True);
    }

    [Test]
    public void TestScratchTailReleaseUsesScratchTailBadWindow()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(291), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(291), Is.True);
    }
}
