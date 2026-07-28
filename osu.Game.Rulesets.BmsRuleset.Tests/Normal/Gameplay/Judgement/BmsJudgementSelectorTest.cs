using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementSelectorTest
{
    private static double rankRate(int rank) => BmsJudgementProfileProvider.RateForRank(rank);

    [Test]
    public void TestFastMissRowIsEmptyPoorForNextCandidate()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 600);

        Assert.That(selection.IsEmptyPoor, Is.True);
        Assert.That(selection.Candidate, Is.EqualTo(candidate));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Miss));
    }

    [Test]
    public void TestEmptyPoorUsesNearestUpcomingCandidate()
    {
        var next = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);
        var later = new BmsJudgementCandidate(StartTime: 1100, EndTime: 1100, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [later, next], inputTime: 600);

        Assert.That(selection.IsEmptyPoor, Is.True);
        Assert.That(selection.Candidate, Is.EqualTo(next));
    }

    [Test]
    public void TestFastBadConsumesCandidate()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 780);

        Assert.That(selection.IsEmptyPoor, Is.False);
        Assert.That(selection.Candidate, Is.EqualTo(candidate));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Ok));
    }

    [Test]
    public void TestPressOutsideMissRowDoesNothing()
    {
        var candidate = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [candidate], inputTime: 499);

        Assert.That(selection.IsEmptyPoor, Is.False);
        Assert.That(selection.Candidate, Is.Null);
        Assert.That(selection.Result, Is.EqualTo(HitResult.None));
    }

    [Test]
    public void TestComboAlgorithmCanPreferLaterGoodCandidate()
    {
        var first = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);
        var second = new BmsJudgementCandidate(StartTime: 1200, EndTime: 1200, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);

        // inputTime=1240: first note (1000) is 240ms slow → BAD (outside GOOD -150 slow bound).
        // second note (1200) is 40ms slow → GREAT (inside GREAT -60 bound, better than first).
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [first, second], inputTime: 1240);

        Assert.That(selection.Candidate, Is.EqualTo(second));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Great));
    }

    [Test]
    public void TestPerCandidateJudgementRateChangesResultAtSameOffset()
    {
        // Same StartTime (1000) and inputTime (1035 → +35ms slow); only the per-candidate
        // JudgementRate differs. At +35ms: RANK 3 (rate 1.0) → GREAT; RANK 0 (rate 0.25)
        // → GOOD — the tighter windows shrink the GREAT/GOOD bands so +35 falls through to
        // GOOD. Proves the selector judges each candidate against its OWN rate, not a global one.
        var easy = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(3), IsLongNote: false);
        var veryHard = new BmsJudgementCandidate(StartTime: 1000, EndTime: 1000, Column: 1, JudgementRate: rankRate(0), IsLongNote: false);

        var easyResult = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [easy], inputTime: 1035);
        var veryHardResult = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [veryHard], inputTime: 1035);

        Assert.That(easyResult.Result, Is.EqualTo(HitResult.Great));
        Assert.That(veryHardResult.Result, Is.EqualTo(HitResult.Good));
    }
}
