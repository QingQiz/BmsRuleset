using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsModNoGoodNoGreatTest
{
    [Test]
    public void TestNoGoodDegradesGoodToBad()
    {
        // 7K rank 2: PG=(-15,15), GR=(-45,45), GD=(-112.5,112.5), BD=(-220,220).
        var mod = new BmsModNoGood();
        var table = mod.ApplyToJudgementWindow(
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false));

        Assert.Multiple(() =>
        {
            Assert.That(table.ResultForOffset(10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.ResultForOffset(-10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.ResultForOffset(20), Is.EqualTo(HitResult.Great));
            Assert.That(table.ResultForOffset(-20), Is.EqualTo(HitResult.Great));
            Assert.That(table.ResultForOffset(60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(-60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(220), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(-220), Is.EqualTo(HitResult.Ok));
        });
    }

    [Test]
    public void TestNoGreatDegradesGreatAndGoodToBad()
    {
        var mod = new BmsModNoGreat();
        var table = mod.ApplyToJudgementWindow(
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false));

        Assert.Multiple(() =>
        {
            Assert.That(table.ResultForOffset(10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.ResultForOffset(-10), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.ResultForOffset(20), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(-20), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(-60), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(220), Is.EqualTo(HitResult.Ok));
            Assert.That(table.ResultForOffset(-220), Is.EqualTo(HitResult.Ok));
        });
    }

    [Test]
    public void TestConstraintDoesNotAffectBadAndMissRows()
    {
        var mod = new BmsModNoGreat();
        var table = mod.ApplyToJudgementWindow(
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, judgementRate: 0.75, tail: false));

        Assert.Multiple(() =>
        {
            Assert.That(table.IsPastPassivePoorOffset(280), Is.False);
            Assert.That(table.IsPastPassivePoorOffset(281), Is.True);
            Assert.That(table.IsEmptyPoorOffset(-221), Is.True);
            Assert.That(table.IsEmptyPoorOffset(-501), Is.False);
        });
    }
}
