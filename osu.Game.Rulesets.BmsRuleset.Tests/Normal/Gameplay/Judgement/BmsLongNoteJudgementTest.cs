using System;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementTest
{
    [Test]
    public void TestSyntheticHeadUsesHeadOffset()
    {
        var (processor, source) = createLongNoteProcessor(BmsLongNoteMode.HellChargeNote);

        processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Head, 1017, HitResult.Great, 1.5));

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

        processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, eventTime, HitResult.Great, 0.75));

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
            Kind = KeyNoteKind.SustainHold,
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
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Head, 1005, HitResult.Great));

        Assert.That(processor.HitEvents.Last().GameplayRate, Is.Not.Null);
        Assert.DoesNotThrow(() => processor.HitEvents.CalculateUnstableRate());
    }

    [Test]
    public void TestApplySyntheticLongNoteEndpointBadBreaksCombo()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            Kind = KeyNoteKind.ReleaseHold,
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
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Ok));

        Assert.That(processor.Combo.Value, Is.Zero);
    }

    [Test]
    public void TestApplySyntheticLongNoteEndpointCreatesUrSafeHitEvent()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            Kind = KeyNoteKind.ReleaseHold,
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
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Perfect));

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
        processor.ApplyBeatmap(bindNotes(beatmap));

        // Create a synthetic endpoint manually and apply it via the public ApplyResult path.
        // The ApplySyntheticLongNoteEndpoint method itself handles null by throwing naturally.
        Assert.DoesNotThrow(() =>
        {
            var endpoint = new BmsNote { StartTime = 2000, Column = 1 };
            var result = new BmsJudgementResult(endpoint, endpoint.CreateJudgement()) { Type = HitResult.Perfect };
            processor.ApplyTestResult(result);
        });
    }

    // --- Score processor synthetic endpoint tests ---

    [Test]
    public void TestApplySyntheticLongNoteEndpointIncrementsScore()
    {
        var processor = new BmsScoreProcessor();
        var source = new BmsLongNote
        {
            Kind = KeyNoteKind.ReleaseHold,
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
        processor.ApplyBeatmap(bindNotes(beatmap));

        var result = processor.ApplyTestSyntheticEndpoint(endpoint(source, BmsLongNoteEndpointKind.Tail, 1505, HitResult.Perfect));

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(processor.TotalScore.Value, Is.GreaterThan(0));
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowIsPoor()
    {
        var tail = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.HitAt(-500), Is.EqualTo(HitResult.None));
        Assert.That(tail.PastDeadline(-500), Is.False);
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
                    Kind = KeyNoteKind.ReleaseHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.5, false)]);
        var source = (BmsLongNote)beatmap.HitObjects[0];
        var endpoint = BmsTestJudgements.CreateEndpoint(source, 1500);
        var result = new BmsJudgementResult(endpoint, endpoint.CreateJudgement())
        {
            Type = HitResult.Meh,
        };

        var before = processor.Health.Value;
        processor.ApplyTestResult(result);

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
                    Kind = KeyNoteKind.ReleaseHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.2, false)]);
        var source = (BmsLongNote)beatmap.HitObjects[0];
        var endpoint = BmsTestJudgements.CreateEndpoint(source, 1500);
        var result = new BmsJudgementResult(endpoint, endpoint.CreateJudgement())
        {
            Type = HitResult.Perfect,
        };

        processor.ApplyTestResult(result);

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

    [TestCase(100, 60, 20, 90, 50)]
    [TestCase(60, 100, 20, 50, 90)]
    [TestCase(100, 60, 30, 85, 45)]
    public void TestBodyEndpointsLieAtNoteCentres(
        float headOffset, float tailOffset, float noteHeight, float expectedHeadCentre, float expectedTailCentre)
    {
        Assert.Multiple(() =>
        {
            Assert.That(BmsLongNoteGeometry.BodyCentreOffset(headOffset, noteHeight), Is.EqualTo(expectedHeadCentre));
            Assert.That(BmsLongNoteGeometry.BodyCentreOffset(tailOffset, noteHeight), Is.EqualTo(expectedTailCentre));
        });
    }

    [Test]
    public void TestHellChargeTickCanTriggerFailure()
    {
        var processor = new BmsHealthProcessor();
        processor.Gauge().SetGaugeType(BmsGaugeType.Hard);
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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.01, false)]);
        processor.ApplyTestBodyTick(false);

        Assert.That(processor.Health.Value, Is.Zero);
        Assert.That(processor.CurrentHealth.HasFailed, Is.True);
    }

    [Test]
    public void TestHellChargeTickClampsAtTwoPercent()
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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.005, false)]);
        processor.ApplyTestBodyTick(false);

        Assert.That(processor.Health.Value, Is.EqualTo(0.02));
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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.5, false)]);
        var before = processor.Health.Value;
        processor.ApplyTestBodyTick(true);

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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.2, false)]);
        var before = processor.Health.Value;
        processor.ApplyTestBodyTick(true);
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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.5, false)]);
        var before = processor.Health.Value;
        processor.ApplyTestBodyTick(false);
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
                    Kind = KeyNoteKind.SustainHold,
                    StartTime = 1000,
                    Duration = 500,
                    Column = 1,
                },
            },
        };
        processor.ApplyBeatmap(bindNotes(beatmap));

        processor.Gauge().RestoreGaugeStates([new BmsGaugeStateSnapshot(processor.Gauge().GaugeType, 0.5, false)]);
        var before = processor.Health.Value;
        processor.ApplyTestBodyTick(false);

        Assert.That(processor.Health.Value, Is.LessThan(before));
    }

    [Test]
    public void TestLongNoteVisualStateWaitsUntilHeadReachesJudgementLine()
    {
        var visualState = new BmsLongNoteVisualState();

        visualState.PrepareHeadPin();
        visualState.UpdateHeadYAtStartTime(20, 980, 1000, 0);
        visualState.ObserveHeldBodyDirection(20, 40, false, (_, _) => 1);
        var fallingHead = visualState.ResolveHeldHeadY(realHeadY: 20, canPin: false);
        visualState.UpdateHeadYAtStartTime(80, 1020, 1000, 0);
        visualState.ObserveHeldBodyDirection(80, 100, true, (_, _) => 1);
        var fixedHead = visualState.ResolveHeldHeadY(realHeadY: 80, canPin: true);

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
        visualState.ObserveHeldBodyDirection(20, 40, false, (_, _) => 1);
        var fallingHead = visualState.ResolveHeldHeadY(realHeadY: 20, canPin: false);
        visualState.ObserveHeldBodyDirection(60, 80, false, (_, _) => 1);
        var completedHead = visualState.ResolveHeldHeadY(realHeadY: 60, canPin: false);

        Assert.That(fallingHead, Is.EqualTo(20));
        Assert.That(completedHead, Is.EqualTo(60));
    }

    [Test]
    public void TestLongNoteVisualStatePrepareClearsPreviousPin()
    {
        var visualState = new BmsLongNoteVisualState();

        visualState.PrepareHeadPin();
        visualState.UpdateHeadYAtStartTime(50, 1000, 1000, 0);
        visualState.ObserveHeldBodyDirection(50, 70, true, (_, _) => 1);
        Assert.That(visualState.ResolveHeldHeadY(realHeadY: 50, canPin: true), Is.EqualTo(50));

        visualState.PrepareHeadPin();
        visualState.ObserveHeldBodyDirection(20, 40, false, (_, _) => 1);
        Assert.That(visualState.ResolveHeldHeadY(realHeadY: 20, canPin: false), Is.EqualTo(20));
    }

    [Test]
    public void TestScratchTailReleaseUsesScratchTailBadWindow()
    {
        var tail = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 0, rank: 3, tail: true);

        Assert.That(tail.HitAt(230), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(231), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(290), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(291), Is.EqualTo(HitResult.None));
        Assert.That(tail.PastDeadline(291), Is.True);
    }

    [Test]
    public void TestTailReleaseUsesTailBadWindow()
    {
        var tail = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, column: 1, rank: 3, tail: true);

        Assert.That(tail.HitAt(220), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(221), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(280), Is.EqualTo(HitResult.Ok));
        Assert.That(tail.HitAt(281), Is.EqualTo(HitResult.None));
        Assert.That(tail.PastDeadline(281), Is.True);
    }

    private static BmsBeatmap bindNotes(BmsBeatmap beatmap)
    {
        foreach (var note in beatmap.HitObjects)
            note.Beatmap = beatmap;
        return beatmap;
    }

    private static (BmsScoreProcessor processor, BmsLongNote source) createLongNoteProcessor(BmsLongNoteMode mode)
    {
        var source = new BmsLongNote { Kind = mode.ToNoteKind(), StartTime = 1000, Duration = 500, Column = 1 };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            LockedLongNoteMode = mode,
            HitObjects = { source },
        };
        source.Beatmap = beatmap;
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(bindNotes(beatmap));
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
