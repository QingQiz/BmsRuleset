using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementSelectorTest
{
    [Test]
    public void TestEarlyMissRowIsEmptyPoorAndDoesNotSelectCandidate()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, Rank: 3, IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 600);

        Assert.That(selection.IsEmptyPoor, Is.True);
        Assert.That(selection.Candidate, Is.Null);
        Assert.That(selection.Result, Is.EqualTo(HitResult.Miss));
    }

    [Test]
    public void TestEarlyBadConsumesCandidate()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, Rank: 3, IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 780);

        Assert.That(selection.IsEmptyPoor, Is.False);
        Assert.That(selection.Candidate, Is.EqualTo(candidate));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Ok));
    }

    [Test]
    public void TestPressOutsideMissRowDoesNothing()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, Rank: 3, IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 499);

        Assert.That(selection.IsEmptyPoor, Is.False);
        Assert.That(selection.Candidate, Is.Null);
        Assert.That(selection.Result, Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestComboAlgorithmCanPreferLaterGoodCandidate()
    {
        var first = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, Rank: 3, IsLongNote: false);
        var second = new BmsJudgementCandidate(StartTime: 1200, EndTime: 1200, Column: 1, Rank: 3, IsLongNote: false);

        // inputTime=1240: first note (1000) is 240ms late → BAD (outside GOOD -150 late bound).
        // second note (1200) is 40ms late → GREAT (inside GREAT -60 bound, better than first).
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [first, second], inputTime: 1240);

        Assert.That(selection.Candidate, Is.EqualTo(second));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Great));
    }
}
