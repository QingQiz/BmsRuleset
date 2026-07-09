using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsLocalLeaderboardScoreSelectorTest
{
    [Test]
    public void TestSelectsScoreByBeatmapHashWhenBeatmapInfoPointsToOldBeatmap()
    {
        var oldBeatmap = new BeatmapInfo { Hash = "target-hash" };
        var scoreWithOldBeatmapLink = score("target-hash", "bms", 900_000, oldBeatmap);

        var selected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [scoreWithOldBeatmapLink],
            "target-hash",
            "bms",
            null,
            LeaderboardSortMode.Score);

        Assert.That(selected.Single(), Is.SameAs(scoreWithOldBeatmapLink));
    }

    [Test]
    public void TestExcludesOtherHashesRulesetsAndDeletedScores()
    {
        var matching = score("target-hash", "bms", 900_000);
        var otherHash = score("other-hash", "bms", 1_000_000);
        var otherRuleset = score("target-hash", "mania", 1_000_000);
        var deleted = score("target-hash", "bms", 1_000_000);
        deleted.DeletePending = true;

        var selected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [otherHash, otherRuleset, deleted, matching],
            "target-hash",
            "bms",
            null,
            LeaderboardSortMode.Score);

        Assert.That(selected, Is.EqualTo(new[] { matching }));
    }

    [Test]
    public void TestAttachesFallbackBeatmapForScoresWithoutBeatmapInfo()
    {
        var currentBeatmap = new BeatmapInfo { Hash = "target-hash" };
        var scoreWithoutBeatmapInfo = score("target-hash", "bms", 900_000);
        scoreWithoutBeatmapInfo.BeatmapInfo = null;

        var selected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [scoreWithoutBeatmapInfo],
            "target-hash",
            "bms",
            null,
            LeaderboardSortMode.Score,
            currentBeatmap);

        Assert.That(selected.Single().BeatmapInfo, Is.SameAs(currentBeatmap));
    }

    [Test]
    public void TestExactModsFilterUsesNativeLocalLeaderboardSemantics()
    {
        var noModScore = score("target-hash", "bms", 900_000);
        var noFailScore = score("target-hash", "bms", 800_000, mods: new BmsModNoFail());
        var noFailMirrorScore = score("target-hash", "bms", 1_000_000, mods: [new BmsModNoFail(), new BmsModMirror()]);

        var noModSelected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [noFailScore, noModScore],
            "target-hash",
            "bms",
            [],
            LeaderboardSortMode.Score);

        var noFailSelected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [noFailMirrorScore, noModScore, noFailScore],
            "target-hash",
            "bms",
            [new BmsModNoFail()],
            LeaderboardSortMode.Score);

        Assert.That(noModSelected, Is.EqualTo(new[] { noModScore }));
        Assert.That(noFailSelected, Is.EqualTo(new[] { noFailScore }));
    }

    [Test]
    public void TestSortsByRequestedLeaderboardMode()
    {
        var lowerAccuracy = score("target-hash", "bms", 1_000_000);
        lowerAccuracy.Accuracy = 0.95;

        var higherAccuracy = score("target-hash", "bms", 900_000);
        higherAccuracy.Accuracy = 0.99;

        var selected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [lowerAccuracy, higherAccuracy],
            "target-hash",
            "bms",
            null,
            LeaderboardSortMode.Accuracy);

        Assert.That(selected, Is.EqualTo(new[] { higherAccuracy, lowerAccuracy }));
    }

    private static ScoreInfo score(string beatmapHash, string rulesetShortName, long totalScore, BeatmapInfo beatmapInfo = null, params Mod[] mods) => new()
    {
        BeatmapInfo = beatmapInfo ?? new BeatmapInfo { Hash = beatmapHash },
        BeatmapHash = beatmapHash,
        Ruleset = new RulesetInfo { ShortName = rulesetShortName },
        TotalScore = totalScore,
        Date = DateTimeOffset.UtcNow,
        Mods = mods,
    };
}
