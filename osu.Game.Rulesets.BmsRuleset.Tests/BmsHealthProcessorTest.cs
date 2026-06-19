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
    public void TestEmptyPoorClampsAtZero()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.01;
        processor.RegisterEmptyPoor();

        Assert.That(processor.Health.Value, Is.Zero);
    }

    [Test]
    public void TestEmptyPoorReducesHealthByTwoPercent()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.8;
        processor.RegisterEmptyPoor();

        Assert.That(processor.Health.Value, Is.EqualTo(0.78).Within(0.001));
        Assert.That(processor.HasFailed, Is.False);
    }

    [Test]
    public void TestGaugeBadReducesHealthByFourPercent()
    {
        var processor = new BmsHealthProcessor();
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

        var expectedHealth = Math.Max(0.0, initialHealth - 0.04);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestGaugeClearConditionPassesAtEightyPercent()
    {
        var processor = new BmsHealthProcessor();
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
    public void TestGaugeInitialHealthIsTwentyPercent()
    {
        var processor = new BmsHealthProcessor();
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
    public void TestGaugeIsCappedAtOneHundredPercent()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        foreach (var hitObject in beatmap.HitObjects)
        {
            processor.ApplyResult(new JudgementResult(hitObject, hitObject.CreateJudgement())
            {
                Type = HitResult.Perfect,
            });
        }

        Assert.That(processor.Health.Value, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void TestGaugePgreatGainDrivenByTotal()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 80,
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
    public void TestGaugePoorReducesHealthBySixPercent()
    {
        var processor = new BmsHealthProcessor();
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

        var expectedHealth = Math.Max(0.0, initialHealth - 0.06);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestIgnoredLandmineDoesNotChangeHealth()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1, IsMine = true, LandmineDamagePercent = 25 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.8;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.IgnoreMiss,
        });

        Assert.That(processor.Health.Value, Is.EqualTo(0.8).Within(0.001));
        Assert.That(processor.HasFailed, Is.False);
    }

    [Test]
    public void TestLandmineReducesHealthByDamagePercent()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1, IsMine = true, LandmineDamagePercent = 25 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.8;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Meh,
        });

        Assert.That(processor.Health.Value, Is.EqualTo(0.55).Within(0.001));
        Assert.That(processor.HasFailed, Is.False);
    }

    [Test]
    public void TestLandminesDoNotCountTowardsTotalGaugeRecovery()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 80,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1500, Column = 2, IsMine = true, LandmineDamagePercent = 25 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Perfect,
        });

        Assert.That(processor.Health.Value, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void TestLandmineZzForcesFailure()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1, IsMine = true, LandmineDamagePercent = 647.5 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 1;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Meh,
        });

        Assert.That(processor.Health.Value, Is.Zero);
        Assert.That(processor.HasFailed, Is.True);
    }
}
