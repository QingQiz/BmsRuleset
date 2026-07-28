using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementControllerTest
{

    // --- helpers (shared by all tasks) ---

    private static (BmsLongNoteJudgementController controller, FakeLongNoteHooks hooks) makeController(
        BmsLongNoteMode mode, double start, double duration, int column = 1, int rank = 2)
    {
        var ln = new BmsLongNote
        {
            StartTime = start,
            Duration = duration,
            Column = column,
            Beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
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

    [Test]
    public void TestChargeLifetimeEndUsesTailOkWindow()
    {
        var (controller, _) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);

        var lifetimeEnd = controller.ChargeTailLifetimeEnd();

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);
        Assert.That(lifetimeEnd, Is.EqualTo(1500 + tailTable.SlowWindowFor(HitResult.Ok) + 100));
    }

    [Test]
    public void TestHcnHeadPoorStartsBodyAndRegistersHeadScoringEvent()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, start: 1000, duration: 500);

        // HCN head hit with POOR starts the body instead of ending the drawable.
        var ok = controller.TryHit(currentTime: 1000, HitResult.Meh);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(hooks.HellChargeHeadPoor, Has.Count.EqualTo(1));
        Assert.That(hooks.HellChargeHeadPoor[0].eventTime, Is.EqualTo(1000));
        // lifetimeEnd extends past EndTime by the tail Ok slow window + margin.
        Assert.That(hooks.HellChargeHeadPoor[0].lifetimeEnd, Is.GreaterThan(1500));
    }

    [Test]
    public void TestHcnTickAccruesWhileHoldingWithinBody()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Meh); // start body

        // 250ms of holding inside [1000,1500]: tracker tick interval is 200ms -> one tick of scale 0.5.
        controller.UpdatePostResult(currentTime: 1250, elapsed: 250, holding: true);

        Assert.That(hooks.HellChargeTicks, Has.Count.EqualTo(1));
        Assert.That(hooks.HellChargeTicks[0].holding, Is.True);
        Assert.That(hooks.HellChargeTicks[0].scale, Is.EqualTo(BmsHellChargeBodyTracker.DEFAULT_TICK_SCALE));
    }

    [Test]
    public void TestNonPoorTailDoesNotRetire()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, 0, // PERFECT tail: fades immediately via the clear hook
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true));

        controller.UpdatePostResult(currentTime: 1601, elapsed: 16, holding: false);

        Assert.That(hooks.RetireCount, Is.Zero); // already faded via ClearVisualIfTailWasNotPoor
        Assert.That(hooks.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestNonPoorTailReleaseStopsHoldImmediately()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);

        controller.TryRelease(1500, 0, tailTable); // PERFECT tail

        Assert.That(controller.LongNoteStarted, Is.False); // non-POOR tail stops the hold immediately
    }

    [Test]
    public void TestPassiveChargeTailMissFiresSyntheticMeh()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);

        controller.CheckPassiveResult(currentTime: 1500 + 600);

        Assert.That(hooks.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(hooks.SyntheticEndpoints[0].result, Is.EqualTo(HitResult.Meh));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestPassiveHeadMissAppliesMehAndMarksTailJudgedNormal()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);

        // Past the head passive-poor offset (Bme7K rank-2 head Ok slow edge is +280ms).
        controller.CheckPassiveResult(currentTime: 1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestPassiveHeadMissChargeWaitsForTailPoorWindow()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);

        controller.CheckPassiveResult(currentTime: 1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(hooks.SyntheticEndpoints, Is.Empty);
        Assert.That(controller.TailJudged, Is.False);

        controller.UpdatePostResult(currentTime: 1500 + 600, elapsed: 16, holding: false);

        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(1500d, 2100d, HitResult.Meh)]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestLongChargeHeadMissDoesNotCreateExtremeFastTailOffset()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 10_000);

        controller.CheckPassiveResult(currentTime: 1281);

        Assert.That(hooks.AppliedEndpoints, Is.EqualTo([(1000d, 1281d, HitResult.Meh)]));
        Assert.That(hooks.SyntheticEndpoints, Is.Empty);

        controller.UpdatePostResult(currentTime: 11_281, elapsed: 16, holding: false);

        Assert.That(hooks.SyntheticEndpoints, Is.EqualTo([(11_000d, 11_281d, HitResult.Meh)]));
    }

    [Test]
    public void TestPassiveHeadMissDoesNotFireBeforePoorWindow()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);

        controller.CheckPassiveResult(currentTime: 1000);

        Assert.That(hooks.AppliedResults, Is.Empty);
        Assert.That(controller.TailJudged, Is.False);
    }

    [Test]
    public void TestPassiveTailMissAppliesMehAfterEndTime()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);

        // EndTime = 1500; past the tail passive-poor offset (Bme7K rank-2 tail Ok slow edge is +280ms).
        controller.CheckPassiveResult(currentTime: 1500 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
    }

    [Test]
    public void TestAutomaticNormalTailUsesHeadJudgementOffset()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(987, HitResult.Great);

        controller.CheckPassiveResult(1500);

        Assert.That(hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
        Assert.That(hooks.AppliedEndpoints.Last().eventTime, Is.EqualTo(1487));
        Assert.That(hooks.AppliedEndpoints.Last().eventTime - hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(-13));
    }

    [Test]
    public void TestPoorTailReleaseKeepsHoldUntilRetire()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);

        controller.TryRelease(1500, 500, tailTable); // POOR tail

        Assert.That(controller.LongNoteStarted, Is.True); // body stays visible until retire
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void TestFailedTailHeldVisualDependsOnMode(BmsLongNoteMode mode, bool expected)
    {
        var (controller, _) = makeController(mode, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);

        controller.TryRelease(1100, -400, tailTable);

        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.ShouldShowHeldVisual(keyPressed: true), Is.EqualTo(expected));
    }

    [Test]
    public void TestResetClearsStateAndExposesMode()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);

        // Bind already reset; after a head hit, Reset must clear it again.
        Assert.That(controller.LongNoteStarted, Is.False);
        Assert.That(controller.TailJudged, Is.False);
        Assert.That(controller.IsChargeMode, Is.False);
    }

    [Test]
    public void TestRetireFiresAfterTailGraceForPoorTail()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, 500, // POOR tail: body stays visible until retire
            BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true));

        // EndTime=1500; grace=50; retire once past 1550 and tail judged.
        controller.UpdatePostResult(currentTime: 1601, elapsed: 16, holding: false);

        Assert.That(hooks.RetireCount, Is.EqualTo(1));
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestTryHitChargeAppliesHeadResultImmediately()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);

        var ok = controller.TryHit(currentTime: 1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryHitNormalStartsHoldWithoutApplyingHeadResult()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);

        var ok = controller.TryHit(currentTime: 1000, HitResult.Perfect);

        Assert.That(ok, Is.True);
        Assert.That(controller.LongNoteStarted, Is.True);
        Assert.That(controller.TailJudged, Is.False);
        // Normal LN defers the head result to tail release; only pin+seed happened.
        Assert.That(hooks.AppliedResults, Is.Empty);
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestNormalHeadDefersEndpointUntilCompletion()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);

        controller.TryHit(1013, HitResult.Great);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
    }

    [Test]
    public void TestRewindBeforeHeadDiscardsPendingEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1013, HitResult.Great);

        controller.UpdatePostResult(1005, 1, holding: false);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestReplayAfterRewindCommitsOnlyReplayedHeadEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);
        controller.TryHit(1013, HitResult.Great);
        controller.UpdatePostResult(1005, 1, holding: false);

        controller.TryHit(1017, HitResult.Great);
        controller.TryRelease(1510, 10, tailTable);

        Assert.That(hooks.AppliedJudgements.Single().endpoints.Select(e => e.TimeOffset),
            Is.EqualTo(new[] { 17, 10 }));
    }

    [Test]
    public void TestFastHeadBeforeStartIsNotTreatedAsRewind()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(987, HitResult.Great);

        controller.UpdatePostResult(990, 1, holding: true);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.True);
    }

    [Test]
    public void TestRewindBeforeFastHeadDiscardsPendingEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(987, HitResult.Great);

        controller.UpdatePostResult(986, 1, holding: false);

        Assert.That(hooks.AppliedJudgements, Is.Empty);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestChargeHeadAppliesHeadEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(987, HitResult.Great);

        Assert.That(hooks.AppliedEndpoints, Is.EqualTo([(1000d, 987d, HitResult.Great)]));
    }

    [Test]
    public void TestEndpointCapturesGameplayRateWhenJudged()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, 1000, 500);

        controller.TryHit(987, HitResult.Great, gameplayRate: 1.5);

        Assert.That(hooks.AppliedJudgements.Single().endpoints.Single().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void TestNormalTailAppliesTailEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1004, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);

        controller.TryRelease(1518, 18, tailTable);

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedJudgements.Single().endpoints.Select(e => e.Kind),
                Is.EqualTo(new[] { BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail }));
            Assert.That(hooks.AppliedEndpoints.Last().endpointTime, Is.EqualTo(1500));
            Assert.That(hooks.AppliedEndpoints.Last().eventTime, Is.EqualTo(1518));
        });
    }

    [Test]
    public void TestFastReleaseBeforeTailWindowRecordsFastPoorOffset()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, 1000, 500);
        controller.TryHit(1000, HitResult.Perfect);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);

        controller.TryRelease(1200, -300, tailTable);

        var tailEndpoint = hooks.AppliedJudgements.Single().endpoints.Last();

        Assert.Multiple(() =>
        {
            Assert.That(hooks.AppliedJudgements.Single().result, Is.EqualTo(HitResult.Meh));
            Assert.That(tailEndpoint.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
            Assert.That(tailEndpoint.TimeOffset, Is.EqualTo(-300));
        });
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
    public void TestTryHitRejectsAfterHeadAlreadyJudged()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);

        var ok = controller.TryHit(1010, HitResult.Perfect);

        Assert.That(ok, Is.False);
        Assert.That(hooks.UserHeadJudgedCount, Is.EqualTo(1));
    }

    [Test]
    public void TestTryReleaseChargeAppliesSyntheticTailEndpoint()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect); // CN head already judged via ApplyJudgementResult

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: true);

        var ok = controller.TryRelease(currentTime: 1500, releaseOffset: 0, tailTable);

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
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: true);

        var ok = controller.TryRelease(currentTime: 1500, releaseOffset: 0, tailTable);

        Assert.That(ok, Is.True);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Perfect]));
        Assert.That(controller.TailJudged, Is.True);
        // Non-POOR tail -> visuals cleared.
        Assert.That(hooks.ClearedTails, Is.EqualTo([HitResult.Perfect]));
    }

    [Test]
    public void TestTryReleaseNormalPoorTailDoesNotClearVisuals()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        controller.TryHit(1000, HitResult.Perfect);

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: true);

        controller.TryRelease(currentTime: 1500, releaseOffset: 500, tailTable);

        // POOR tail -> visuals kept (the body stays for the miss animation).
        Assert.That(hooks.ClearedTails, Is.Empty);
        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void TestTryReleaseRejectsBeforeHoldStarted()
    {
        var (controller, _) = makeController(BmsLongNoteMode.LongNote, start: 1000, duration: 500);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, column: 1, rank: 2, tail: true);

        var ok = controller.TryRelease(1500, 0, tailTable);

        Assert.That(ok, Is.False);
    }
}
