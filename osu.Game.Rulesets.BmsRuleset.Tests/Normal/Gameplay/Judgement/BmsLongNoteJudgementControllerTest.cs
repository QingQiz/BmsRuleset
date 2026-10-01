using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementControllerTest
{

    // --- helpers (shared by all tasks) ---

    private static (BmsLongNoteJudgementController controller, RecordingSink sink) makeController(
        BmsLongNoteMode mode, double start, double duration, int column = 1, int rank = 2, BmsLayoutVariant layout = BmsLayoutVariant.Bme7K)
    {
        var ln = new BmsLongNote
        {
            StartTime = start,
            Duration = duration,
            Column = column,
            Beatmap = new BmsBeatmap
            {
                LayoutVariant = layout,
                TotalColumns = 8,
                Rank = rank,
                LockedLongNoteMode = mode,
            },
        };
        var sink = new RecordingSink();
        var controller = new BmsLongNoteJudgementController(ln);
        return (controller, sink);
    }

    [Test]
    public void TestNormalTailCannotImproveGreatHead()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1040, HitResult.Great);
        controller.TryRelease(sink, 1500, 0, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Great]));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void TestPmsEarlyReleaseCanBeRescuedWithinMargin(BmsLongNoteMode mode)
    {
        var (controller, sink) = makeController(mode, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true);
        controller.TryRelease(sink, 1500, -1500, table);
        controller.UpdatePostResult(sink, 1699, 199, false);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryRepress(sink, 1699), Is.True);
        controller.UpdatePostResult(sink, 1701, 2, true);
        Assert.That(controller.TailJudged, Is.False);
        controller.TryRelease(sink, 3000, 0, table);
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(mode == BmsLongNoteMode.LongNote ? sink.AppliedResults.Last() : sink.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsReleaseMarginRestoresAfterRewind()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1500, -1500, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true));
        controller.TryRepress(sink, 1600);
        controller.Rewind(1550);
        controller.UpdatePostResult(sink, 1700, 150, false);
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Ok]));
    }

    [Test]
    public void TestHellChargeSuccessfulEarlyTailKeepsRecovering()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000, rank: 3);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.UpdatePostResult(sink, 1800, 800, true);
        controller.TryRelease(sink, 1890, -110, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 3, true));
        sink.HellChargeTicks.Clear();
        controller.UpdatePostResult(sink, 1999, 199, false);
        Assert.That(sink.HellChargeTicks, Is.EqualTo(new[] { (true, 0.5) }));
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(sink.ClearedTails, Is.Empty);
    }

    [TestCase(-50, HitResult.Perfect, true)]
    [TestCase(-110, HitResult.Great, true)]
    [TestCase(-140, HitResult.Good, true)]
    [TestCase(-160, HitResult.Ok, false)]
    [TestCase(-400, HitResult.Meh, false)]
    public void TestHellChargeReleasedVisualFollowsTailResult(double offset, HitResult result, bool heldVisual)
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 2000 + offset, offset, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        Assert.That(sink.SyntheticJudgements.Single().result, Is.EqualTo(result));
        Assert.That(controller.ShouldShowHeldVisual(false), Is.EqualTo(heldVisual));
        Assert.That(controller.ShouldShowHeldVisual(true), Is.True, "Reholding a failed HCN must still pin its remaining body.");
    }

    [Test]
    public void TestHellChargeCompletedVisualEndsAtTailAcrossRewind()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1910, -90, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));
        controller.UpdatePostResult(sink, 1999, 89, false);
        Assert.That(sink.RetireCount, Is.Zero);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.True);

        controller.UpdatePostResult(sink, 2000, 1, false);
        Assert.That(sink.RetireCount, Is.EqualTo(1));
        Assert.That(controller.ShouldShowHeldVisual(false), Is.False);
        controller.UpdatePostResult(sink, 2050, 50, false);
        Assert.That(sink.RetireCount, Is.EqualTo(1));

        controller.Rewind(2000);
        Assert.That(controller.LongNoteStarted, Is.False);
        controller.Rewind(1999);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.True);
        controller.UpdatePostResult(sink, 2000, 1, false);
        Assert.That(sink.RetireCount, Is.EqualTo(2));

        controller.Rewind(1900);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.False);
        Assert.That(controller.ShouldShowHeldVisual(true), Is.True);
    }

    [Test]
    public void TestHellChargeLateTailRemainsJudgeableAcrossRewind()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.UpdatePostResult(sink, 2100, 100, true);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(sink.RetireCount, Is.Zero);
        Assert.That(controller.TryRelease(sink, 2140, 140, table), Is.True);
        Assert.That(sink.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Good));
        controller.UpdatePostResult(sink, 2140, 40, false);
        Assert.That(sink.RetireCount, Is.EqualTo(1));

        controller.Rewind(2100);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryRelease(sink, 2110, 110, table), Is.True);
        Assert.That(sink.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Great));
    }

    [TestCase(1699, false)]
    [TestCase(1700, true)]
    [TestCase(1701, true)]
    public void TestPmsReleaseExpiresAtExactlyTwoHundredMilliseconds(double time, bool expired)
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1500, -1500, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true));
        controller.UpdatePostResult(sink, time, time - 1500, false);
        Assert.That(controller.TailJudged, Is.EqualTo(expired));
        Assert.That(sink.AppliedResults, Is.EqualTo(expired ? new[] { HitResult.Ok } : System.Array.Empty<HitResult>()));
        if (expired)
        {
            Assert.That(controller.TryRepress(sink, time), Is.False);
            controller.CheckPassiveResult(sink, 3300);
            Assert.That(sink.AppliedResults, Is.EqualTo(new[] { HitResult.Ok }), "expiry must judge only once");
        }
    }

    [TestCase(1683, false)]
    [TestCase(1683.001, true)]
    public void TestPmsChargeTailTimeoutUsesHeadWindow(double time, bool expired)
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.CheckPassiveResult(sink, time);
        Assert.That(controller.TailJudged, Is.EqualTo(expired));
        Assert.That(sink.SyntheticJudgements.Select(j => j.result),
            Is.EqualTo(expired ? new[] { HitResult.Meh } : System.Array.Empty<HitResult>()));
    }

    private sealed class RecordingSink : IBmsLongNoteJudgementSink
    {
        public System.Action<string> Observe { get; set; } = _ => { };

        public System.Action<bool, double> HellChargeTick { get; }

        public RecordingSink() => HellChargeTick = ApplyHellChargeTick;

        public List<(HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints)> AppliedJudgements { get; } = [];

        public IReadOnlyList<HitResult> AppliedResults => AppliedJudgements.Select(j => j.result).ToList();

        public IReadOnlyList<(double endpointTime, double eventTime, HitResult result)> AppliedEndpoints =>
            AppliedJudgements.SelectMany(j => j.endpoints.Select(e => (e.ExpectedTime, e.EventTime, e.Result))).ToList();

        public List<HitResult> ClearedTails { get; } = [];

        public List<(HitResult result, BmsLongNoteEndpointResult endpoint)> SyntheticJudgements { get; } = [];

        public IReadOnlyList<(double endpointTime, double eventTime, HitResult result)> SyntheticEndpoints =>
            SyntheticJudgements.Select(j => (j.endpoint.ExpectedTime, j.endpoint.EventTime, j.endpoint.Result)).ToList();

        public List<(double eventTime, double lifetimeEnd)> HellChargeHeadPoor { get; } = [];

        public List<(bool holding, double scale)> HellChargeTicks { get; } = [];

        public int UserHeadJudgedCount;

        public int RetireCount;

        public void OnUserHeadJudged(BmsLongNote source)
        {
            Observe("pin");
            UserHeadJudgedCount++;
        }

        public void OnHellChargeHeadPoor(BmsLongNote source, double eventTime, double lifetimeEnd)
        {
            Observe("poor");
            HellChargeHeadPoor.Add((eventTime, lifetimeEnd));
        }

        public void ApplyJudgementResult(BmsLongNote source, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints)
        {
            Observe("main");
            AppliedJudgements.Add((result, endpoints));
        }

        public void ClearVisualIfTailWasNotPoor(BmsLongNote source, HitResult result)
        {
            Observe("clear");
            // POOR tails keep their body visible for the miss animation.
            if (result == HitResult.Meh)
                return;

            ClearedTails.Add(result);
        }

        public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint)
        {
            Observe("synthetic " + endpoint.Kind);
            SyntheticJudgements.Add((endpoint.Result, endpoint));
        }

        public void ApplyHellChargeTick(bool holding, double scale) => HellChargeTicks.Add((holding, scale));

        public void Retire(BmsLongNote source) => RetireCount++;
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestSynchronousOutputsObserveHeadBeforeResultAndTailBeforeCommit(BmsLongNoteMode mode)
    {
        var (controller, sink) = makeController(mode, 1000, 500);
        var observed = new List<(string Event, bool Head, bool Tail, bool Started)>();
        sink.Observe = name => observed.Add((name, controller.HeadJudged, controller.TailJudged, controller.LongNoteStarted));

        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1500, 0, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        var expected = new List<(string, bool, bool, bool)> { ("pin", true, false, true), ("main", true, false, true) };
        if (mode != BmsLongNoteMode.LongNote)
            expected.Add(("synthetic Tail", true, false, true));
        if (mode != BmsLongNoteMode.HellChargeNote)
            expected.Add(("clear", true, true, false));
        Assert.That(observed, Is.EqualTo(expected));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestHellChargeHeadPoorNotifiesIndexBeforeBothSyntheticEndpoints()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        var observed = new List<(string Event, bool Head, bool Tail, bool Started)>();
        sink.Observe = name => observed.Add((name, controller.HeadJudged, controller.TailJudged, controller.LongNoteStarted));

        controller.CheckPassiveResult(sink, 1600);

        Assert.That(observed, Is.EqualTo(new[]
        {
            ("poor", true, false, true), ("synthetic Head", true, false, true), ("synthetic Tail", true, false, true),
        }));
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void TestFailedTailHeldVisualDependsOnMode(BmsLongNoteMode mode, bool expected)
    {
        var (controller, sink) = makeController(mode, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1100, -400, tailTable);

        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.ShouldShowHeldVisual(true), Is.EqualTo(expected));
    }

    [Test]
    public void TestAutomaticNormalTailDoesNotBecomePoorAfterEndTime()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        // A delayed update must preserve beatoraja's stored head judgement instead of using
        // the elapsed time past the tail as a release offset.
        controller.CheckPassiveResult(sink, 1500 + 600);

        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestAutomaticNormalTailUsesHeadJudgementOffsetAndWindow()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1080, HitResult.Good);

        controller.CheckPassiveResult(sink, 2000);

        Assert.Multiple(() =>
        {
            Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Good]));
            Assert.That(sink.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(sink.AppliedEndpoints.Last().eventTime, Is.EqualTo(1580));
            Assert.That(sink.AppliedEndpoints.Last().eventTime - sink.AppliedEndpoints.Last().endpointTime, Is.EqualTo(80));
        });
    }

    [Test]
    public void TestChargeHeadAppliesHeadEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(sink, 987, HitResult.Great);

        Assert.That(sink.AppliedEndpoints, Is.EqualTo([(1000d, 987d, HitResult.Great)]));
    }

    [Test]
    public void TestChargeLifetimeEndUsesHeadBadWindow()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);

        // PMS head BAD ends at 183ms, unlike the 283ms tail BAD window.
        Assert.That(controller.ChargeTailLifetimeEnd(), Is.EqualTo(1783));
    }

    [Test]
    public void TestEndpointCapturesGameplayRateWhenJudged()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(sink, 987, HitResult.Great, 1.5);

        Assert.That(sink.AppliedJudgements.Single().endpoints.Single().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void TestFastHeadBeforeStartIsNotTreatedAsRewind()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 987, HitResult.Great);

        controller.UpdatePostResult(sink, 990, 1, true);

        Assert.That(sink.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.True);
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowRecordsFastBadOffset()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1200, -300, tailTable);

        var tailEndpoint = sink.AppliedJudgements.Single().endpoints.Last();

        Assert.Multiple(() =>
        {
            Assert.That(sink.AppliedJudgements.Single().result, Is.EqualTo(HitResult.Ok));
            Assert.That(tailEndpoint.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
            Assert.That(tailEndpoint.TimeOffset, Is.EqualTo(-300));
        });
    }

    [Test]
    public void TestHcnHeadPoorStartsBodyAndRegistersHeadScoringEvent()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 500);

        // HCN head hit with POOR starts the body instead of ending the drawable.
        var ok = controller.TryHit(sink, 1000, HitResult.Meh);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(sink.HellChargeHeadPoor, Has.Count.EqualTo(1));
        Assert.That(sink.HellChargeHeadPoor[0].eventTime, Is.EqualTo(1000));
        // lifetimeEnd extends past EndTime by the tail Ok slow window + margin.
        Assert.That(sink.HellChargeHeadPoor[0].lifetimeEnd, Is.GreaterThan(1500));
    }

    [Test]
    public void TestHellChargeTicksFollowReboundSink()
    {
        var (controller, originalSink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(originalSink, 1000, HitResult.Perfect);
        controller.UpdatePostResult(originalSink, 1250, 250, true);
        var reboundSink = new RecordingSink();

        controller.UpdatePostResult(reboundSink, 1500, 250, true);

        Assert.That(originalSink.HellChargeTicks, Has.Count.EqualTo(1));
        Assert.That(reboundSink.HellChargeTicks.Single(), Is.EqualTo((true, BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE)));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestRewindIntoBodyRetainsHeadAndReplaysTail(BmsLongNoteMode mode)
    {
        var (controller, sink) = makeController(mode, 1000, 1000);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(sink, 987, HitResult.Great);
        controller.TryRelease(sink, 2000, 0, tailTable);
        controller.Rewind(1500);

        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryHit(sink, 1500, HitResult.Perfect), Is.False);
        Assert.That(controller.TryRelease(sink, 2000, 0, tailTable), Is.True);
        Assert.That(sink.UserHeadJudgedCount, Is.EqualTo(1));

        controller.Rewind(980);
        Assert.That(controller.LongNoteStarted, Is.False);
        Assert.That(controller.TryHit(sink, 1000, HitResult.Perfect), Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestPauseCompletesActiveLongNote(BmsLongNoteMode mode)
    {
        var (controller, sink) = makeController(mode, 1000, 1000);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        Assert.That(controller.CompleteAtPause(sink, 1200), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(controller.TailJudged, Is.True);
            Assert.That(controller.LongNoteStarted, Is.False);
            Assert.That(controller.TryRelease(sink, 2000, 0,
                BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true)), Is.False);
            Assert.That(mode == BmsLongNoteMode.LongNote ? sink.AppliedResults.Count : sink.SyntheticJudgements.Count,
                Is.EqualTo(1));
            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                sink.HellChargeTicks.Clear();
                controller.UpdatePostResult(sink, 1400, 200, true);
                Assert.That(sink.HellChargeTicks, Is.Empty);
            }
        });
    }

    [Test]
    public void TestHcnTickAccruesWhileHoldingWithinBody()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Meh); // start body

        // 250ms of holding inside [1000,1500]: tracker tick interval is 200ms -> one tick of scale 0.5.
        controller.UpdatePostResult(sink, 1250, 250, true);

        Assert.That(sink.HellChargeTicks, Has.Count.EqualTo(1));
        Assert.That(sink.HellChargeTicks[0].holding, Is.True);
        Assert.That(sink.HellChargeTicks[0].scale, Is.EqualTo(BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE));
    }

    [Test]
    public void TestLongChargeHeadMissImmediatelyJudgesBothEndpoints()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 10_000);

        controller.CheckPassiveResult(sink, 1281);

        Assert.That(sink.AppliedEndpoints, Is.EqualTo([(1000d, 1281d, HitResult.Meh)]));
        Assert.That(sink.SyntheticEndpoints, Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));

        controller.UpdatePostResult(sink, 11_281, 16, false);

        Assert.That(sink.SyntheticEndpoints, Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));
    }

    [Test]
    public void TestNonPoorTailDoesNotRetire()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1500, 0, // PERFECT tail: fades immediately via the clear hook
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        controller.UpdatePostResult(sink, 1601, 16, false);

        Assert.That(sink.RetireCount, Is.Zero); // already faded via ClearVisualIfTailWasNotPoor
        Assert.That(sink.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestNonPoorTailReleaseStopsHoldImmediately()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1500, 0, tailTable); // PERFECT tail

        Assert.That(controller.LongNoteStarted, Is.False); // non-POOR tail stops the hold immediately
    }

    [Test]
    public void TestNormalHeadDefersEndpointUntilCompletion()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.TryHit(sink, 1013, HitResult.Great);

        Assert.That(sink.AppliedJudgements, Is.Empty);
    }

    [Test]
    public void TestNormalTailAppliesTailEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1004, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1518, 18, tailTable);

        Assert.Multiple(() =>
        {
            Assert.That(sink.AppliedJudgements.Single().endpoints.Select(e => e.Kind),
                Is.EqualTo([BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail]));
            Assert.That(sink.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(sink.AppliedEndpoints.Last().eventTime, Is.EqualTo(1518));
        });
    }

    [Test]
    public void TestPassiveChargeTailMissFiresSyntheticMeh()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        controller.CheckPassiveResult(sink, 1500 + 600);

        Assert.That(sink.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(sink.SyntheticEndpoints[0].result, Is.EqualTo(HitResult.Meh));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissAppliesMehAndMarksTailJudgedNormal()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        // Past the head passive-poor offset (Bme7K rank-2 head Ok slow edge is +280ms).
        controller.CheckPassiveResult(sink, 1000 + 600);

        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestPassiveHeadMissChargeJudgesTailImmediately()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.CheckPassiveResult(sink, 1000 + 600);

        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(sink.SyntheticEndpoints, Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(controller.TailJudged, Is.True);

        controller.UpdatePostResult(sink, 1500 + 600, 16, false);

        Assert.That(sink.SyntheticEndpoints, Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissDoesNotFireBeforePoorWindow()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.CheckPassiveResult(sink, 1000);

        Assert.That(sink.AppliedResults, Is.Empty);
        Assert.That(controller.TailJudged, Is.False);
    }

    [Test]
    public void TestPassiveNormalHeadPoorDoesNotInventTailEvent()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.CheckPassiveResult(sink, 1600);

        Assert.That(sink.AppliedEndpoints, Is.EqualTo([(1000d, 1600d, HitResult.Meh)]));
        Assert.That(sink.SyntheticEndpoints, Is.Empty);
    }

    [Test]
    public void TestPoorTailReleaseKeepsHoldUntilRetire()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1500, 500, tailTable); // POOR tail

        Assert.That(controller.LongNoteStarted, Is.True); // body stays visible until retire
    }

    [Test]
    public void TestReplayAfterRewindCommitsOnlyReplayedHeadEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(sink, 1013, HitResult.Great);
        controller.UpdatePostResult(sink, 1005, 1, false);

        controller.TryHit(sink, 1017, HitResult.Great);
        controller.TryRelease(sink, 1510, 10, tailTable);

        Assert.That(sink.AppliedJudgements.Single().endpoints.Select(e => e.TimeOffset),
            Is.EqualTo([17, 10]));
    }

    [Test]
    public void TestResetClearsStateAndExposesMode()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        Assert.That(controller.LongNoteStarted, Is.False);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.IsChargeMode, Is.False);
    }

    [Test]
    public void TestRetireFiresAfterTailGraceForPoorTail()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);
        controller.TryRelease(sink, 1500, 500, // POOR tail: body stays visible until retire
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        // EndTime=1500; grace=50; retire once past 1550 and tail judged.
        controller.UpdatePostResult(sink, 1601, 16, false);

        Assert.That(sink.RetireCount, Is.EqualTo(1));
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeFastHeadDiscardsPendingEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 987, HitResult.Great);

        controller.UpdatePostResult(sink, 986, 1, false);

        Assert.That(sink.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeHeadDiscardsPendingEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1013, HitResult.Great);

        controller.UpdatePostResult(sink, 1005, 1, false);

        Assert.That(sink.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestTryHitChargeAppliesHeadResultImmediately()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        var ok = controller.TryHit(sink, 1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(sink.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitNormalStartsHoldWithoutApplyingHeadResult()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        var ok = controller.TryHit(sink, 1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(controller.TailJudged, Is.False);
        // Normal LN defers the head result to tail release; only pin+seed happened.
        Assert.That(sink.AppliedResults, Is.Empty);
        Assert.That(sink.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitRejectsAfterHeadAlreadyJudged()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        var ok = controller.TryHit(sink, 1010, HitResult.Perfect);

        Assert.That(ok, Is.False);
        Assert.That(sink.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryReleaseChargeAppliesSyntheticTailEndpoint()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect); // CN head already judged via ApplyJudgementResult

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(sink, 1500, 0, tailTable);

        Assert.That(ok, Is.True);
        // CN tail is a synthetic scoring event, NOT a second ApplyResult on the drawable.
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Perfect])); // head only
        Assert.That(sink.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(sink.SyntheticEndpoints[0].endpointTime, Is.EqualTo(1500)); // ln.EndTime
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestTryReleaseNormalAppliesTailResultAndClearsOnNonPoor()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(sink, 1500, 0, tailTable);

        Assert.That(ok, Is.True);
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(controller.TailJudged, Is.True);
        // Non-POOR tail -> visuals cleared.
        Assert.That(sink.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestTryReleaseNormalPoorTailDoesNotClearVisuals()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(sink, 1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(sink, 1500, 500, tailTable);

        // POOR tail -> visuals kept (the body stays for the miss animation).
        Assert.That(sink.ClearedTails, Is.Empty);
        Assert.That(sink.AppliedResults, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void TestTryReleaseRejectsBeforeHoldStarted()
    {
        var (controller, sink) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(sink, 1500, 0, tailTable);

        Assert.That(ok, Is.False);
    }
}
