using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementControllerTest
{

    // --- helpers (shared by all tasks) ---

    private static (BmsLongNoteJudgementController controller, FakeLongNoteHooks hooks) makeController(
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
        var hooks = new FakeLongNoteHooks();
        var controller = new BmsLongNoteJudgementController();
        controller.Bind(ln, hooks);
        return (controller, hooks);
    }

    [Test]
    public void TestNormalTailCannotImproveGreatHead()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1040, HitResult.Great);
        controller.TryRelease(1500, 0, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Great]));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void TestPmsEarlyReleaseCanBeRescuedWithinMargin(BmsLongNoteMode mode)
    {
        var (controller, hooks) = makeController(mode, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(1000, HitResult.Perfect);
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true);
        controller.TryRelease(1500, -1500, table);
        controller.UpdatePostResult(1699, 199, false);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryRepress(1699), Is.True);
        controller.UpdatePostResult(1701, 2, true);
        Assert.That(controller.TailJudged, Is.False);
        controller.TryRelease(3000, 0, table);
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(mode == BmsLongNoteMode.LongNote ? hooks.AppliedResults.Last() : hooks.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void TestPmsReleaseMarginRestoresAfterRewind()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, -1500, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true));
        controller.TryRepress(1600);
        controller.Rewind(1550);
        controller.UpdatePostResult(1700, 150, false);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Ok]));
    }

    [Test]
    public void TestHellChargeSuccessfulEarlyTailKeepsRecovering()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000, rank: 3);
        controller.TryHit(1000, HitResult.Perfect);
        controller.UpdatePostResult(1800, 800, true);
        controller.TryRelease(1890, -110, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 3, true));
        hooks.HellChargeTicks.Clear();
        controller.UpdatePostResult(1999, 199, false);
        Assert.That(hooks.HellChargeTicks, Is.EqualTo(new[] { (true, 0.5) }));
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(hooks.ClearedTails, Is.Empty);
    }

    [TestCase(-50, HitResult.Perfect, true)]
    [TestCase(-110, HitResult.Great, true)]
    [TestCase(-140, HitResult.Good, true)]
    [TestCase(-160, HitResult.Ok, false)]
    [TestCase(-400, HitResult.Meh, false)]
    public void TestHellChargeReleasedVisualFollowsTailResult(double offset, HitResult result, bool heldVisual)
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(2000 + offset, offset, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        Assert.That(hooks.SyntheticJudgements.Single().result, Is.EqualTo(result));
        Assert.That(controller.ShouldShowHeldVisual(false), Is.EqualTo(heldVisual));
        Assert.That(controller.ShouldShowHeldVisual(true), Is.True, "Reholding a failed HCN must still pin its remaining body.");
    }

    [Test]
    public void TestHellChargeCompletedVisualEndsAtTailAcrossRewind()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1910, -90, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));
        controller.UpdatePostResult(1999, 89, false);
        Assert.That(hooks.RetireCount, Is.Zero);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.True);

        controller.UpdatePostResult(2000, 1, false);
        Assert.That(hooks.RetireCount, Is.EqualTo(1));
        Assert.That(controller.ShouldShowHeldVisual(false), Is.False);
        controller.UpdatePostResult(2050, 50, false);
        Assert.That(hooks.RetireCount, Is.EqualTo(1));

        controller.Rewind(2000);
        Assert.That(controller.LongNoteStarted, Is.False);
        controller.Rewind(1999);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.True);
        controller.UpdatePostResult(2000, 1, false);
        Assert.That(hooks.RetireCount, Is.EqualTo(2));

        controller.Rewind(1900);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.ShouldShowHeldVisual(false), Is.False);
        Assert.That(controller.ShouldShowHeldVisual(true), Is.True);
    }

    [Test]
    public void TestHellChargeLateTailRemainsJudgeableAcrossRewind()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(1000, HitResult.Perfect);
        controller.UpdatePostResult(2100, 100, true);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(hooks.RetireCount, Is.Zero);
        Assert.That(controller.TryRelease(2140, 140, table), Is.True);
        Assert.That(hooks.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Good));
        controller.UpdatePostResult(2140, 40, false);
        Assert.That(hooks.RetireCount, Is.EqualTo(1));

        controller.Rewind(2100);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryRelease(2110, 110, table), Is.True);
        Assert.That(hooks.SyntheticJudgements.Last().result, Is.EqualTo(HitResult.Great));
    }

    [TestCase(1699, false)]
    [TestCase(1700, true)]
    [TestCase(1701, true)]
    public void TestPmsReleaseExpiresAtExactlyTwoHundredMilliseconds(double time, bool expired)
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 2000, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, -1500, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true));
        controller.UpdatePostResult(time, time - 1500, false);
        Assert.That(controller.TailJudged, Is.EqualTo(expired));
        Assert.That(hooks.AppliedResults, Is.EqualTo(expired ? new[] { HitResult.Ok } : System.Array.Empty<HitResult>()));
        if (expired)
        {
            Assert.That(controller.TryRepress(time), Is.False);
            controller.CheckPassiveResult(3300);
            Assert.That(hooks.AppliedResults, Is.EqualTo(new[] { HitResult.Ok }), "expiry must judge only once");
        }
    }

    [TestCase(1683, false)]
    [TestCase(1683.001, true)]
    public void TestPmsChargeTailTimeoutUsesHeadWindow(double time, bool expired)
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);
        controller.TryHit(1000, HitResult.Perfect);
        controller.CheckPassiveResult(time);
        Assert.That(controller.TailJudged, Is.EqualTo(expired));
        Assert.That(hooks.SyntheticJudgements.Select(j => j.result),
            Is.EqualTo(expired ? new[] { HitResult.Meh } : System.Array.Empty<HitResult>()));
    }

    private sealed class FakeLongNoteHooks : IBmsLongNoteHooks
    {
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

        public void OnUserHeadJudged() => UserHeadJudgedCount++;

        public void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd) => HellChargeHeadPoor.Add((eventTime, lifetimeEnd));

        public void ApplyJudgementResult(HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints)
            => AppliedJudgements.Add((result, endpoints));

        public void ClearVisualIfTailWasNotPoor(HitResult result)
        {
            // Mirror the drawable's hook contract: POOR tails keep their visuals (body stays for the miss animation).
            if (result == HitResult.Meh)
                return;

            ClearedTails.Add(result);
        }

        public void ApplySyntheticEndpoint(HitResult result, BmsLongNoteEndpointResult endpoint)
            => SyntheticJudgements.Add((result, endpoint));

        public void ApplyHellChargeTick(bool holding, double scale) => HellChargeTicks.Add((holding, scale));

        public void Retire() => RetireCount++;
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void TestFailedTailHeldVisualDependsOnMode(BmsLongNoteMode mode, bool expected)
    {
        var (controller, _) = makeController(mode, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1100, -400, tailTable);

        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.ShouldShowHeldVisual(true), Is.EqualTo(expected));
    }

    [Test]
    public void TestAutomaticNormalTailDoesNotBecomePoorAfterEndTime()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);

        // A delayed update must preserve beatoraja's stored head judgement instead of using
        // the elapsed time past the tail as a release offset.
        controller.CheckPassiveResult(1500 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestAutomaticNormalTailUsesHeadJudgementOffsetAndWindow()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1080, HitResult.Good);

        controller.CheckPassiveResult(2000);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Good]));
            Assert.That(hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(hooks.AppliedEndpoints.Last().eventTime, Is.EqualTo(1580));
            Assert.That(hooks.AppliedEndpoints.Last().eventTime - hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(80));
        });
    }

    [Test]
    public void TestChargeHeadAppliesHeadEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(987, HitResult.Great);

        Assert.That(hooks.AppliedEndpoints, Is.EqualTo([(1000d, 987d, HitResult.Great)]));
    }

    [Test]
    public void TestChargeLifetimeEndUsesHeadBadWindow()
    {
        var (controller, _) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500, layout: BmsLayoutVariant.Pms9K);

        // PMS head BAD ends at 183ms, unlike the 283ms tail BAD window.
        Assert.That(controller.ChargeTailLifetimeEnd(), Is.EqualTo(1783));
    }

    [Test]
    public void TestEndpointCapturesGameplayRateWhenJudged()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(987, HitResult.Great, 1.5);

        Assert.That(hooks.AppliedJudgements.Single().endpoints.Single().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void TestFastHeadBeforeStartIsNotTreatedAsRewind()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(987, HitResult.Great);

        controller.UpdatePostResult(990, 1, true);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.True);
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowRecordsFastBadOffset()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1200, -300, tailTable);

        var tailEndpoint = hooks.AppliedJudgements.Single().endpoints.Last();

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedJudgements.Single().result, Is.EqualTo(HitResult.Ok));
            Assert.That(tailEndpoint.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
            Assert.That(tailEndpoint.TimeOffset, Is.EqualTo(-300));
        });
    }

    [Test]
    public void TestHcnHeadPoorStartsBodyAndRegistersHeadScoringEvent()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 500);

        // HCN head hit with POOR starts the body instead of ending the drawable.
        var ok = controller.TryHit(1000, HitResult.Meh);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(hooks.HellChargeHeadPoor, Has.Count.EqualTo(1));
        Assert.That(hooks.HellChargeHeadPoor[0].eventTime, Is.EqualTo(1000));
        // lifetimeEnd extends past EndTime by the tail Ok slow window + margin.
        Assert.That(hooks.HellChargeHeadPoor[0].lifetimeEnd, Is.GreaterThan(1500));
    }

    [Test]
    public void TestHellChargeTicksFollowReboundHooks()
    {
        var (controller, originalHooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 1000);
        controller.TryHit(1000, HitResult.Perfect);
        controller.UpdatePostResult(1250, 250, true);
        var ln = originalHooks.AppliedJudgements.Single().endpoints.Single().Source;
        var reboundHooks = new FakeLongNoteHooks();

        controller.Bind(ln, reboundHooks);
        controller.UpdatePostResult(1500, 250, true);

        Assert.That(originalHooks.HellChargeTicks, Has.Count.EqualTo(1));
        Assert.That(reboundHooks.HellChargeTicks.Single(), Is.EqualTo((true, BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE)));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestRewindIntoBodyRetainsHeadAndReplaysTail(BmsLongNoteMode mode)
    {
        var (controller, hooks) = makeController(mode, 1000, 1000);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(987, HitResult.Great);
        controller.TryRelease(2000, 0, tailTable);
        controller.Rewind(1500);

        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.TryHit(1500, HitResult.Perfect), Is.False);
        Assert.That(controller.TryRelease(2000, 0, tailTable), Is.True);
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));

        controller.Rewind(980);
        Assert.That(controller.LongNoteStarted, Is.False);
        Assert.That(controller.TryHit(1000, HitResult.Perfect), Is.True);
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void TestPauseCompletesActiveLongNote(BmsLongNoteMode mode)
    {
        var (controller, hooks) = makeController(mode, 1000, 1000);
        controller.TryHit(1000, HitResult.Perfect);

        Assert.That(controller.CompleteAtPause(1200), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(controller.TailJudged, Is.True);
            Assert.That(controller.LongNoteStarted, Is.False);
            Assert.That(controller.TryRelease(2000, 0,
                BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true)), Is.False);
            Assert.That(mode == BmsLongNoteMode.LongNote ? hooks.AppliedResults.Count : hooks.SyntheticJudgements.Count,
                Is.EqualTo(1));
            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                hooks.HellChargeTicks.Clear();
                controller.UpdatePostResult(1400, 200, true);
                Assert.That(hooks.HellChargeTicks, Is.Empty);
            }
        });
    }

    [Test]
    public void TestHcnTickAccruesWhileHoldingWithinBody()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, 1000, 500);
        controller.TryHit(1000, HitResult.Meh); // start body

        // 250ms of holding inside [1000,1500]: tracker tick interval is 200ms -> one tick of scale 0.5.
        controller.UpdatePostResult(1250, 250, true);

        Assert.That(hooks.HellChargeTicks, Has.Count.EqualTo(1));
        Assert.That(hooks.HellChargeTicks[0].holding, Is.True);
        Assert.That(hooks.HellChargeTicks[0].scale, Is.EqualTo(BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE));
    }

    [Test]
    public void TestLongChargeHeadMissImmediatelyJudgesBothEndpoints()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 10_000);

        controller.CheckPassiveResult(1281);

        Assert.That(hooks.AppliedEndpoints, Is.EqualTo([(1000d, 1281d, HitResult.Meh)]));
        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));

        controller.UpdatePostResult(11_281, 16, false);

        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(11_000d, 1281d, HitResult.Meh)]));
    }

    [Test]
    public void TestNonPoorTailDoesNotRetire()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, 0, // PERFECT tail: fades immediately via the clear hook
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        controller.UpdatePostResult(1601, 16, false);

        Assert.That(hooks.RetireCount, Is.Zero); // already faded via ClearVisualIfTailWasNotPoor
        Assert.That(hooks.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestNonPoorTailReleaseStopsHoldImmediately()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1500, 0, tailTable); // PERFECT tail

        Assert.That(controller.LongNoteStarted, Is.False); // non-POOR tail stops the hold immediately
    }

    [Test]
    public void TestNormalHeadDefersEndpointUntilCompletion()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.TryHit(1013, HitResult.Great);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
    }

    [Test]
    public void TestNormalTailAppliesTailEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1004, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1518, 18, tailTable);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedJudgements.Single().endpoints.Select(e => e.Kind),
                Is.EqualTo([BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail]));
            Assert.That(hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(hooks.AppliedEndpoints.Last().eventTime, Is.EqualTo(1518));
        });
    }

    [Test]
    public void TestPassiveChargeTailMissFiresSyntheticMeh()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);

        controller.CheckPassiveResult(1500 + 600);

        Assert.That(hooks.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(hooks.SyntheticEndpoints[0].result, Is.EqualTo(HitResult.Meh));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissAppliesMehAndMarksTailJudgedNormal()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        // Past the head passive-poor offset (Bme7K rank-2 head Ok slow edge is +280ms).
        controller.CheckPassiveResult(1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestPassiveHeadMissChargeJudgesTailImmediately()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.CheckPassiveResult(1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(controller.TailJudged, Is.True);

        controller.UpdatePostResult(1500 + 600, 16, false);

        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(1500d, 1600d, HitResult.Meh)]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissDoesNotFireBeforePoorWindow()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.CheckPassiveResult(1000);

        Assert.That(hooks.AppliedResults, Is.Empty);
        Assert.That(controller.TailJudged, Is.False);
    }

    [Test]
    public void TestPassiveNormalHeadPoorDoesNotInventTailEvent()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.CheckPassiveResult(1600);

        Assert.That(hooks.AppliedEndpoints, Is.EqualTo([(1000d, 1600d, HitResult.Meh)]));
        Assert.That(hooks.SyntheticEndpoints, Is.Empty);
    }

    [Test]
    public void TestPoorTailReleaseKeepsHoldUntilRetire()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1500, 500, tailTable); // POOR tail

        Assert.That(controller.LongNoteStarted, Is.True); // body stays visible until retire
    }

    [Test]
    public void TestReplayAfterRewindCommitsOnlyReplayedHeadEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);
        controller.TryHit(1013, HitResult.Great);
        controller.UpdatePostResult(1005, 1, false);

        controller.TryHit(1017, HitResult.Great);
        controller.TryRelease(1510, 10, tailTable);

        Assert.That(hooks.AppliedJudgements.Single().endpoints.Select(e => e.TimeOffset),
            Is.EqualTo([17, 10]));
    }

    [Test]
    public void TestResetClearsStateAndExposesMode()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        // Bind already reset; after a head hit, Reset must clear it again.
        Assert.That(controller.LongNoteStarted, Is.False);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.IsChargeMode, Is.False);
    }

    [Test]
    public void TestRetireFiresAfterTailGraceForPoorTail()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, 500, // POOR tail: body stays visible until retire
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true));

        // EndTime=1500; grace=50; retire once past 1550 and tail judged.
        controller.UpdatePostResult(1601, 16, false);

        Assert.That(hooks.RetireCount, Is.EqualTo(1));
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeFastHeadDiscardsPendingEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(987, HitResult.Great);

        controller.UpdatePostResult(986, 1, false);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestRewindBeforeHeadDiscardsPendingEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1013, HitResult.Great);

        controller.UpdatePostResult(1005, 1, false);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestTryHitChargeAppliesHeadResultImmediately()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        var ok = controller.TryHit(1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitNormalStartsHoldWithoutApplyingHeadResult()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        var ok = controller.TryHit(1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(controller.TailJudged, Is.False);
        // Normal LN defers the head result to tail release; only pin+seed happened.
        Assert.That(hooks.AppliedResults, Is.Empty);
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitRejectsAfterHeadAlreadyJudged()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);

        var ok = controller.TryHit(1010, HitResult.Perfect);

        Assert.That(ok, Is.False);
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryReleaseChargeAppliesSyntheticTailEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect); // CN head already judged via ApplyJudgementResult

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(1500, 0, tailTable);

        Assert.That(ok, Is.True);
        // CN tail is a synthetic scoring event, NOT a second ApplyResult on the drawable.
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect])); // head only
        Assert.That(hooks.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(hooks.SyntheticEndpoints[0].endpointTime, Is.EqualTo(1500)); // ln.EndTime
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestTryReleaseNormalAppliesTailResultAndClearsOnNonPoor()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(1500, 0, tailTable);

        Assert.That(ok, Is.True);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(controller.TailJudged, Is.True);
        // Non-POOR tail -> visuals cleared.
        Assert.That(hooks.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestTryReleaseNormalPoorTailDoesNotClearVisuals()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        controller.TryRelease(1500, 500, tailTable);

        // POOR tail -> visuals kept (the body stays for the miss animation).
        Assert.That(hooks.ClearedTails, Is.Empty);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void TestTryReleaseRejectsBeforeHoldStarted()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, true);

        var ok = controller.TryRelease(1500, 0, tailTable);

        Assert.That(ok, Is.False);
    }
}
