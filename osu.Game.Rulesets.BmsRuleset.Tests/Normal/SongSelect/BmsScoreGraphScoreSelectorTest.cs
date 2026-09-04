using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsScoreSelectorGraphTest
{
    [Test]
    public void TestSelectsHighestExScoreInsteadOfTotalScore()
    {
        var higherTotalScore = score(900_000, 10, 0);
        var higherExScore = score(800_000, 9, 3);

        Assert.That(BmsLampScoreSelector.SelectBest([higherTotalScore, higherExScore], [], 100), Is.SameAs(higherExScore));
    }

    [Test]
    public void TestUsesLampReductionModMatching()
    {
        var noMod = score(800_000, 8, 0);
        var hiddenScratch = score(900_000, 10, 0);
        hiddenScratch.Mods = [new BmsModHideScratch()];

        Assert.Multiple(() =>
        {
            Assert.That(BmsLampScoreSelector.SelectBest([noMod, hiddenScratch], [], 100), Is.SameAs(noMod));
            Assert.That(BmsLampScoreSelector.SelectBest([noMod, hiddenScratch], [new BmsModHideScratch()], 100), Is.SameAs(hiddenScratch));
        });
    }

    [Test]
    public void TestMissingStatisticsUsesPersistedAccuracy()
    {
        var lower = new ScoreInfo { Accuracy = 0.75 };
        var higher = new ScoreInfo { Accuracy = 0.8 };

        Assert.That(BmsLampScoreSelector.SelectBest([lower, higher], [], 100), Is.SameAs(higher));
    }

    [Test]
    public void TestEqualExScoreUsesEarlierResult()
    {
        var earlier = score(800_000, 10, 0);
        earlier.Date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = score(800_000, 10, 0);
        later.Date = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.That(BmsLampScoreSelector.SelectBest([later, earlier], [], 100), Is.SameAs(earlier));
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
