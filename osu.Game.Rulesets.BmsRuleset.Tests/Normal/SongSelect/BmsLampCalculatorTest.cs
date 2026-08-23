using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsLampCalculatorTest
{
    [Test]
    public void TestNoScoreReturnsNoPlay()
    {
        Assert.That(BmsLampCalculator.Calculate(null), Is.EqualTo(BmsLamp.NoPlay));
    }

    [Test]
    public void TestFailedRankWins()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.F, stats((HitResult.Perfect, 50)), new BmsModExHardGauge())), Is.EqualTo(BmsLamp.Failed));
    }

    [Test]
    public void TestMaxBeatsGaugeLamp()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.X, stats((HitResult.Perfect, 50)), new BmsModExHardGauge())), Is.EqualTo(BmsLamp.Max));
    }

    [Test]
    public void TestPerfectBeatsGaugeLamp()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.S, stats((HitResult.Perfect, 49), (HitResult.Great, 1)), new BmsModHardGauge())), Is.EqualTo(BmsLamp.Perfect));
    }

    [Test]
    public void TestFullComboBeatsGaugeLamp()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.A, stats((HitResult.Perfect, 48), (HitResult.Great, 1), (HitResult.Good, 1)), new BmsModEasyGauge())), Is.EqualTo(BmsLamp.FullCombo));
    }

    [Test]
    public void TestEmptyPoorDoesNotPreventClearQualityLamp()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.X, stats((HitResult.Perfect, 50), (HitResult.Miss, 3)), new BmsModEasyGauge())), Is.EqualTo(BmsLamp.Max));
    }

    [TestCase(HitResult.Ok)]
    [TestCase(HitResult.Meh)]
    public void TestBadAndPoorPreventClearQualityLamp(HitResult breakingResult)
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.B, stats((HitResult.Perfect, 49), (breakingResult, 1)), new BmsModEasyGauge())), Is.EqualTo(BmsLamp.EasyClear));
    }

    [Test]
    public void TestGaugeLampMapping()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModAssistEasyGauge())), Is.EqualTo(BmsLamp.LightAssistClear));
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModEasyGauge())), Is.EqualTo(BmsLamp.EasyClear));
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)))), Is.EqualTo(BmsLamp.Clear));
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModHardGauge())), Is.EqualTo(BmsLamp.HardClear));
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModExHardGauge())), Is.EqualTo(BmsLamp.ExHardClear));
    }

    [Test]
    public void TestMissingStatisticsFallsBackToGauge()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, new Dictionary<HitResult, int>(), new BmsModHardGauge())), Is.EqualTo(BmsLamp.HardClear));
    }

    [Test]
    public void TestUnknownGaugeFallsBackToClear()
    {
        Assert.That(BmsLampCalculator.Calculate(score(ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModHazardGauge())), Is.EqualTo(BmsLamp.Clear));
    }

    private static ScoreInfo score(ScoreRank rank, Dictionary<HitResult, int> statistics, params Mod[] mods) => new()
    {
        Rank = rank,
        Statistics = statistics,
        Mods = mods,
    };

    private static Dictionary<HitResult, int> stats(params (HitResult result, int count)[] entries)
    {
        var statistics = new Dictionary<HitResult, int>();

        foreach (var (result, count) in entries)
            statistics[result] = count;

        return statistics;
    }
}
