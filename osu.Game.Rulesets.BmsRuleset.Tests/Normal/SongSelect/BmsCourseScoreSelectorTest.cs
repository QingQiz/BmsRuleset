using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.BmsRuleset.SongSelect.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsCourseScoreSelectorTest
{
    [Test]
    public void TestLampIsFilteredByMods()
    {
        var noModResult = result(BmsLamp.Clear, ScoreRank.S);
        var hideScratchResult = result(BmsLamp.ExHardClear, ScoreRank.S, mods: [new BmsModHideScratch()]);

        Assert.That(BmsCourseScoreSelector.SelectBest([hideScratchResult, noModResult], []), Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([hideScratchResult, noModResult], [new BmsModHideScratch()]), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestSelectedModsKeepOnlyMatchingRank()
    {
        var noModResult = result(BmsLamp.Clear, ScoreRank.S);
        var doubleTimeResult = result(BmsLamp.ExHardClear, ScoreRank.A, mods: [new BmsModDoubleTime()]);

        // Both the lamp and rank follow the selected Mod.
        Assert.That(BmsCourseScoreSelector.SelectBest([doubleTimeResult, noModResult], [new BmsModDoubleTime()]),
            Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.A)));
        Assert.That(BmsCourseScoreSelector.SelectBest([doubleTimeResult, noModResult], []),
            Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestNoMatchingResultShowsNoPlayAndHidesRank()
    {
        var noModResult = result(BmsLamp.Clear, ScoreRank.S);

        Assert.That(BmsCourseScoreSelector.SelectBest([noModResult], [new BmsModDoubleTime()]), Is.EqualTo((BmsLamp.NoPlay, (ScoreRank?)null)));
    }

    [Test]
    public void TestUnknownAcronymDoesNotLockOutNoModSelection()
    {
        var unknownModResult = result(BmsLamp.Clear, ScoreRank.A, acronyms: ["XX"]);

        Assert.That(BmsCourseScoreSelector.SelectBest([unknownModResult], []), Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.A)));
    }

    [Test]
    public void TestExClassTierAcronymIsDroppedForMatching()
    {
        var exClassResult = result(BmsLamp.HardClear, ScoreRank.S, acronyms: ["C2"]);

        Assert.That(BmsCourseScoreSelector.SelectBest([exClassResult], []), Is.EqualTo((BmsLamp.HardClear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([exClassResult], [new BmsModHardGauge()]), Is.EqualTo((BmsLamp.HardClear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestExHardClassTierAcronymIsDroppedForMatching()
    {
        var exHardClassResult = result(BmsLamp.ExHardClear, ScoreRank.S, acronyms: ["C3"]);

        Assert.That(BmsCourseScoreSelector.SelectBest([exHardClassResult], []), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([exHardClassResult], [new BmsModExHardGauge()]), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestAutoGaugeAcronymKeepsExHardClear()
    {
        var autoGaugeResult = result(BmsLamp.ExHardClear, ScoreRank.S, acronyms: ["AG"]);

        Assert.That(BmsCourseScoreSelector.SelectBest([autoGaugeResult], []), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([autoGaugeResult], [new BmsModAutoGauge()]), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestFailedResultCountsAsFailedLamp()
    {
        var failedResult = result(BmsLamp.Failed, ScoreRank.F);
        var clearResult = result(BmsLamp.Clear, ScoreRank.S);

        Assert.That(BmsCourseScoreSelector.SelectBest([failedResult, clearResult], []), Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([failedResult], []), Is.EqualTo((BmsLamp.Failed, (ScoreRank?)ScoreRank.F)));
    }

    [Test]
    public void TestHigherTierLampWinsAcrossAllResults()
    {
        var classResult = result(BmsLamp.Clear, ScoreRank.S, acronyms: ["C1"]);
        var exHardResult = result(BmsLamp.ExHardClear, ScoreRank.A, acronyms: ["C3"]);

        Assert.That(BmsCourseScoreSelector.SelectBest([classResult, exHardResult], []), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestRankAndLampUseTheBestMatchingResult()
    {
        var hideScratchResult = result(BmsLamp.ExHardClear, ScoreRank.A, mods: [new BmsModHideScratch()]);
        var clearResult = result(BmsLamp.Clear, ScoreRank.S);

        Assert.That(BmsCourseScoreSelector.SelectBest([hideScratchResult, clearResult], []), Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestRankOnlyFollowsMatchingResults()
    {
        var noModResult = result(BmsLamp.Clear, ScoreRank.S);
        var hideScratchResult = result(BmsLamp.ExHardClear, ScoreRank.X, mods: [new BmsModHideScratch()]);

        Assert.That(BmsCourseScoreSelector.SelectBest([hideScratchResult, noModResult], []), Is.EqualTo((BmsLamp.Clear, (ScoreRank?)ScoreRank.S)));
    }

    [Test]
    public void TestNoGoodAndNoGreatDoNotAffectLampOrRankSelection()
    {
        var noGoodResult = result(BmsLamp.HardClear, ScoreRank.A, mods: [new BmsModNoGood()]);
        var noGreatResult = result(BmsLamp.ExHardClear, ScoreRank.S, mods: [new BmsModNoGreat()]);

        Assert.That(BmsCourseScoreSelector.SelectBest([noGoodResult, noGreatResult], []), Is.EqualTo((BmsLamp.ExHardClear, (ScoreRank?)ScoreRank.S)));
        Assert.That(BmsCourseScoreSelector.SelectBest([noGoodResult], [new BmsModNoGreat()]), Is.EqualTo((BmsLamp.HardClear, (ScoreRank?)ScoreRank.A)));
    }

    [Test]
    public void TestEmptyResultsShowNoPlay()
    {
        Assert.That(BmsCourseScoreSelector.SelectBest([], []), Is.EqualTo((BmsLamp.NoPlay, (ScoreRank?)null)));
    }

    private static BmsCourseResult result(
        BmsLamp lamp,
        ScoreRank? rank,
        long totalScore = 0,
        Mod[] mods = null,
        string[] acronyms = null)
    {
        var score = new ScoreInfo
        {
            TotalScore = totalScore,
        };

        return new BmsCourseResult(
            lamp,
            rank,
            BmsCourseScoreData.From(score),
            new BmsCourseAttemptData
            {
                Status = BmsCourseStatus.Passed,
                GaugeType = BmsGaugeType.Class,
                ModAcronyms = acronyms ?? Array.ConvertAll(mods ?? [], mod => mod.Acronym),
                Stages = [],
            });
    }
}
