using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsModNoGoodNoGreatTest
{
    [Test]
    public void TestNoGoodDegradesGoodToBad()
    {
        // 7K rank 2: PG=(-15,15), GR=(-45,45), GD=(-112.5,112.5), BD=(-220,220).
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false, modifiers: BmsJudgementModifiers.NoGood);

        Assert.Multiple(() =>
        {
            Assert.That(table.HitAt(10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.HitAt(-10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.HitAt(20), Is.EqualTo(HitResult.Great));
            Assert.That(table.HitAt(-20), Is.EqualTo(HitResult.Great));
            Assert.That(table.HitAt(60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(-60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(210), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(-165), Is.EqualTo(HitResult.Ok));
        });
    }

    [Test]
    public void TestNoGreatDegradesGreatAndGoodToBad()
    {
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false, modifiers: BmsJudgementModifiers.NoGreat);

        Assert.Multiple(() =>
        {
            Assert.That(table.HitAt(10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.HitAt(-10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.HitAt(20), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(-20), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(-60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(210), Is.EqualTo(HitResult.Ok));
            Assert.That(table.HitAt(-165), Is.EqualTo(HitResult.Ok));
        });
    }

    [Test]
    public void TestConstraintDoesNotAffectBadAndMissRows()
    {
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false, modifiers: BmsJudgementModifiers.NoGreat);

        Assert.Multiple(() =>
        {
            Assert.That(table.PastDeadline(210), Is.False);
            Assert.That(table.PastDeadline(210.001), Is.True);
            Assert.That(table.EmptyPoorAt(-221), Is.True);
            Assert.That(table.EmptyPoorAt(-501), Is.False);
        });
    }
}
