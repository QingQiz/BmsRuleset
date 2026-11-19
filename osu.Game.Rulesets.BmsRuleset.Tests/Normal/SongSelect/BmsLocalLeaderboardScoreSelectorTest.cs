using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
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
        var noMineScore = score("target-hash", Constant.SHORT_NAME, 800_000, mods: new BmsModNoMine());
        var noMineMirrorScore = score("target-hash", Constant.SHORT_NAME, 1_000_000, mods: [new BmsModNoMine(), new BmsModMirror()]);

        var noModSelected = BmsLocalLeaderboardService.SelectScores(
            [noMineScore, noModScore],
            "target-hash",
            Constant.SHORT_NAME,
            [],
            LeaderboardSortMode.Score);

        var noMineSelected = BmsLocalLeaderboardService.SelectScores(
            [noMineMirrorScore, noModScore, noMineScore],
            "target-hash",
            Constant.SHORT_NAME,
            [new BmsModNoMine()],
            LeaderboardSortMode.Score);

        Assert.That(noModSelected, Is.EqualTo([noModScore]));
        Assert.That(noMineSelected, Is.EqualTo([noMineScore]));
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

    [TestCase(7, true)]
    [TestCase(4, false)]
    public void TestManiaScoresOnConvertibleBeatmaps(int keys, bool convertible)
    {
        var mania = new RulesetInfo { ShortName = "mania", OnlineID = 3 };
        var beatmap = new BeatmapInfo(mania, new BeatmapDifficulty { CircleSize = keys }) { Hash = "target-hash" };
        var native = new ScoreInfo(beatmap, mania) { TotalScore = 900_000 };
        var bms = score(beatmap.Hash, Constant.SHORT_NAME, 800_000, beatmap);
        Mod[] mods = [new BmsModHideScratch(), new BmsModManiaProfile()];

        ScoreInfo[] select(Mod[] exactMods) => BmsLocalLeaderboardService.SelectScores([native, bms], beatmap.Hash, Constant.SHORT_NAME,
            exactMods, LeaderboardSortMode.Score, beatmap);

        Assert.That(select(null), Is.EqualTo(convertible ? new[] { native, bms } : [bms]));
        Assert.That(select([]), Is.EqualTo([bms]));
        var selected = select(mods);
        if (convertible)
        {
            var display = selected.Single();
            Assert.That(display.ID, Is.EqualTo(native.ID));
            Assert.That(display.Ruleset, Is.EqualTo(mania));
            Assert.That(display.Mods, Is.EqualTo(mods));
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(display, mods), Is.True);
            Assert.That(BmsLampScoreSelector.MatchesSelectedMods(display, [mods[1]]), Is.False);
        }
        else
            Assert.That(selected, Is.Empty);

        Assert.That(native.Mods, Is.Empty);
        Assert.That(bms.Mods, Is.Empty);
    }

    [Test]
    public void TestManiaModSettingsAndAcronymCollision()
    {
        var mania = new RulesetInfo { ShortName = "mania", OnlineID = 3 };
        var beatmap = new BeatmapInfo(mania, new BeatmapDifficulty { CircleSize = 7 }) { Hash = "target-hash" };
        var native = new ScoreInfo(beatmap, mania) { Mods = [new ModCinema(), new TestDoubleTime { SpeedChange = { Value = 1.3 } }] };
        var originalJson = native.ModsJson;
        var projected = BmsLocalLeaderboardService.SelectScores([native], beatmap.Hash, Constant.SHORT_NAME, null, LeaderboardSortMode.Score, beatmap).Single();
        Assert.That(projected.Mods.OfType<BmsModDoubleTime>().Single().SpeedChange.Value, Is.EqualTo(1.3).Within(0.001));
        Assert.That(projected.Mods, Has.One.TypeOf<ModCinema>());
        var target = new BmsRuleset().RulesetInfo;
        var playback = BmsLocalScores.ForPlayback(projected, target);
        Assert.That(playback.Ruleset, Is.EqualTo(target));
        Assert.That(playback.ID, Is.EqualTo(native.ID));
        Assert.That(playback.DeepClone().Mods.Select(mod => mod.GetType()), Is.EqualTo(new[] { typeof(BmsModDoubleTime), typeof(BmsModHideScratch), typeof(BmsModManiaProfile) }));
        Assert.That(projected.Ruleset, Is.EqualTo(mania));
        Assert.That(native.ModsJson, Is.EqualTo(originalJson));
        Assert.That(BmsLocalLeaderboardService.SelectScores([native], beatmap.Hash, Constant.SHORT_NAME,
            [new BmsModHideScratch(), new BmsModManiaProfile(), new BmsModDoubleTime(), new BmsModConstant()], LeaderboardSortMode.Score, beatmap), Is.Empty);
    }

    [Test]
    public void TestNativeDaycoreRequiresMatchingSpeedForPersonalBest()
    {
        var mania = new RulesetInfo { ShortName = "mania", OnlineID = 3 };
        var beatmap = new BeatmapInfo(mania, new BeatmapDifficulty { CircleSize = 7 }) { Hash = "target-hash" };
        var native = new ScoreInfo(beatmap, mania) { Mods = [new TestDaycore()], IsLegacyScore = false };
        var display = BmsLocalLeaderboardService.SelectScores([native], beatmap.Hash, Constant.SHORT_NAME, null, LeaderboardSortMode.Score, beatmap).Single();
        Mod[] mods = [new BmsModHideScratch(), new BmsModManiaProfile()];
        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(display, mods), Is.False);
        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(display, [.. mods, new BmsModHalfTime()]), Is.True);
    }

    private class TestDoubleTime : ModDoubleTime;

    private class TestDaycore : ModDaycore;

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
