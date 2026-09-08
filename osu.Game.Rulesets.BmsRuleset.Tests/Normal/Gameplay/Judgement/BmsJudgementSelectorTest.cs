using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementSelectorTest
{
    private static double rankRate(int rank) => BmsJudgementProfileProvider.RateForRank(rank);

    [TestCase(BmsJudgementAlgorithm.Combo, 1000, HitResult.Good)]
    [TestCase(BmsJudgementAlgorithm.Duration, 1100, HitResult.Perfect)]
    [TestCase(BmsJudgementAlgorithm.Lowest, 1000, HitResult.Good)]
    [TestCase(BmsJudgementAlgorithm.Score, 1100, HitResult.Perfect)]
    public void TestOverlappingGoodAndPerfect(BmsJudgementAlgorithm algorithm, double expectedTime, HitResult expectedResult)
    {
        var first = note(1000);
        var second = note(1100);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [second, first], 1100, algorithm);

        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(expectedTime));
        Assert.That(selection.Result, Is.EqualTo(expectedResult));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 1200)]
    [TestCase(BmsJudgementAlgorithm.Duration, 1200)]
    [TestCase(BmsJudgementAlgorithm.Lowest, 1000)]
    [TestCase(BmsJudgementAlgorithm.Score, 1200)]
    public void TestOverlappingBadAndGreat(BmsJudgementAlgorithm algorithm, double expectedTime)
    {
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1200), note(1000)], 1240, algorithm);

        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(expectedTime));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 1000)]
    [TestCase(BmsJudgementAlgorithm.Duration, 1450)]
    [TestCase(BmsJudgementAlgorithm.Lowest, 1000)]
    [TestCase(BmsJudgementAlgorithm.Score, 1000)]
    [TestCase(null, 1450)]
    public void TestOverlappingBadWindowsPreserveLegacyReplayBehaviour(BmsJudgementAlgorithm? algorithm, double expectedTime)
    {
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1450), note(1000)], 1250, algorithm);

        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(expectedTime));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Ok));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 150, 150, false)]
    [TestCase(BmsJudgementAlgorithm.Combo, 150.01, 150, true)]
    [TestCase(BmsJudgementAlgorithm.Combo, 151, 150.01, false)]
    [TestCase(BmsJudgementAlgorithm.Score, 60, 60, false)]
    [TestCase(BmsJudgementAlgorithm.Score, 60.01, 60, true)]
    [TestCase(BmsJudgementAlgorithm.Score, 61, 60.01, false)]
    public void TestStrictLateAndInclusiveEarlyThresholds(BmsJudgementAlgorithm algorithm, double lateOffset, double earlyOffset, bool selectNext)
    {
        var first = note(1000 - lateOffset);
        var second = note(1000 + earlyOffset);

        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [second, first], 1000, algorithm);

        Assert.That(selection.Candidate, Is.EqualTo(selectNext ? second : first));
    }

    [TestCase(1100, 1000)]
    [TestCase(1100.01, 1200)]
    public void TestDurationRetainsEarlierNoteOnTie(double inputTime, double expectedTime)
    {
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1200), note(1000)], inputTime, BmsJudgementAlgorithm.Duration);

        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(expectedTime));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 60)]
    [TestCase(BmsJudgementAlgorithm.Score, 30)]
    public void TestEachCandidateUsesItsOwnThreshold(BmsJudgementAlgorithm algorithm, double offset)
    {
        var tight = note(1000 - offset) with { JudgementRate = 0.25 };
        var wide = note(1000 + offset);
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [tight, wide], 1000, algorithm);
        Assert.That(selection.Candidate, Is.EqualTo(wide));

        var firstWide = tight with { JudgementRate = 1 };
        var nextTight = wide with { JudgementRate = 0.25 };
        selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [firstWide, nextTight], 1000, algorithm);
        Assert.That(selection.Candidate, Is.EqualTo(firstWide));
    }

    [TestCase(BmsLayoutVariant.Bms5K, 1, 100)]
    [TestCase(BmsLayoutVariant.Bme7K, 0, 160)]
    [TestCase(BmsLayoutVariant.Bme7KDouble, 15, 160)]
    [TestCase(BmsLayoutVariant.Pms9K, 0, 117)]
    public void TestComboUsesLayoutAndScratchWindows(BmsLayoutVariant layout, int column, double goodWindow)
    {
        var first = note(1000) with { Column = column };
        var second = note(1000 + goodWindow) with { Column = column, IsLongNote = true, EndTime = 2000 };

        var atBoundary = BmsJudgementSelector.SelectPress(layout, column, [second, first], second.StartTime, BmsJudgementAlgorithm.Combo);
        var afterBoundary = BmsJudgementSelector.SelectPress(layout, column, [second, first], second.StartTime + 0.01, BmsJudgementAlgorithm.Combo);

        Assert.That(atBoundary.Candidate, Is.EqualTo(first));
        Assert.That(afterBoundary.Candidate, Is.EqualTo(second));
    }

    [Test]
    public void TestAlgorithmsUseModifiedWindows()
    {
        IReadOnlyList<IApplicableToJudgementWindow> mods = [new BmsModNoGreat()];
        BmsJudgementProfileProvider.SetActiveWindowMods(mods);
        try
        {
            BmsJudgementAlgorithm[] algorithms = [BmsJudgementAlgorithm.Combo, BmsJudgementAlgorithm.Score];
            foreach (var algorithm in algorithms)
            {
                var future = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1000), note(1060)], 1050, algorithm);
                var past = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1000), note(1060)], 1060, algorithm);

                Assert.That(future.Candidate?.StartTime, Is.EqualTo(1000), algorithm.ToString());
                Assert.That(past.Candidate?.StartTime, Is.EqualTo(1060), algorithm.ToString());
            }
        }
        finally
        {
            BmsJudgementProfileProvider.ClearActiveWindowMods(mods);
        }
    }

    [Test]
    public void TestEmptyPoorAndColumnIsolation([Values] BmsJudgementAlgorithm algorithm)
    {
        var next = note(1000);
        var wrongColumn = note(600) with { Column = 2 };
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1100), wrongColumn, next], 600, algorithm);

        Assert.That(selection.Candidate, Is.EqualTo(next));
        Assert.That(selection.IsEmptyPoor, Is.True);
        Assert.That(selection.Result, Is.EqualTo(HitResult.Miss));
    }

    [Test]
    public void TestEmptyAndOutOfWindowCandidates([Values] BmsJudgementAlgorithm algorithm)
    {
        Assert.That(BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [], 1000, algorithm).Candidate, Is.Null);
        Assert.That(BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1000)], 499, algorithm).Candidate, Is.Null);
        Assert.That(BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, [note(1000)], 1281, algorithm).Candidate, Is.Null);
    }

    private static BmsJudgementCandidate note(double time) => new(time, time, 1, 1, false);

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
