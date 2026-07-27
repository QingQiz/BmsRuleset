using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsLampScoreSelectorTest
{
    [Test]
    public void TestNoSelectedModsExcludesSignificantModScores()
    {
        var noModScore = score(900);
        var hideScratchScore = score(1_000, new BmsModHideScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([hideScratchScore, noModScore], []), Is.SameAs(noModScore));
    }

    [Test]
    public void TestSelectedHideScratchKeepsNoModScore()
    {
        var noModScore = score(1_000);
        var hideScratchScore = score(900, new BmsModHideScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, hideScratchScore], [new BmsModHideScratch()]), Is.SameAs(noModScore));
    }

    [Test]
    public void TestSelectedAutoScratchKeepsNoModScore()
    {
        var noModScore = score(1_000);
        var autoScratchScore = score(900, new BmsModAutoScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, autoScratchScore], [new BmsModAutoScratch()]), Is.SameAs(noModScore));
    }

    [TestCase(typeof(BmsModHalfTime), typeof(BmsModDoubleTime))]
    [TestCase(typeof(BmsModDoubleTime), typeof(BmsModHalfTime))]
    public void TestRateAdjustModsMatchExactly(Type selectedRateModType, Type otherRateModType)
    {
        var selectedRateScore = score(900, create(selectedRateModType));
        var otherRateScore = score(1_000, create(otherRateModType));

        Assert.That(BmsLampScoreSelector.SelectBest([otherRateScore, selectedRateScore], [create(selectedRateModType)]), Is.SameAs(selectedRateScore));
    }

    [TestCase(typeof(BmsModHalfTime), 0.80)]
    [TestCase(typeof(BmsModDoubleTime), 1.75)]
    public void TestRateAdjustModsMatchSpeedChange(Type rateModType, double selectedSpeed)
    {
        var selectedRateScore = score(900, rateMod(rateModType, selectedSpeed));
        var defaultRateScore = score(1_000, create(rateModType));

        Assert.That(BmsLampScoreSelector.SelectBest([defaultRateScore, selectedRateScore], [rateMod(rateModType, selectedSpeed)]), Is.SameAs(selectedRateScore));
    }

    [Test]
    public void TestSelectedHalfTimeKeepsNoModScore()
    {
        var noModScore = score(1_000);
        var halfTimeScore = score(900, new BmsModHalfTime());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, halfTimeScore], [new BmsModHalfTime()]), Is.SameAs(noModScore));
    }

    [Test]
    public void TestSelectedConstantKeepsNoModScore()
    {
        var noModScore = score(1_000);
        var constantScore = score(900, new BmsModConstant());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, constantScore], [new BmsModConstant()]), Is.SameAs(noModScore));
    }

    [Test]
    public void TestOtherModsDoNotAffectMatching()
    {
        var noFailScore = score(1_000, new BmsModNoFail());
        var noModScore = score(900);

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, noFailScore], []), Is.SameAs(noFailScore));
        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, noFailScore], [new BmsModNoFail()]), Is.SameAs(noFailScore));
    }

    [Test]
    public void TestSelectedDoubleTimeHidesScoreWithoutMod()
    {
        Assert.That(BmsLampScoreSelector.SelectBest([score(1_000)], [new BmsModDoubleTime()]), Is.Null);
    }

    [TestCase(typeof(BmsModHardGauge))]
    [TestCase(typeof(BmsModExHardGauge))]
    [TestCase(typeof(BmsModHazardGauge))]
    public void TestSelectedNonDoubleTimeModKeepsScore(Type modType)
    {
        var existingScore = score(1_000);

        Assert.That(BmsLampScoreSelector.SelectBest([existingScore], [create(modType)]), Is.SameAs(existingScore));
    }

    [Test]
    public void TestOnlyAdditionalDoubleTimeHidesExistingLamp()
    {
        var autoScratchScore = score(1_000, new BmsModAutoScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([autoScratchScore], [new BmsModAutoScratch(), new BmsModHideScratch()]), Is.SameAs(autoScratchScore));
        Assert.That(BmsLampScoreSelector.SelectBest([autoScratchScore], [new BmsModAutoScratch(), new BmsModHardGauge()]), Is.SameAs(autoScratchScore));
        Assert.That(BmsLampScoreSelector.SelectBest([autoScratchScore], [new BmsModAutoScratch(), new BmsModDoubleTime()]), Is.Null);
    }

    [Test]
    public void TestDifficultyReductionModsUseSubsetMatching()
    {
        var hideScratchScore = score(1_000, new BmsModHideScratch());
        var hideScratchConstantScore = score(1_000, new BmsModHideScratch(), new BmsModConstant());

        Assert.That(BmsLampScoreSelector.SelectBest([hideScratchScore], [new BmsModHideScratch(), new BmsModConstant()]), Is.SameAs(hideScratchScore));
        Assert.That(BmsLampScoreSelector.SelectBest([hideScratchConstantScore], [new BmsModHideScratch()]), Is.Null);
        Assert.That(BmsLampScoreSelector.SelectBest([hideScratchConstantScore], [new BmsModHideScratch(), new BmsModConstant(), new BmsModHalfTime()]), Is.SameAs(hideScratchConstantScore));
        Assert.That(BmsLampScoreSelector.SelectBest([hideScratchConstantScore], [new BmsModHideScratch(), new BmsModHalfTime()]), Is.Null);
    }

    [TestCase(typeof(BmsModLongNote))]
    [TestCase(typeof(BmsModChargeNote))]
    [TestCase(typeof(BmsModHellChargeNote))]
    public void TestLongNoteModeModsUseDifficultyReductionMatching(Type modType)
    {
        var noModScore = score(900);
        var modeScore = score(1_000, create(modType));

        Assert.That(BmsLampScoreSelector.SelectBest([modeScore, noModScore], []), Is.SameAs(noModScore));
        Assert.That(BmsLampScoreSelector.SelectBest([noModScore], [create(modType)]), Is.SameAs(noModScore));
        Assert.That(BmsLampScoreSelector.SelectBest([modeScore], [create(modType)]), Is.SameAs(modeScore));
    }

    [TestCase(typeof(BmsModLongNote), typeof(BmsModChargeNote))]
    [TestCase(typeof(BmsModLongNote), typeof(BmsModHellChargeNote))]
    [TestCase(typeof(BmsModChargeNote), typeof(BmsModLongNote))]
    [TestCase(typeof(BmsModChargeNote), typeof(BmsModHellChargeNote))]
    [TestCase(typeof(BmsModHellChargeNote), typeof(BmsModLongNote))]
    [TestCase(typeof(BmsModHellChargeNote), typeof(BmsModChargeNote))]
    public void TestDifferentLongNoteModeScoresDoNotMatch(Type scoreModType, Type selectedModType)
    {
        var modeScore = score(1_000, create(scoreModType));

        Assert.That(BmsLampScoreSelector.SelectBest([modeScore], [create(selectedModType)]), Is.Null);
    }

    [Test]
    public void TestBetterLampFromEasierScoreIsSelected()
    {
        var harderClear = score(1_000, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)));
        var easierFullCombo = score(900, ScoreRank.A, stats((HitResult.Perfect, 1), (HitResult.Good, 1)), new BmsModHideScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([harderClear, easierFullCombo], [new BmsModHideScratch()]), Is.SameAs(easierFullCombo));
    }

    [Test]
    public void TestBestMatchingScoreUsesPanelLocalRankOrdering()
    {
        var earlierScore = score(1_000, date: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var laterScore = score(1_000, date: new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var lowerScore = score(900, date: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.That(BmsLampScoreSelector.SelectBest([laterScore, lowerScore, earlierScore], []), Is.SameAs(earlierScore));
    }

    [Test]
    public void TestBetterLampBeatsHigherScore()
    {
        var highScoreClear = score(1_000, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)));
        var lowScoreHardClear = score(900, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModHardGauge());

        Assert.That(BmsLampScoreSelector.SelectBest([highScoreClear, lowScoreHardClear], []), Is.SameAs(lowScoreHardClear));
    }

    private static ScoreInfo score(long totalScore, params Mod[] mods) =>
        score(totalScore, DateTimeOffset.UtcNow, mods);

    private static ScoreInfo score(long totalScore, DateTimeOffset date, params Mod[] mods) => new()
    {
        TotalScore = totalScore,
        Date = date,
        Mods = mods,
    };

    private static ScoreInfo score(long totalScore, ScoreRank rank, Dictionary<HitResult, int> statistics, params Mod[] mods)
    {
        var score = BmsLampScoreSelectorTest.score(totalScore, mods);
        score.Rank = rank;
        score.Statistics = statistics;
        return score;
    }

    private static Dictionary<HitResult, int> stats(params (HitResult result, int count)[] entries)
    {
        var statistics = new Dictionary<HitResult, int>();

        foreach (var (result, count) in entries)
            statistics[result] = count;

        return statistics;
    }

    private static Mod create(Type type) => (Mod)Activator.CreateInstance(type)!;

    private static Mod rateMod(Type type, double speedChange)
    {
        var mod = (ModRateAdjust)create(type);
        mod.SpeedChange.Value = speedChange;
        return mod;
    }
}
