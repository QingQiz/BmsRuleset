using System.Linq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModPausedTest
{
    [Test]
    public void TestPausedModIsRegisteredAsUnplayableSystemMod()
    {
        var ruleset = new BmsRuleset();
        var mod = ruleset.GetModsFor(ModType.System).OfType<BmsModPaused>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Acronym, Is.EqualTo("PA"));
            Assert.That(mod.UserPlayable, Is.False);
            Assert.That(mod.HasImplementation, Is.True);
            Assert.That(mod.UsesDefaultConfiguration, Is.True);
        });
    }

    [Test]
    public void TestPausedModIsAddedToScore()
    {
        var score = new ScoreInfo { Mods = [] };
        score.Pauses.Add(1000);
        score.Pauses.Add(2000);

        BmsModPaused.ApplyToScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModPaused>());
    }

    [Test]
    public void TestPauseAttributionIsIdempotent()
    {
        var score = new ScoreInfo { Mods = [] };
        score.Pauses.Add(1000);

        BmsModPaused.ApplyToScore(score);
        score.Pauses.Add(2000);
        BmsModPaused.ApplyToScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModPaused>());
    }

    [Test]
    public void TestScorePopulationAttributesPauses()
    {
        var processor = new BmsScoreProcessor();
        var score = new ScoreInfo { Mods = [] };
        score.Pauses.Add(1000);

        processor.PopulateScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModPaused>());
    }

    [Test]
    public void TestPausedModRoundTripsThroughApiMod()
    {
        var ruleset = new BmsRuleset();
        var apiMod = new APIMod(new BmsModPaused());

        var converted = apiMod.ToMod(ruleset);

        Assert.That(converted, Is.TypeOf<BmsModPaused>());
    }

    [TestCase(10000, -10000, 5000)]
    [TestCase(2000, -10000, -3000)]
    [TestCase(2000, 0, 0)]
    public void TestResumeRewindTarget(double pauseTime, double minimumTime, double expected)
    {
        Assert.That(BmsPlayfield.ComputeResumeRewindTarget(pauseTime, minimumTime), Is.EqualTo(expected));
    }

    [Test]
    public void TestRepeatedPauseReusesExistingRewindWindow()
    {
        var playfield = new BmsPlayfield(new BmsBeatmap { TotalColumns = 1 });

        var firstTarget = playfield.BeginResumeRewind(10000, 0);
        var repeatedTarget = playfield.BeginResumeRewind(7000, 0);

        Assert.That(firstTarget, Is.EqualTo(5000));
        Assert.That(repeatedTarget, Is.EqualTo(firstTarget));
        Assert.That(playfield.ResumeRewindStartTime, Is.EqualTo(5000));
        Assert.That(playfield.ResumeRewindEndTime, Is.EqualTo(10000));
    }

    [Test]
    public void TestPauseAfterExistingRewindStartsNewWindow()
    {
        var playfield = new BmsPlayfield(new BmsBeatmap { TotalColumns = 1 });

        playfield.BeginResumeRewind(10000, 0);
        var nextTarget = playfield.BeginResumeRewind(11000, 0);

        Assert.That(nextTarget, Is.EqualTo(6000));
        Assert.That(playfield.ResumeRewindStartTime, Is.EqualTo(6000));
        Assert.That(playfield.ResumeRewindEndTime, Is.EqualTo(11000));
    }

    [Test]
    public void TestSystemModsDoNotHideScoreFromExactLocalFilter()
    {
        var pausedScore = new ScoreInfo
        {
            BeatmapHash = "hash",
            Ruleset = new RulesetInfo { ShortName = Constant.SHORT_NAME },
            Mods = [new BmsModPaused(), new BmsModBranchReplay()],
        };

        var selected = BmsLocalLeaderboardScoreSelector.SelectScores(
            [pausedScore],
            "hash",
            Constant.SHORT_NAME,
            [],
            LeaderboardSortMode.Score);

        Assert.That(selected, Is.EqualTo(new[] { pausedScore }));
    }
}
