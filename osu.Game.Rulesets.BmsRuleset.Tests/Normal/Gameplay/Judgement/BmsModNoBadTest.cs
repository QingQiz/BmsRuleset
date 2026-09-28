using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsModNoBadTest
{
    [Test]
    public void TestOnlyBadIsRemoved(
        [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant layout,
        [Values(0, 1)] int column,
        [Values(0, 1, 2, 3, 4)] int rank,
        [Values(false, true)] bool tail)
    {
        var original = BmsJudgementProfileProvider.GetTable(layout, column, rank, tail);
        var table = new BmsModNoBad().ApplyToJudgementWindow(original);

        Assert.That(table.EPoorWindow, Is.EqualTo(original.EPoorWindow));
        Assert.That(table.HitWindows.Where(row => row.Result != HitResult.Ok),
            Is.EqualTo(original.HitWindows.Where(row => row.Result != HitResult.Ok)));
        Assert.That(table.SlowWindowFor(HitResult.Ok), Is.EqualTo(original.SlowWindowFor(HitResult.Ok)));

        for (var offset = -550d; offset <= 550; offset += 0.5)
        {
            var result = original.ResultForOffset(offset);
            Assert.That(table.ResultForOffset(offset), Is.EqualTo(result == HitResult.Ok ? HitResult.None : result), $"offset {offset}");
            Assert.That(table.IsEmptyPoorOffset(offset),
                Is.EqualTo(original.EPoorWindow is { } miss && miss.ContainsOffset(offset) && result is HitResult.None or HitResult.Ok),
                $"empty POOR at {offset}");
            Assert.That(table.IsPastPassivePoorOffset(offset), Is.EqualTo(original.IsPastPassivePoorOffset(offset)), $"passive POOR at {offset}");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestCombinationWithConstraintsIsOrderIndependent(bool noGreat)
    {
        var original = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
        IApplicableToJudgementWindow constraint = noGreat ? new BmsModNoGreat() : new BmsModNoGood();
        var noBad = new BmsModNoBad();
        var first = noBad.ApplyToJudgementWindow(constraint.ApplyToJudgementWindow(original));
        var second = constraint.ApplyToJudgementWindow(noBad.ApplyToJudgementWindow(original));

        Assert.That(first.HitWindows, Is.EqualTo(second.HitWindows));
        Assert.That(first.ResultForOffset(60), Is.EqualTo(HitResult.None));
        Assert.That(first.ResultForOffset(20), Is.EqualTo(noGreat ? HitResult.None : HitResult.Great));
        Assert.That(first.ResultForOffset(0), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestModCanBeResolvedFromAcronym()
    {
        Assert.That(new BmsRuleset().CreateModFromAcronym("NB"), Is.TypeOf<BmsModNoBad>());
    }
}
