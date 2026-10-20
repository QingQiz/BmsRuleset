using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteRuleTest
{
    [Test]
    public void TestRetirementPreservesExpiredBodies(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values] bool poorTail)
    {
        var run = new BmsLongNoteTestContext(mode);
        run.Press(1000, HitResult.Perfect);
        run.Release(poorTail ? 2000 : 1500, 1500);
        run.Frame(1601, 101, false);

        Assert.That(run.State.TailResult, Is.EqualTo(poorTail ? HitResult.Meh : HitResult.Perfect));
        Assert.That(run.Presentation(time: 1601).Retired, Is.True);
        Assert.That(run.Presentation(time: 1601).ClearBodyOnRetire,
            Is.EqualTo(mode != BmsLongNoteMode.HellChargeNote && !poorTail));
    }

    [Test]
    public void TestNormalTailCannotImproveGreatHead()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1040, HitResult.Great);
        run.Release(1500);
        Assert.That(run.Results, Is.EqualTo([HitResult.Great]));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void TestPmsEarlyReleaseCanBeRescuedWithinMargin(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        run.Press(1000, HitResult.Perfect);
        run.Release(1500);
        run.Frame(1699, 199, false);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Repress(1699), Is.True);
        run.Frame(1701, 2, true);
        Assert.That(run.State.TailJudged, Is.False);
        run.Release(3000);
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(mode == BmsLongNoteMode.LongNote ? run.Results.Last() : run.Output.SyntheticEndpoints.Last().Result, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsReleaseMarginRestoresAfterRewind()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        run.Press(1000, HitResult.Perfect);
        run.Release(1500);
        run.Repress(1600);
        run.Rewind(1550);
        run.Frame(1700, 150, false);
        Assert.That(run.Results, Is.EqualTo([HitResult.Ok]));
    }

    [Test]
    public void TestHellChargeSuccessfulEarlyTailKeepsRecovering()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000, rank: 3);
        run.Press(1000, HitResult.Perfect);
        run.Frame(1800, 800, true);
        run.Release(1890);
        Assert.That(run.Output.BodyTicks, Has.Count.EqualTo(4));
        run.Output.BodyTicks.Clear();
        run.Frame(1999, 199, false);
        Assert.That(run.Output.BodyTicks, Is.Empty);
        Assert.That(run.State.LongNoteStarted, Is.True);
        Assert.That(run.Presentation().Retired, Is.False);
    }

    [TestCase(-50, HitResult.Perfect, true)]
    [TestCase(-110, HitResult.Great, true)]
    [TestCase(-140, HitResult.Good, true)]
    [TestCase(-160, HitResult.Ok, false)]
    [TestCase(-400, HitResult.Meh, false)]
    public void TestHellChargeReleasedVisualFollowsTailResult(double offset, HitResult result, bool heldVisual)
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        run.Press(1000, HitResult.Perfect);
        run.Release(2000 + offset);

        Assert.That(run.Output.SyntheticEndpoints.Single().Result, Is.EqualTo(result));
        Assert.That(run.Presentation(false).HoldingBody, Is.EqualTo(heldVisual));
        Assert.That(run.Presentation(true).HoldingBody, Is.True, "Reholding a failed HCN must still pin its remaining body.");
    }

    [Test]
    public void TestHellChargeCompletedVisualEndsAtTailAcrossRewind()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        run.Press(1000, HitResult.Perfect);
        run.Release(1910);
        run.Frame(1999, 89, false);
        Assert.That(run.FinishedPublications, Is.Zero);
        Assert.That(run.Presentation(false).HoldingBody, Is.True);

        run.Frame(2000, 1, false);
        Assert.That(run.FinishedPublications, Is.EqualTo(1));
        Assert.That(run.Presentation(false).HoldingBody, Is.False);
        run.Frame(2050, 50, false);
        Assert.That(run.FinishedPublications, Is.EqualTo(1));

        run.Rewind(2000);
        Assert.That(run.State.LongNoteStarted, Is.False);
        run.Rewind(1999);
        Assert.That(run.Presentation(false).HoldingBody, Is.True);
        run.Frame(2000, 1, false);
        Assert.That(run.FinishedPublications, Is.EqualTo(1));

        run.Rewind(1900);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Presentation(false).HoldingBody, Is.False);
        Assert.That(run.Presentation(true).HoldingBody, Is.True);
    }

    [Test]
    public void TestHellChargeLateTailRemainsJudgeableAcrossRewind()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        run.Press(1000, HitResult.Perfect);
        run.Frame(2100, 100, true);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.FinishedPublications, Is.Zero);
        Assert.That(run.Release(2140), Is.True);
        Assert.That(run.Output.SyntheticEndpoints.Last().Result, Is.EqualTo(HitResult.Good));
        run.Frame(2140, 40, false);
        Assert.That(run.FinishedPublications, Is.EqualTo(1));

        run.Rewind(2100);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Release(2110), Is.True);
        Assert.That(run.Output.SyntheticEndpoints.Last().Result, Is.EqualTo(HitResult.Great));
    }

    [TestCase(1699, false)]
    [TestCase(1700, true)]
    [TestCase(1701, true)]
    public void TestPmsReleaseExpiresAtExactlyTwoHundredMilliseconds(double time, bool expired)
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        run.Press(1000, HitResult.Perfect);
        run.Release(1500);
        run.Frame(time, time - 1500, false);
        Assert.That(run.State.TailJudged, Is.EqualTo(expired));
        Assert.That(run.Results, Is.EqualTo(expired ? new[] { HitResult.Ok } : System.Array.Empty<HitResult>()));
        if (expired)
        {
            Assert.That(run.Repress(time), Is.False);
            run.Passive(3300);
            Assert.That(run.Results, Is.EqualTo(new[] { HitResult.Ok }), "expiry must judge only once");
        }
    }

    [TestCase(1683, false)]
    [TestCase(1683.001, true)]
    public void TestPmsChargeTailTimeoutUsesHeadWindow(double time, bool expired)
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);
        run.Press(1000, HitResult.Perfect);
        run.Passive(time);
        Assert.That(run.State.TailJudged, Is.EqualTo(expired));
        Assert.That(run.Output.SyntheticEndpoints.Select(j => j.Result),
            Is.EqualTo(expired ? new[] { HitResult.Meh } : System.Array.Empty<HitResult>()));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestSynchronousOutputsObserveHeadBeforeResultAndTailBeforeCommit(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode, 1000, 500);
        var observed = new List<(string Event, bool Head, bool Tail, bool Started)>();
        run.Output.StatePublished += (_, state, _) => observed.Add(("state", state.HeadJudged, state.TailJudged, state.LongNoteStarted));
        run.Output.BeforeResultCheckpoint += (_, _) => observed.Add(("main", run.State.HeadJudged, run.State.TailJudged, run.State.LongNoteStarted));
        run.Output.SyntheticEndpoint += (endpoint, applied) =>
        {
            if (!applied) observed.Add(("synthetic " + endpoint.Kind, run.State.HeadJudged, run.State.TailJudged, run.State.LongNoteStarted));
        };

        run.Press(1000, HitResult.Perfect);
        run.Release(1500);

        var expected = new List<(string, bool, bool, bool)> { ("state", true, false, true), ("main", true, false, true) };
        if (mode == BmsLongNoteMode.HellChargeNote)
            expected.Add(("state", true, false, true));
        if (mode != BmsLongNoteMode.LongNote)
            expected.Add(("synthetic Tail", true, false, true));
        expected.Add(("state", true, true, mode == BmsLongNoteMode.HellChargeNote));
        Assert.That(observed, Is.EqualTo(expected));
        Assert.That(run.State.TailJudged, Is.True);
    }

    [Test]
    public void TestHellChargeHeadPoorNotifiesIndexBeforeBothSyntheticEndpoints()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        var observed = new List<(string Event, bool Head, bool Tail, bool Started)>();
        run.Output.StatePublished += (_, state, _) => observed.Add(("state", state.HeadJudged, state.TailJudged, state.LongNoteStarted));
        run.Output.BeforeResultCheckpoint += (_, _) => observed.Add(("main", run.State.HeadJudged, run.State.TailJudged, run.State.LongNoteStarted));
        run.Output.SyntheticEndpoint += (endpoint, applied) =>
        {
            if (!applied) observed.Add(("synthetic " + endpoint.Kind, run.State.HeadJudged, run.State.TailJudged, run.State.LongNoteStarted));
        };

        run.Passive(1600);

        Assert.That(observed, Is.EqualTo(new[]
        {
            ("state", true, false, true), ("synthetic Head", true, false, true), ("synthetic Tail", true, false, true), ("state", true, true, true),
        }));
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.LongNoteStarted, Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void TestFailedTailHeldVisualDependsOnMode(BmsLongNoteMode mode, bool expected)
    {
        var run = new BmsLongNoteTestContext(mode, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        run.Release(1100);

        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.Presentation(true).HoldingBody, Is.EqualTo(expected));
    }

    [Test]
    public void TestAutomaticNormalTailDoesNotBecomePoorAfterEndTime()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        // A delayed update must preserve beatoraja's stored head judgement instead of using
        // the elapsed time past the tail as a release offset.
        run.Passive(1500 + 600);

        Assert.That(run.Results, Is.EqualTo([HitResult.Perfect]));
        Assert.That(run.State.TailJudged, Is.True);
    }

    [Test]
    public void TestAutomaticNormalTailUsesHeadJudgementOffsetAndWindow()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1080, HitResult.Good);

        run.Passive(2000);

        Assert.Multiple(() =>
        {
            Assert.That(run.Results, Is.EqualTo([HitResult.Good]));
            Assert.That(run.MainEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(run.MainEndpoints.Last().eventTime, Is.EqualTo(1580));
            Assert.That(run.MainEndpoints.Last().eventTime - run.MainEndpoints.Last().endpointTime, Is.EqualTo(80));
        });
    }

    [Test]
    public void TestChargeHeadAppliesHeadEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);

        run.Press(987, HitResult.Great);

        Assert.That(run.MainEndpoints, Is.EqualTo([(1000d, 987d, HitResult.Great)]));
    }

    [Test]
    public void TestChargeLifetimeEndUsesHeadBadWindow()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);

        // PMS head BAD ends at 183ms, unlike the 283ms tail BAD window.
        Assert.That(run.Presentation().LifetimeEnd, Is.EqualTo(1783));
    }

    [Test]
    public void TestEndpointCapturesGameplayRateWhenJudged()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);

        run.Press(987, HitResult.Great, 1.5);

        Assert.That(run.MainResults.Single().Endpoints.Single().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void TestFastHeadBeforeStartIsNotTreatedAsRewind()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(987, HitResult.Great);

        run.Frame(990, 1, true);

        Assert.That(run.MainResults, Is.Empty);
        Assert.That(run.State.LongNoteStarted, Is.True);
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowRecordsFastBadOffset()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        run.Release(1200);

        var tailEndpoint = run.MainResults.Single().Endpoints.Last();

        Assert.Multiple(() =>
        {
            Assert.That(run.MainResults.Single().Result, Is.EqualTo(HitResult.Ok));
            Assert.That(tailEndpoint.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
            Assert.That(tailEndpoint.TimeOffset, Is.EqualTo(-300));
        });
    }

    [Test]
    public void TestHcnHeadPoorStartsBodyAndRegistersHeadScoringEvent()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 500);

        // HCN head hit with POOR starts the body instead of ending the drawable.
        var ok = run.Press(1000, HitResult.Meh);

        Assert.That(ok, Is.True);
        Assert.That(run.State.LongNoteStarted, Is.True);
        Assert.That(run.Publications.Count(p => p.State.HeadJudged && !p.State.TailJudged), Is.EqualTo(1));
        Assert.That(run.State.HeadTime, Is.EqualTo(1000));
        // lifetimeEnd extends past EndTime by the tail Ok slow window + margin.
        Assert.That(run.Presentation().LifetimeEnd, Is.GreaterThan(1500));
    }

    [Test]
    public void TestHellChargeTicksUseCurrentOutputObserver()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        var originalTicks = new List<(bool Holding, double Scale)>();
        var replacementTicks = new List<(bool Holding, double Scale)>();
        run.Output.BodyTick = (_, held, scale, _) => originalTicks.Add((held, scale));
        run.Press(1000, HitResult.Perfect);
        run.Frame(1250, 250, true);
        run.Output.BodyTick = (_, held, scale, _) => replacementTicks.Add((held, scale));

        run.Frame(1500, 250, true);

        Assert.That(originalTicks, Has.Count.EqualTo(1));
        Assert.That(replacementTicks.Single(), Is.EqualTo((true, BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE)));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestRewindIntoBodyRetainsHeadAndReplaysTail(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode, 1000, 1000);
        run.Press(987, HitResult.Great);
        run.Release(2000);
        run.Rewind(1500);

        Assert.That(run.State.LongNoteStarted, Is.True);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Press(1500, HitResult.Perfect), Is.False);
        Assert.That(run.Release(2000), Is.True);
        Assert.That(run.HeadPublications, Is.EqualTo(1));

        run.Rewind(980);
        Assert.That(run.State.LongNoteStarted, Is.False);
        Assert.That(run.Press(1000, HitResult.Perfect), Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestEndSettlesActiveLongNote(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode, 1000, 1000);
        run.Press(1000, HitResult.Perfect);

        run.End(1200);
        Assert.Multiple(() =>
        {
            Assert.That(run.State.TailJudged, Is.True);
            Assert.That(run.State.LongNoteStarted, Is.False);
            Assert.That(run.Release(2000), Is.False);
            Assert.That(mode == BmsLongNoteMode.LongNote ? run.MainResults.Count : run.Output.SyntheticEndpoints.Count,
                Is.EqualTo(1));
            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                run.Output.BodyTicks.Clear();
                run.Frame(1400, 200, true);
                Assert.That(run.Output.BodyTicks, Is.Empty);
            }
        });
    }

    [Test]
    public void TestHcnTickAccruesWhileHoldingWithinBody()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, 1000, 500);
        run.Press(1000, HitResult.Meh); // start body

        // 250ms of holding inside [1000,1500]: tracker tick interval is 200ms -> one tick of scale 0.5.
        run.Frame(1250, 250, true);

        Assert.That(run.Output.BodyTicks, Has.Count.EqualTo(1));
        Assert.That(run.Output.BodyTicks[0].Holding, Is.True);
        Assert.That(run.Output.BodyTicks[0].Scale, Is.EqualTo(BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE));
    }

    [Test]
    public void TestLongChargeHeadMissImmediatelyJudgesBothEndpoints()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 10_000);

        run.Passive(1281);

        Assert.That(run.MainEndpoints, Is.EqualTo([(1000d, 1281d, HitResult.Meh)]));
        Assert.That(run.SyntheticEndpoints.ToArray(), Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));

        run.Frame(11_281, 16, false);

        Assert.That(run.SyntheticEndpoints.ToArray(), Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));
    }

    [Test]
    public void TestNonPoorTailDoesNotPublishCompletionTwice()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);
        run.Release(1500);

        var publications = run.Publications.Count;
        run.Frame(1601, 16, false);

        Assert.That(run.Publications, Has.Count.EqualTo(publications));
        Assert.That(run.Presentation(time: 1601).Retired, Is.True);
    }

    [Test]
    public void TestNonPoorTailReleaseStopsHoldImmediately()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        run.Release(1500); // PERFECT tail

        Assert.That(run.State.LongNoteStarted, Is.False); // non-POOR tail stops the hold immediately
    }

    [Test]
    public void TestNormalHeadDefersEndpointUntilCompletion()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        run.Press(1013, HitResult.Great);

        Assert.That(run.MainResults, Is.Empty);
    }

    [Test]
    public void TestNormalTailAppliesTailEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1004, HitResult.Perfect);

        run.Release(1518);

        Assert.Multiple(() =>
        {
            Assert.That(run.MainResults.Single().Endpoints.Select(e => e.Kind),
                Is.EqualTo([BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail]));
            Assert.That(run.MainEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(run.MainEndpoints.Last().eventTime, Is.EqualTo(1518));
        });
    }

    [Test]
    public void TestPassiveChargeTailMissFiresSyntheticMeh()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        run.Passive(1500 + 600);

        Assert.That(run.SyntheticEndpoints.ToArray(), Has.Length.EqualTo(1));
        Assert.That(run.SyntheticEndpoints.ToArray()[0].result, Is.EqualTo(HitResult.Meh));
        Assert.That(run.State.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissAppliesMehAndMarksTailJudgedNormal()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        // Past the head passive-poor offset (Bme7K rank-2 head Ok slow edge is +280ms).
        run.Passive(1000 + 600);

        Assert.That(run.Results, Is.EqualTo([HitResult.Meh]));
        Assert.That(run.State.TailJudged, Is.True);
        Assert.That(run.State.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestPassiveHeadMissChargeJudgesTailImmediately()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);

        run.Passive(1000 + 600);

        Assert.That(run.Results, Is.EqualTo([HitResult.Meh]));
        Assert.That(run.SyntheticEndpoints.ToArray(), Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(run.State.TailJudged, Is.True);

        run.Frame(1500 + 600, 16, false);

        Assert.That(run.SyntheticEndpoints.ToArray(), Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(run.State.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissDoesNotFireBeforePoorWindow()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        run.Passive(1000);

        Assert.That(run.Results, Is.Empty);
        Assert.That(run.State.TailJudged, Is.False);
    }

    [Test]
    public void TestPassiveNormalHeadPoorDoesNotInventTailEvent()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        run.Passive(1600);

        Assert.That(run.MainEndpoints, Is.EqualTo([(1000d, 1600d, HitResult.Meh)]));
        Assert.That(run.SyntheticEndpoints.ToArray(), Is.Empty);
    }

    [Test]
    public void TestPoorTailReleaseKeepsHoldUntilRetire()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        run.Release(2000, 1500); // POOR tail

        Assert.That(run.State.LongNoteStarted, Is.True); // body stays visible until retire
    }

    [Test]
    public void TestReplayAfterRewindCommitsOnlyReplayedHeadEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1013, HitResult.Great);
        run.Frame(1005, 1, false);

        run.Press(1017, HitResult.Great);
        run.Release(1510);

        Assert.That(run.MainResults.Single().Endpoints.Select(e => e.TimeOffset),
            Is.EqualTo([17, 10]));
    }

    [Test]
    public void TestResetClearsStateAndExposesMode()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        Assert.That(run.State.LongNoteStarted, Is.False);
        Assert.That(run.State.TailJudged, Is.False);
        Assert.That(run.Presentation().KeepAfterHeadResult, Is.False);
    }

    [Test]
    public void TestRetireFiresAfterTailGraceForPoorTail()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);
        run.Release(2000, 1500);

        // EndTime=1500; grace=50; retire once past 1550 and tail judged.
        run.Frame(1601, 16, false);

        Assert.That(run.FinishedPublications, Is.EqualTo(1));
        Assert.That(run.State.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeFastHeadDiscardsPendingEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(987, HitResult.Great);

        run.Frame(986, 1, false);

        Assert.That(run.MainResults, Is.Empty);
        Assert.That(run.State.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeHeadDiscardsPendingEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1013, HitResult.Great);

        run.Frame(1005, 1, false);

        Assert.That(run.MainResults, Is.Empty);
        Assert.That(run.State.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestTryHitChargeAppliesHeadResultImmediately()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);

        var ok = run.Press(1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(run.Results, Is.EqualTo([HitResult.Perfect]));
        Assert.That(run.HeadPublications, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitNormalStartsHoldWithoutApplyingHeadResult()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        var ok = run.Press(1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(run.State.LongNoteStarted, Is.True);
        Assert.That(run.State.TailJudged, Is.False);
        // Normal LN defers the head result to tail release; only pin+seed happened.
        Assert.That(run.Results, Is.Empty);
        Assert.That(run.HeadPublications, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitRejectsAfterHeadAlreadyJudged()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);

        var ok = run.Press(1010, HitResult.Perfect);

        Assert.That(ok, Is.False);
        Assert.That(run.HeadPublications, Is.EqualTo(1));
    }

    [Test]
    public void TestTryReleaseChargeAppliesSyntheticTailEndpoint()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.ChargeNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);


        var ok = run.Release(1500);

        Assert.That(ok, Is.True);
        // CN tail is a synthetic scoring event, NOT a second ApplyResult on the drawable.
        Assert.That(run.Results, Is.EqualTo([HitResult.Perfect])); // head only
        Assert.That(run.SyntheticEndpoints.ToArray(), Has.Length.EqualTo(1));
        Assert.That(run.SyntheticEndpoints.ToArray()[0].endpointTime, Is.EqualTo(1500)); // ln.EndTime
        Assert.That(run.State.TailJudged, Is.True);
    }

    [Test]
    public void TestTryReleaseNormalAppliesTailResultAndClearsOnNonPoor()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);


        var ok = run.Release(1500);

        Assert.That(ok, Is.True);
        Assert.That(run.Results, Is.EqualTo([HitResult.Perfect]));
        Assert.That(run.State.TailJudged, Is.True);
        // Non-POOR tail -> visuals cleared.
        Assert.That(run.Presentation(time: 1500).Retired, Is.True);
    }

    [Test]
    public void TestTryReleaseNormalPoorTailDoesNotClearVisuals()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);
        run.Press(1000, HitResult.Perfect);


        run.Release(2000, 1500);

        // POOR tail -> visuals kept (the body stays for the miss animation).
        Assert.That(run.Presentation().Retired, Is.False);
        Assert.That(run.Results, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void TestTryReleaseRejectsBeforeHoldStarted()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.LongNote, 1000, 500);

        var ok = run.Release(1500);

        Assert.That(ok, Is.False);
    }
}
