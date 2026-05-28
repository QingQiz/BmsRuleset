using System;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsHealthProcessorTest
{
    [Test]
    public void TestGaugeInitialHealthIsTwentyPercent()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        Assert.That(processor.Health.Value, Is.EqualTo(0.2).Within(0.001));
    }

    [Test]
    public void TestGaugePgreatGainDrivenByTotal()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 200,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Perfect,
        });

        Assert.That(processor.Health.Value, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void TestGaugePoorReducesHealthByFourPointEightPercent()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        var initialHealth = processor.Health.Value;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Meh,
        });

        var expectedHealth = Math.Max(0.0, initialHealth - 0.048);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestGaugeBadReducesHealthByThreePointTwoPercent()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        var initialHealth = processor.Health.Value;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Ok,
        });

        var expectedHealth = Math.Max(0.0, initialHealth - 0.032);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestGaugeClearConditionPassesAtEightyPercent()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 400,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });

        Assert.That(processor.HasFailed, Is.False);
        Assert.That(processor.Health.Value, Is.GreaterThanOrEqualTo(0.8));
    }

    [Test]
    public void TestGaugeClearConditionFailsBelowEightyPercent()
    {
        var processor = new BmsHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });

        Assert.That(processor.Health.Value, Is.LessThan(0.8));
        Assert.That(processor.HasFailed, Is.True);
    }
}
