using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsLocalLeaderboardServiceTest
{

    private static ScoreInfo score(string beatmapHash, string rulesetShortName, long totalScore, BeatmapInfo beatmapInfo = null, params Mod[] mods) => new()
    {
        BeatmapInfo = beatmapInfo ?? new BeatmapInfo { Hash = beatmapHash },
        BeatmapHash = beatmapHash,
        Ruleset = new RulesetInfo { ShortName = rulesetShortName },
        TotalScore = totalScore,
        Date = DateTimeOffset.UtcNow,
        Mods = mods,
    };

    [Test]
    public void TestAttachesFallbackBeatmapForScoresWithoutBeatmapInfo()
    {
        var currentBeatmap = new BeatmapInfo { Hash = "target-hash" };
        var scoreWithoutBeatmapInfo = score("target-hash", Constant.SHORT_NAME, 900_000);
        scoreWithoutBeatmapInfo.BeatmapInfo = null;

        var selected = BmsLocalLeaderboardService.SelectScores(
            [scoreWithoutBeatmapInfo],
            "target-hash",
            Constant.SHORT_NAME,
            null,
            LeaderboardSortMode.Score,
            currentBeatmap);

        Assert.That(selected.Single().BeatmapInfo, Is.SameAs(currentBeatmap));
    }

    [Test]
    public void TestExactModsFilterUsesNativeLocalLeaderboardSemantics()
    {
        var noModScore = score("target-hash", Constant.SHORT_NAME, 900_000);
        var noFailScore = score("target-hash", Constant.SHORT_NAME, 800_000, mods: new BmsModNoFail());
        var noFailMirrorScore = score("target-hash", Constant.SHORT_NAME, 1_000_000, mods: [new BmsModNoFail(), new BmsModMirror()]);

        var noModSelected = BmsLocalLeaderboardService.SelectScores(
            [noFailScore, noModScore],
            "target-hash",
            Constant.SHORT_NAME,
            [],
            LeaderboardSortMode.Score);

        var noFailSelected = BmsLocalLeaderboardService.SelectScores(
            [noFailMirrorScore, noModScore, noFailScore],
            "target-hash",
            Constant.SHORT_NAME,
            [new BmsModNoFail()],
            LeaderboardSortMode.Score);

        Assert.That(noModSelected, Is.EqualTo([noModScore]));
        Assert.That(noFailSelected, Is.EqualTo([noFailScore]));
    }

    [Test]
    public void TestExcludesOtherHashesRulesetsAndDeletedScores()
    {
        var matching = score("target-hash", Constant.SHORT_NAME, 900_000);
        var otherHash = score("other-hash", Constant.SHORT_NAME, 1_000_000);
        var otherRuleset = score("target-hash", "mania", 1_000_000);
        var deleted = score("target-hash", Constant.SHORT_NAME, 1_000_000);
        deleted.DeletePending = true;

        var selected = BmsLocalLeaderboardService.SelectScores(
            [otherHash, otherRuleset, deleted, matching],
            "target-hash",
            Constant.SHORT_NAME,
            null,
            LeaderboardSortMode.Score);

        Assert.That(selected, Is.EqualTo([matching]));
    }

    [Test]
    public void TestSelectsScoreByBeatmapHashWhenBeatmapInfoPointsToOldBeatmap()
    {
        var oldBeatmap = new BeatmapInfo { Hash = "target-hash" };
        var scoreWithOldBeatmapLink = score("target-hash", Constant.SHORT_NAME, 900_000, oldBeatmap);

        var selected = BmsLocalLeaderboardService.SelectScores(
            [scoreWithOldBeatmapLink],
            "target-hash",
            Constant.SHORT_NAME,
            null,
            LeaderboardSortMode.Score);

        Assert.That(selected.Single(), Is.SameAs(scoreWithOldBeatmapLink));
    }

    [Test]
    public void TestSortsByRequestedLeaderboardMode()
    {
        var lowerAccuracy = score("target-hash", Constant.SHORT_NAME, 1_000_000);
        lowerAccuracy.Accuracy = 0.95;

        var higherAccuracy = score("target-hash", Constant.SHORT_NAME, 900_000);
        higherAccuracy.Accuracy = 0.99;

        var selected = BmsLocalLeaderboardService.SelectScores(
            [lowerAccuracy, higherAccuracy],
            "target-hash",
            Constant.SHORT_NAME,
            null,
            LeaderboardSortMode.Accuracy);

        Assert.That(selected, Is.EqualTo([higherAccuracy, lowerAccuracy]));
    }
}
