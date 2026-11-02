using System;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsScoreAggregationTest
{
    [Test]
    public void UnequalRoundsUseJudgementWeightedAccuracyAndIndependentMods()
    {
        var beatmap = new BeatmapInfo { Hash = "aggregate", Ruleset = new BmsRuleset().RulesetInfo };
        var rate = new BmsModDoubleTime { SpeedChange = { Value = 1.25 } };
        ScoreInfo[] scores =
        [
            new()
            {
                BeatmapInfo = beatmap, Accuracy = 1, MaxCombo = 10, TotalScore = 100,
                Statistics = { [HitResult.Perfect] = 10 }, MaximumStatistics = { [HitResult.Perfect] = 10 },
                Date = DateTimeOffset.UnixEpoch,
            },
            new()
            {
                BeatmapInfo = beatmap, Accuracy = 0.5, MaxCombo = 20, TotalScore = 200,
                Statistics = { [HitResult.Great] = 30 }, MaximumStatistics = { [HitResult.Perfect] = 30 },
                Date = DateTimeOffset.UnixEpoch.AddDays(1),
            },
        ];

        var aggregate = BmsScoreAggregation.CreateScore(scores, beatmap, [rate], false);

        Assert.That(aggregate.Accuracy, Is.EqualTo(0.625));
        Assert.That(aggregate.MaxCombo, Is.EqualTo(20));
        Assert.That(aggregate.TotalScore, Is.EqualTo(300));
        Assert.That(aggregate.MaximumStatistics[HitResult.Perfect], Is.EqualTo(40));
        Assert.That(aggregate.Statistics[HitResult.Perfect], Is.EqualTo(10));
        Assert.That(aggregate.Statistics[HitResult.Great], Is.EqualTo(30));
        Assert.That(aggregate.Date, Is.EqualTo(scores[1].Date));
        Assert.That(aggregate.Passed, Is.False);
        Assert.That(aggregate.Mods[0], Is.Not.SameAs(rate));
        Assert.That(((BmsModDoubleTime)aggregate.Mods[0]).SpeedChange.Value, Is.EqualTo(1.25));
    }

    [Test]
    public void EmptyAggregateKeepsBeatmapAndHasNoScore()
    {
        var beatmap = new BeatmapInfo { Hash = "empty", Ruleset = new BmsRuleset().RulesetInfo };
        var aggregate = BmsScoreAggregation.CreateScore([], beatmap, [], false);

        Assert.That(aggregate.BeatmapInfo, Is.SameAs(beatmap));
        Assert.That(aggregate.Rank, Is.EqualTo(ScoreRank.F));
        Assert.That(aggregate.Accuracy, Is.Zero);
        Assert.That(aggregate.Statistics, Is.Empty);
        Assert.That(aggregate.HitEvents, Is.Empty);
    }
}
