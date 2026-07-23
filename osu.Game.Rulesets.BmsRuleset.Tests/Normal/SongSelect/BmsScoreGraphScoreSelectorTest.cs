using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsScoreGraphScoreSelectorTest
{
    [Test]
    public void TestSelectsHighestExScoreInsteadOfTotalScore()
    {
        var higherTotalScore = score(900_000, 10, 0);
        var higherExScore = score(800_000, 9, 3);

        Assert.That(BmsScoreGraphScoreSelector.SelectBest([higherTotalScore, higherExScore], [], 100), Is.SameAs(higherExScore));
    }

    [Test]
    public void TestUsesLampReductionModMatching()
    {
        var noMod = score(800_000, 8, 0);
        var hiddenScratch = score(900_000, 10, 0);
        hiddenScratch.Mods = [new BmsModHideScratch()];

        Assert.Multiple(() =>
        {
            Assert.That(BmsScoreGraphScoreSelector.SelectBest([noMod, hiddenScratch], [], 100), Is.SameAs(noMod));
            Assert.That(BmsScoreGraphScoreSelector.SelectBest([noMod, hiddenScratch], [new BmsModHideScratch()], 100), Is.SameAs(hiddenScratch));
        });
    }

    [Test]
    public void TestMissingStatisticsUsesPersistedAccuracy()
    {
        var lower = new ScoreInfo { Accuracy = 0.75 };
        var higher = new ScoreInfo { Accuracy = 0.8 };

        Assert.That(BmsScoreGraphScoreSelector.SelectBest([lower, higher], [], 100), Is.SameAs(higher));
    }

    [Test]
    public void TestEqualExScoreUsesEarlierResult()
    {
        var earlier = score(800_000, 10, 0);
        earlier.Date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = score(800_000, 10, 0);
        later.Date = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.That(BmsScoreGraphScoreSelector.SelectBest([later, earlier], [], 100), Is.SameAs(earlier));
    }

    private static ScoreInfo score(long totalScore, int perfect, int great)
    {
        var score = new ScoreInfo
        {
            TotalScore = totalScore,
            Statistics = new Dictionary<HitResult, int>
            {
                [HitResult.Perfect] = perfect,
                [HitResult.Great] = great,
            },
        };

        return score;
    }
}
