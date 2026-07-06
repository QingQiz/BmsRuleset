using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
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
    public void TestSelectedHideScratchOnlyShowsHideScratchScores()
    {
        var noModScore = score(1_000);
        var hideScratchScore = score(900, new BmsModHideScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, hideScratchScore], [new BmsModHideScratch()]), Is.SameAs(hideScratchScore));
    }

    [Test]
    public void TestSelectedAutoScratchOnlyShowsAutoScratchScores()
    {
        var noModScore = score(1_000);
        var autoScratchScore = score(900, new BmsModAutoScratch());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, autoScratchScore], [new BmsModAutoScratch()]), Is.SameAs(autoScratchScore));
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
    public void TestSelectedConstantOnlyShowsConstantScores()
    {
        var noModScore = score(1_000);
        var constantScore = score(900, new BmsModConstant());

        Assert.That(BmsLampScoreSelector.SelectBest([noModScore, constantScore], [new BmsModConstant()]), Is.SameAs(constantScore));
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
    public void TestReturnsNullWhenNoScoreMatchesSelectedSignificantMods()
    {
        Assert.That(BmsLampScoreSelector.SelectBest([score(1_000)], [new BmsModHideScratch()]), Is.Null);
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
