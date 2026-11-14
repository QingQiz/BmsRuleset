using System.Linq;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsModNoBadTest
{
    [Test]
    public void TestOnlyBadIsRemoved(
        [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
        BmsLayoutVariant layout,
        [Values(0, 1)] int column,
        [Values(0, 1, 2, 3, 4)] int rank,
        [Values(false, true)] bool tail)
    {
        var original = BmsEngineRules.GetTiming(layout, column, rank, tail);
        var table = BmsEngineRules.GetTiming(layout, column, rank, tail, BmsJudgementModifiers.NoBad);

        Assert.That(table.WindowFor(BmsResults.EMPTY_POOR), Is.EqualTo(original.WindowFor(BmsResults.EMPTY_POOR)));
        Assert.That(table.Windows.Entries.Where(row => row.Result != BmsResults.BAD),
            Is.EqualTo(original.Windows.Entries.Where(row => row.Result != BmsResults.BAD)));
        Assert.That(table.PassiveDeadline, Is.EqualTo(original.PassiveDeadline));

        for (var offset = -550d; offset <= 550; offset += 0.5)
        {
            var result = original.HitAt(offset);
            Assert.That(table.HitAt(offset), Is.EqualTo(result == HitResult.Ok ? HitResult.None : result), $"offset {offset}");
            Assert.That(table.EmptyPoorAt(offset),
                Is.EqualTo(original.WindowFor(BmsResults.EMPTY_POOR) is { } miss && miss.Contains(Time.FromMilliseconds(offset)) && result is HitResult.None or HitResult.Ok),
                $"empty POOR at {offset}");
            Assert.That(table.PastDeadline(offset), Is.EqualTo(original.PastDeadline(offset)), $"passive POOR at {offset}");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestCombinationWithConstraintsIsOrderIndependent(bool noGreat)
    {
        var firstMods = BmsEngineRules.ModifiersFor([new BmsModNoBad(), noGreat ? new BmsModNoGreat() : new BmsModNoGood()]);
        var secondMods = BmsEngineRules.ModifiersFor([noGreat ? new BmsModNoGreat() : new BmsModNoGood(), new BmsModNoBad()]);
        var first = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false, firstMods);
        var second = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false, secondMods);

        Assert.That(first.Windows.Entries, Is.EqualTo(second.Windows.Entries));
        Assert.That(first.HitAt(60), Is.EqualTo(HitResult.None));
        Assert.That(first.HitAt(20), Is.EqualTo(noGreat ? HitResult.None : HitResult.Great));
        Assert.That(first.HitAt(0), Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestModCanBeResolvedFromAcronym()
    {
        Assert.That(new BmsRuleset().CreateModFromAcronym("NB"), Is.TypeOf<BmsModNoBad>());
    }
}
