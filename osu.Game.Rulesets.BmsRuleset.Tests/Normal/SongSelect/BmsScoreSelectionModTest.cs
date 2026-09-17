using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Scoring;

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

    private class DifficultyIncreaseMod : BmsModMirror, IApplicableToScoreSelection
    {
        public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Increase;
    }

    private class DifficultyReductionMod : BmsModMirror, IApplicableToScoreSelection
    {
        public IApplicableToScoreSelection.ScoreSelectionDifficulty Difficulty => IApplicableToScoreSelection.ScoreSelectionDifficulty.Reduction;
    }
}
