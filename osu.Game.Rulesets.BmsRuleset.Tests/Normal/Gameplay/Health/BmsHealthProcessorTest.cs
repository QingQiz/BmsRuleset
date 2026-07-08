using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Health;

[TestFixture]
public class BmsHealthProcessorTest
{

    [Test]
    public void TestAutoGaugeAllLayersFailTriggersFailure()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([BmsGaugeType.Hazard]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // Hazard BAD (Ok) = -1 (instant kill on Fixed algorithm).
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Ok });

        Assert.That(processor.HasFailed, Is.True);
        Assert.That(processor.HasEverFailed, Is.True);
        Assert.That(processor.Health.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestAutoGaugeCascadeSwitchAvoidsFrameworkFailure()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([
            BmsGaugeType.Hazard, BmsGaugeType.ExHard, BmsGaugeType.Hard,
            BmsGaugeType.Normal, BmsGaugeType.Easy, BmsGaugeType.AssistEasy
        ]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
                new BmsHitObject { StartTime = 3000, Column = 3 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        // Hazard starts at 1.0 (survival gauge). Apply a BAD (Ok) which is -1 for Hazard.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Ok });

        // Hazard should be failed, now active should be ExHard. Framework failure NOT triggered.
        Assert.That(processor.HasFailed, Is.False);
        Assert.That(processor.HasEverFailed, Is.False);
        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.ExHard));
        Assert.That(processor.Health.Value, Is.GreaterThan(0));
    }

    [Test]
    public void TestGaugeHistoryRecordsLayerFailureAndActiveGaugeSwitch()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([BmsGaugeType.Hard, BmsGaugeType.Normal]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.01;
        processor.ApplyHellChargeTick(false, eventTime: 1000);

        var history = processor.GaugeHistory;
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].ActiveGaugeType, Is.EqualTo(BmsGaugeType.Normal));
        Assert.That(history[0].States.Single(s => s.GaugeType == BmsGaugeType.Hard).Failed, Is.True);
        Assert.That(history[0].States.Single(s => s.GaugeType == BmsGaugeType.Hard).Health, Is.Zero);
        Assert.That(history[0].States.Single(s => s.GaugeType == BmsGaugeType.Normal).Failed, Is.False);
    }

    [Test]
    public void TestEmptyPoorGaugeHistoryDefaultsToCurrentTime()
    {
        const double current_time = 1234;
        var processor = createProcessorWithClock(current_time);

        processor.RegisterEmptyPoor();

        Assert.That(processor.GaugeHistory.Single().Time, Is.EqualTo(current_time));
    }

    [Test]
    public void TestHellChargeGaugeHistoryDefaultsToCurrentTime()
    {
        const double current_time = 1234;
        var processor = createProcessorWithClock(current_time);

        processor.ApplyHellChargeTick(false);

        Assert.That(processor.GaugeHistory.Single().Time, Is.EqualTo(current_time));
    }

    [Test]
    public void TestAutoGaugeClearCascadeAtSongEnd()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([BmsGaugeType.Normal, BmsGaugeType.Easy, BmsGaugeType.AssistEasy]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // Normal gauge: initial health 0.2, clear threshold 0.8 (fails).
        // Easy gauge: initial health 0.2, clear threshold 0.8 (fails).
        // AssistEasy: initial health 0.2, clear threshold 0.6 → at 0.2 it also fails.
        // All fail → HasPassedAtEnd returns false.
        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.Normal));
        Assert.That(processor.HasPassedAtEnd(), Is.False);
        Assert.That(processor.WorstGaugeType, Is.EqualTo(BmsGaugeType.AssistEasy));
    }

    [Test]
    public void TestAutoGaugeDedupOnSetGaugeTypes()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([BmsGaugeType.Hard, BmsGaugeType.Hard, BmsGaugeType.Easy]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // Should have 2 states (Hard first, Easy second), NOT 3.
        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.Hard));

        // SetGaugeType with an already-present type is a no-op.
        processor.SetGaugeType(BmsGaugeType.Easy);
        // GaugeType should still be Hard (the active one wasn't changed).
        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.Hard));
    }

    [Test]
    public void TestAutoGaugeWorstTypeReflectsPassedGauge()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeTypes([BmsGaugeType.Normal, BmsGaugeType.Easy]);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 200,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // PGREAT gain = Total/100/NoteCount * 1 = 200/100/1 = 2.0
        // Normal HP = 0.2 + 2.0 = 2.2 → clamped to 1.0 (≥0.8 pass)
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });

        Assert.That(processor.HasPassedAtEnd(), Is.True);
        Assert.That(processor.WorstGaugeType, Is.EqualTo(BmsGaugeType.Normal));
    }

    [Test]
    public void TestEmptyPoorCanTriggerFailure()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeType(BmsGaugeType.Hard);
        processor.ApplyBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        });

        processor.Health.Value = 0.01;
        processor.RegisterEmptyPoor();

        Assert.That(processor.Health.Value, Is.Zero);
        Assert.That(processor.HasEverFailed, Is.True);
    }

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
    public void TestGaugeBadReducesHealthByThreePercent()
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

        var expectedHealth = Math.Max(0.0, initialHealth - 0.03);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    private static BmsHealthProcessor createProcessorWithClock(double currentTime)
    {
        var processor = new BmsHealthProcessor
        {
            Clock = new FramedClock(new ManualClock { CurrentTime = currentTime }),
        };

        processor.ApplyBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        });

        return processor;
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
    public void TestHardGaugePassesWhenNeverFailed()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeType(BmsGaugeType.Hard);

        processor.ApplyBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        });

        processor.Health.Value = 0.01;

        Assert.That(processor.HasPassedAtEnd(), Is.True);
    }

    [Test]
    public void TestHazardBadForcesFailure()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeType(BmsGaugeType.Hazard);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Ok,
        });

        Assert.That(processor.Health.Value, Is.Zero);
        Assert.That(processor.HasEverFailed, Is.True);
        Assert.That(processor.HasPassedAtEnd(), Is.False);
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
                new BmsLandmine { StartTime = 1000, Column = 1, LandmineDamagePercent = 25 },
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
                new BmsLandmine { StartTime = 1000, Column = 1, LandmineDamagePercent = 25 },
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
                new BmsLandmine { StartTime = 1500, Column = 2, LandmineDamagePercent = 25 },
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
                new BmsLandmine { StartTime = 1000, Column = 1, LandmineDamagePercent = 647.5 },
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

    [Test]
    public void TestNormalGaugeFailsBelowClearThresholdAtEnd()
    {
        var processor = new BmsHealthProcessor();

        processor.ApplyBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        });

        processor.Health.Value = 0.79;

        Assert.That(processor.HasPassedAtEnd(), Is.False);
    }

    [Test]
    public void TestSetGaugeTypeChangesInitialHealth()
    {
        var processor = new BmsHealthProcessor();
        processor.SetGaugeType(BmsGaugeType.Hard);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.Hard));
        Assert.That(processor.Health.Value, Is.EqualTo(1).Within(0.001));
        Assert.That(processor.DisplayProfile.Value.ColourMode, Is.EqualTo(BmsGaugeColourMode.Fixed));
    }
}
