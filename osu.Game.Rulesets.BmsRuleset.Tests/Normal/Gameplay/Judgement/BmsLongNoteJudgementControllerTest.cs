using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
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
        public List<HitResult> AppliedResults { get; } = [];

        public List<HitResult> ClearedTails { get; } = [];

        public List<(double endpointTime, double eventTime, HitResult result)> SyntheticEndpoints { get; } = [];

        public List<(double eventTime, double lifetimeEnd)> HellChargeHeadPoor { get; } = [];

        public List<(bool holding, double scale)> HellChargeTicks { get; } = [];

        public int UserHeadJudgedCount;

        public int RetireCount;

        public void OnUserHeadJudged() => UserHeadJudgedCount++;

        public void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd) => HellChargeHeadPoor.Add((eventTime, lifetimeEnd));

        public void ApplyJudgementResult(HitResult result) => AppliedResults.Add(result);

        public void ClearVisualIfTailWasNotPoor(HitResult result)
        {
            // Mirror the drawable's hook contract: POOR tails keep their visuals (body stays for the miss animation).
            if (result == HitResult.Meh)
                return;

            ClearedTails.Add(result);
        }

        public void ApplySyntheticTailEndpoint(double endpointTime, double eventTime, HitResult result) => SyntheticEndpoints.Add((endpointTime, eventTime, result));

        public void ApplyHellChargeTick(bool holding, double scale) => HellChargeTicks.Add((holding, scale));

        public void Retire() => RetireCount++;
    }

    [Test]
    public void TestChargeLifetimeEndUsesTailOkWindow()
    {
        var (controller, _) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);

        var lifetimeEnd = controller.ChargeTailLifetimeEnd();

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, tail: true);
        Assert.That(lifetimeEnd, Is.EqualTo(1500 + tailTable.LateWindowFor(HitResult.Ok) + 100));
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
        // lifetimeEnd extends past EndTime by the tail Ok late window + margin.
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

        // Past the head passive-poor offset (Bme7K rank-2 head Ok late edge is +280ms).
        controller.CheckPassiveResult(currentTime: 1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
        Assert.That(controller.LongNoteStarted, Is.False);
    }

    [Test]
    public void TestPassiveHeadMissChargeAppliesMehAndSyntheticTail()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.ChargeNote, start: 1000, duration: 500);

        controller.CheckPassiveResult(currentTime: 1000 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(hooks.SyntheticEndpoints, Has.Count.EqualTo(1));
        Assert.That(hooks.SyntheticEndpoints[0].result, Is.EqualTo(HitResult.Meh));
        Assert.That(controller.TailJudged, Is.True);
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

        // EndTime = 1500; past the tail passive-poor offset (Bme7K rank-2 tail Ok late edge is +280ms).
        controller.CheckPassiveResult(currentTime: 1500 + 600);

        Assert.That(hooks.AppliedResults, Is.EqualTo([HitResult.Meh]));
        Assert.That(controller.TailJudged, Is.True);
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
