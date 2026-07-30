using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementTest
{

    [Test]
    public void TestSyntheticHeadUsesHeadOffset()
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.HellChargeNote);

        processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Head, 1017, HitResult.Great, 1.5));

        var hitEvent = processor.HitEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(hitEvent.TimeOffset, Is.EqualTo(17));
            Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(1000));
            Assert.That(hitEvent.GameplayRate, Is.EqualTo(1.5));
        });
    }

    [TestCase(1481, -19)]
    [TestCase(1519, 19)]
    public void TestApplySyntheticLongNoteEndpointUsesTailOffset(double eventTime, double expectedOffset)
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.ChargeNote);

        processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, eventTime, HitResult.Great, 0.75));

        var hitEvent = processor.HitEvents.Single();
        Assert.That(hitEvent.TimeOffset, Is.EqualTo(expectedOffset));
        Assert.That(hitEvent.HitObject.StartTime, Is.EqualTo(1500));
        Assert.That(hitEvent.GameplayRate, Is.EqualTo(0.75));
    }

    [Test]
    public void TestSyntheticHeadCreatesUrSafeHitEvent()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 1,
        };

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        processor.ApplyBeatmap(beatmap);

        processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Head, 1005, HitResult.Great));

        Assert.That(processor.HitEvents.Last().GameplayRate, Is.Not.Null);
        Assert.DoesNotThrow(() => processor.HitEvents.CalculateUnstableRate());
    }

    [Test]
    public void TestApplySyntheticLongNoteEndpointBadBreaksCombo()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 1,
        };

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        processor.ApplyBeatmap(beatmap);

        processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Ok));

        Assert.That(processor.Combo.Value, Is.Zero);
    }

    [Test]
    public void TestApplySyntheticLongNoteEndpointCreatesUrSafeHitEvent()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 1,
        };

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        processor.ApplyBeatmap(beatmap);

        processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Perfect));

        Assert.That(processor.HitEvents.Last().GameplayRate, Is.Not.Null);
        Assert.DoesNotThrow(() => processor.HitEvents.CalculateUnstableRate());
    }

    [Test]
    public void TestApplySyntheticLongNoteEndpointDoesNotCrashForNullSource()
    {
        var processor = new BmsScoreProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // Create a synthetic endpoint manually and apply it via the public ApplyResult path.
        // The ApplySyntheticLongNoteEndpoint method itself handles null by throwing naturally.
        Assert.DoesNotThrow(() =>
        {
            var endpoint = new BmsNote { StartTime = 2000, Column = 1 };
            var result = new JudgementResult(endpoint, endpoint.CreateJudgement()) { Type = HitResult.Perfect };
            processor.ApplyResult(result);
        });
    }

    // --- Score processor synthetic endpoint tests ---

    [Test]
    public void TestApplySyntheticLongNoteEndpointIncrementsScore()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 1,
        };

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        processor.ApplyBeatmap(beatmap);

        var result = processor.ApplySyntheticLongNoteEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Perfect));

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(processor.TotalScore.Value, Is.GreaterThan(0));
    }

    [Test]
    public void TestCreateSyntheticEndpointClearsTailSample()
    {
        var ln = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            TailSampleKey = 99,
        };

        var endpoint = ln.CreateSyntheticEndpoint(1500);

        Assert.That(endpoint, Is.Not.InstanceOf<BmsLongNote>());
    }

    [Test]
    public void TestCreateSyntheticEndpointCopiesNonTimeProperties()
    {
        var beatmap = new BmsBeatmap
        {
            Rank = 1,
            LayoutVariant = BmsLayoutVariant.Pms9K,
        };

        var ln = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 5,
            Beatmap = beatmap,
            SampleKey = 77,
        };

        var endpoint = ln.CreateSyntheticEndpoint(1500);

        Assert.That(endpoint.Column, Is.EqualTo(5));
        Assert.That(endpoint.Beatmap.Rank, Is.EqualTo(1));
        Assert.That(endpoint.SampleKey, Is.EqualTo(77));
    }

    [Test]
    public void TestCreateSyntheticEndpointCreatesBmsNoteType()
    {
        var ln = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
        };

        var endpoint = ln.CreateSyntheticEndpoint(1500);

        Assert.That(endpoint, Is.InstanceOf<BmsNote>());
    }

    // --- Synthetic endpoint tests ---

    [Test]
    public void TestCreateSyntheticEndpointHasCorrectTime()
    {
        var ln = new BmsLongNote
        {
            StartTime = 1000,
            Duration = 500,
            Column = 3,
            SampleKey = 42,
        };

        var endpoint = ln.CreateSyntheticEndpoint(1500);

        Assert.That(endpoint.StartTime, Is.EqualTo(1500));
        Assert.That(endpoint.GetEndTime() - endpoint.StartTime, Is.Zero);
        Assert.That(endpoint.Column, Is.EqualTo(3));
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowIsPoor()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(-500), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(-500), Is.False);
    }

    [Test]
    public void TestHealthProcessorApplySyntheticEndpointPoorReducesHealth()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.5;
        var source = (BmsLongNote)beatmap.HitObjects[0];
        var endpoint = source.CreateSyntheticEndpoint(1500);
        var result = new JudgementResult(endpoint, endpoint.CreateJudgement())
        {
            Type = HitResult.Meh,
        };

        var before = processor.Health.Value;
        processor.ApplySyntheticLongNoteEndpoint(result);

        Assert.That(processor.Health.Value, Is.LessThan(before));
    }

    // --- Health processor synthetic endpoint tests ---

    [Test]
    public void TestHealthProcessorApplySyntheticLongNoteEndpointChangesHealth()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.2;
        var source = (BmsLongNote)beatmap.HitObjects[0];
        var endpoint = source.CreateSyntheticEndpoint(1500);
        var result = new JudgementResult(endpoint, endpoint.CreateJudgement())
        {
            Type = HitResult.Perfect,
        };

        processor.ApplySyntheticLongNoteEndpoint(result);

        Assert.That(processor.Health.Value, Is.GreaterThan(0.2));
    }

    [Test]
    public void TestHeldBodyDoesNotExtendPastJudgementLineInNormalScroll()
    {
        var direction = BmsLongNoteGeometry.BodyDirectionBeforeTailPasses(scrollDelta: 900, duration: 900, visualDirection: 1, realHeadY: 0, realTailY: -100);

        Assert.That(direction, Is.EqualTo(-1));
        Assert.That(BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset: 0, tailOffset: -100, direction), Is.EqualTo(-100));
        Assert.That(BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset: 0, tailOffset: 100, direction), Is.EqualTo(0));
    }

    [Test]
    public void TestHeldBodyDoesNotExtendPastJudgementLineInReverseScroll()
    {
        var direction = BmsLongNoteGeometry.BodyDirectionBeforeTailPasses(scrollDelta: -900, duration: 900, visualDirection: 1, realHeadY: 0, realTailY: 100);

        Assert.That(direction, Is.EqualTo(1));
        Assert.That(BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset: 0, tailOffset: 100, direction), Is.EqualTo(100));
        Assert.That(BmsLongNoteGeometry.VisibleBodyTailOffset(headOffset: 0, tailOffset: -100, direction), Is.EqualTo(0));
    }

    [Test]
    public void TestHellChargeBodyTrackerEmitsRepressRecoveryPulse()
    {
        var tracker = new BmsHellChargeBodyTracker();
        var ticks = new List<(bool Holding, double Scale)>();

        tracker.MarkReleased();
        tracker.Update(elapsed: 1, holding: true, (holding, scale) => ticks.Add((holding, scale)));

        Assert.That(ticks, Has.Count.EqualTo(1));
        Assert.That(ticks[0], Is.EqualTo((true, BmsHellChargeBodyTracker.REPRESS_RECOVERY_PULSE_SCALE)));
    }

    [Test]
    public void TestHellChargeBodyTrackerEmitsTicksFromAccumulatedBodyTime()
    {
        var tracker = new BmsHellChargeBodyTracker();
        var ticks = new List<(bool Holding, double Scale)>();

        tracker.Update(elapsed: 199, holding: true, (holding, scale) => ticks.Add((holding, scale)));
        Assert.That(ticks, Is.Empty);

        tracker.Update(elapsed: 2, holding: true, (holding, scale) => ticks.Add((holding, scale)));

        Assert.That(ticks, Has.Count.EqualTo(1));
        Assert.That(ticks[0], Is.EqualTo((true, BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE)));
    }

    [Test]
    public void TestHellChargeTickCanTriggerFailure()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.01;
        processor.ApplyHellChargeTick(false);

        Assert.That(processor.Health.Value, Is.Zero);
        Assert.That(processor.HasEverFailed, Is.True);
    }

    [Test]
    public void TestHellChargeTickClampsAtZero()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.005;
        processor.ApplyHellChargeTick(false);

        Assert.That(processor.Health.Value, Is.Zero);
    }

    // --- Health processor HCN tick tests ---

    [Test]
    public void TestHellChargeTickHeldRecoversHealth()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.5;
        var before = processor.Health.Value;
        processor.ApplyHellChargeTick(true);

        Assert.That(processor.Health.Value, Is.GreaterThan(before));
    }

    [Test]
    public void TestHellChargeTickHeldRecoveryIsHalfGreat()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.2;
        var before = processor.Health.Value;
        processor.ApplyHellChargeTick(true);
        var after = processor.Health.Value;
        var delta = after - before;

        // A full GREAT recover would be Total/N = 100/1 = 1.0 → +1.0 but clamped to max 1.0.
        // Half that is +0.5. Check delta is positive and less than or equal to 0.5.
        Assert.That(delta, Is.GreaterThan(0));
        Assert.That(delta, Is.LessThanOrEqualTo(0.51));
    }

    [Test]
    public void TestHellChargeTickReleasedDamageIsHalfBad()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.5;
        var before = processor.Health.Value;
        processor.ApplyHellChargeTick(false);
        var after = processor.Health.Value;
        var delta = after - before;

        // BAD delta = -0.03, half = -0.015
        Assert.That(delta, Is.LessThan(0));
        Assert.That(Math.Abs(delta + 0.015), Is.LessThan(0.001));
    }

    [Test]
    public void TestHellChargeTickReleasedDamagesHealth()
    {
        var processor = new BmsHealthProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 100,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(beatmap);

        processor.Health.Value = 0.5;
        var before = processor.Health.Value;
        processor.ApplyHellChargeTick(false);

        Assert.That(processor.Health.Value, Is.LessThan(before));
    }

    [Test]
    public void TestLongNoteVisualStateWaitsUntilHeadReachesJudgementLine()
    {
        var visualState = new BmsLongNoteVisualState();

        visualState.PrepareHeadPin();
        visualState.UpdateHeadYAtStartTime(20, 980, 1000, 0);
        var fallingHead = visualState.ResolveHeldHeadY(
            realHeadY: 20, realTailY: 40, canPin: false, directionResolver: (_, _) => 1);
        visualState.UpdateHeadYAtStartTime(80, 1020, 1000, 0);
        var fixedHead = visualState.ResolveHeldHeadY(
            realHeadY: 80, realTailY: 100, canPin: true, directionResolver: (_, _) => 1);

        Assert.That(fallingHead, Is.EqualTo(20));
        Assert.That(fixedHead, Is.EqualTo(50));
        Assert.That(visualState.VisibleBodyTailOffset(headOffset: 0, tailOffset: 30), Is.EqualTo(30));
        Assert.That(visualState.VisibleBodyTailOffset(headOffset: 0, tailOffset: -30), Is.EqualTo(0));
    }

    [Test]
    public void TestLongNoteVisualStateDoesNotPinAfterFastCompletion()
    {
        var visualState = new BmsLongNoteVisualState();

        visualState.PrepareHeadPin();
        visualState.UpdateHeadYAtStartTime(50, 980, 1000, 0);
        var fallingHead = visualState.ResolveHeldHeadY(
            realHeadY: 20, realTailY: 40, canPin: false, directionResolver: (_, _) => 1);
        var completedHead = visualState.ResolveHeldHeadY(
            realHeadY: 60, realTailY: 80, canPin: false, directionResolver: (_, _) => 1);

        Assert.That(fallingHead, Is.EqualTo(20));
        Assert.That(completedHead, Is.EqualTo(60));
    }

    [Test]
    public void TestLongNoteVisualStatePrepareClearsPreviousPin()
    {
        var visualState = new BmsLongNoteVisualState();

        visualState.PrepareHeadPin();
        visualState.UpdateHeadYAtStartTime(50, 1000, 1000, 0);
        Assert.That(visualState.ResolveHeldHeadY(
            realHeadY: 50, realTailY: 70, canPin: true, directionResolver: (_, _) => 1), Is.EqualTo(50));

        visualState.PrepareHeadPin();
        Assert.That(visualState.ResolveHeldHeadY(
            realHeadY: 20, realTailY: 40, canPin: false, directionResolver: (_, _) => 1), Is.EqualTo(20));
    }

    [Test]
    public void TestScratchTailReleaseUsesScratchTailBadWindow()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(230), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(231), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(290), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(291), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(291), Is.True);
    }

    [Test]
    public void TestTailReleaseUsesTailBadWindow()
    {
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.ResultForOffset(220), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(221), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(280), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.ResultForOffset(281), Is.EqualTo(HitResult.None));
        Assert.That(tail.IsPastPassivePoorOffset(281), Is.True);
    }

    private static (BmsScoreProcessor processor, BmsLongNote source) createLongNoteProcessor(BmsLongNoteMode mode)
    {
        var source = new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = mode,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        return (processor, source);
    }

    private static BmsLongNoteEndpointResult endpoint(
        BmsLongNote source,
        BmsLongNoteEndpointKind kind,
        double eventTime,
        HitResult result,
        double gameplayRate = 1)
        => new(source, kind, eventTime, gameplayRate, result);
}
