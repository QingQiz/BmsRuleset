using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Scoring;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsScoreSelectionModTest
{
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void TestNewIncreaseModRequiresExactMatch(bool scoreHasMod, bool selectionHasMod, bool expectedMatch)
    {
        var score = new ScoreInfo { Mods = scoreHasMod ? [new DifficultyIncreaseMod()] : [] };

        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, selectionHasMod ? [new DifficultyIncreaseMod()] : []), Is.EqualTo(expectedMatch));
    }

    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void TestNewReductionModAcceptsScoresWithoutMod(bool scoreHasMod, bool selectionHasMod, bool expectedMatch)
    {
        var score = new ScoreInfo { Mods = scoreHasMod ? [new DifficultyReductionMod()] : [] };

        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, selectionHasMod ? [new DifficultyReductionMod()] : []), Is.EqualTo(expectedMatch));
    }

    [Test]
    public void TestIncreaseAndReductionRulesApplyTogether()
    {
        var increaseScore = new ScoreInfo { Mods = [new DifficultyIncreaseMod()] };
        var combinedScore = new ScoreInfo { Mods = [new DifficultyIncreaseMod(), new DifficultyReductionMod()] };

        Assert.Multiple(() =>
        {
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(increaseScore, [new DifficultyIncreaseMod(), new DifficultyReductionMod()]), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(combinedScore, [new DifficultyIncreaseMod(), new DifficultyReductionMod()]), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(combinedScore, [new DifficultyIncreaseMod()]), Is.False);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(combinedScore, [new DifficultyReductionMod()]), Is.False);
        });
    }

    [Test]
    public void TestSameAcronymDoesNotReplaceOptedInMod()
    {
        var neutralScore = new ScoreInfo { Mods = [new BmsModMirror()] };
        var increaseScore = new ScoreInfo { Mods = [new DifficultyIncreaseMod()] };
        var reductionScore = new ScoreInfo { Mods = [new DifficultyReductionMod()] };

        Assert.Multiple(() =>
        {
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(neutralScore, []), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(neutralScore, [new DifficultyIncreaseMod()]), Is.False);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(neutralScore, [new DifficultyReductionMod()]), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(increaseScore, [new BmsModMirror()]), Is.False);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(reductionScore, [new BmsModMirror()]), Is.False);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(reductionScore, [new DifficultyIncreaseMod()]), Is.False);
        });
    }

    [Test]
    public void TestDoubleTimeSelectionRequiresAtLeastSelectedSpeed()
    {
        var score = new ScoreInfo { Mods = [new BmsModDoubleTime { SpeedChange = { Value = 1.5 } }] };

        Assert.Multiple(() =>
        {
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, []), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, [new BmsModDoubleTime { SpeedChange = { Value = 1.2 } }]), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, [new BmsModDoubleTime { SpeedChange = { Value = 1.6 } }]), Is.False);
        });
    }

    [Test]
    public void TestRateEligibilityMatrix()
    {
        double[] rates = [0.5, 0.6, 0.8, 1.0, 1.2, 1.5];
        foreach (var scoreRate in rates)
        foreach (var selectedRate in rates)
        {
            var score = new ScoreInfo { Mods = modsForRate(scoreRate) };
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, modsForRate(selectedRate)),
                Is.EqualTo(scoreRate >= selectedRate), $"Score {scoreRate}, selection {selectedRate}");
            score.Mods = [..score.Mods, new BmsModAutoScratch()];
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, modsForRate(selectedRate)), Is.False);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, [..modsForRate(selectedRate), new BmsModAutoScratch()]),
                Is.EqualTo(scoreRate >= selectedRate));
        }
    }

    private static Mod[] modsForRate(double rate) => rate switch
    {
        < 1 => [new BmsModHalfTime { SpeedChange = { Value = rate } }],
        > 1 => [new BmsModDoubleTime { SpeedChange = { Value = rate } }],
        _ => [],
    };

    [Test]
    public void TestSharedRuleEvaluatedOnceAcrossBothLists()
    {
        var first = new CountingRuleMod();
        var second = new OtherCountingRuleMod();
        var selected = new CountingRuleMod();
        var score = new ScoreInfo { Mods = [first, second] };

        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, [selected]), Is.True);
        Assert.That(first.Calls + second.Calls + selected.Calls, Is.EqualTo(1));
    }

    [Test]
    public void TestRateModsShareRuleIdentity()
    {
        IApplicableToScoreSelection doubleTime = new BmsModDoubleTime();
        IApplicableToScoreSelection halfTime = new BmsModHalfTime();
        Assert.That(doubleTime.ScoreSelectionRuleType, Is.EqualTo(halfTime.ScoreSelectionRuleType));
    }

    private class CountingRuleMod : BmsModMirror, IApplicableToScoreSelection
    {
        public int Calls { get; private set; }
        public Type ScoreSelectionRuleType => typeof(CountingRuleMod);
        public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;

        public bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods)
        {
            Calls++;
            return true;
        }
    }

    private class OtherCountingRuleMod : CountingRuleMod
    {
    }

    private class DifficultyIncreaseMod : BmsModMirror, IApplicableToScoreSelection
    {
        public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Increase;
    }

    private class DifficultyReductionMod : BmsModMirror, IApplicableToScoreSelection
    {
        public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;
    }
}
