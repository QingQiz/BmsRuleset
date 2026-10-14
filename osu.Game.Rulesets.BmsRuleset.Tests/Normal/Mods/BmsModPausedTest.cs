using System.Linq;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModPausedTest
{

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
    public void TestPausedModIsAddedToScore()
    {
        var score = new ScoreInfo { Mods = [] };
        score.Pauses.Add(1000);
        score.Pauses.Add(2000);

        BmsModPaused.ApplyToScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModPaused>());
    }

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
    public void TestPausedModRoundTripsThroughApiMod()
    {
        var ruleset = new BmsRuleset();
        var apiMod = new APIMod(new BmsModPaused());

        var converted = apiMod.ToMod(ruleset);

        Assert.That(converted, Is.TypeOf<BmsModPaused>());
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
    public void TestSystemModsDoNotHideScoreFromExactLocalFilter()
    {
        var pausedScore = new ScoreInfo
        {
            BeatmapHash = "hash",
            Ruleset = new RulesetInfo { ShortName = Constant.SHORT_NAME },
            Mods = [new BmsModPaused(), new BmsModBranchReplay()],
        };

        var selected = BmsLocalLeaderboardService.SelectScores(
            [pausedScore],
            "hash",
            Constant.SHORT_NAME,
            [],
            LeaderboardSortMode.Score);

        Assert.That(selected, Is.EqualTo([pausedScore]));
    }
}
