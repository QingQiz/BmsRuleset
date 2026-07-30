using System;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

internal sealed class BmsLongNoteJudgementController
{

    public bool LongNoteStarted { get; private set; }

    public bool TailJudged { get; private set; }

    public bool IsChargeMode { get; private set; }

    public bool ShouldShowHeldVisual(bool keyPressed)
        => LongNoteStarted
           && keyPressed
           && (!TailJudged || mode == BmsLongNoteMode.HellChargeNote);

    private const double passive_poor_lifetime_margin = 100;
    private const double tail_visibility_grace = 50;
    private readonly BmsHellChargeBodyTracker hellChargeTracker = new();

    private IBmsLongNoteHooks hooks = null!;
    private BmsLongNote ln = null!;
    private BmsLongNoteMode mode;

    private bool headJudged;
    private double headJudgeOffset;
    private BmsLongNoteEndpointResult? pendingHeadEndpoint;

    public void Bind(BmsLongNote hitObject, IBmsLongNoteHooks hooks)
    {
        ln = hitObject;
        this.hooks = hooks;
        refreshMode();
    }

    public void Reset()
    {
        headJudged = false;
        LongNoteStarted = false;
        TailJudged = false;
        headJudgeOffset = 0;
        pendingHeadEndpoint = null;
        hellChargeTracker.Reset();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ln != null) refreshMode();
    }

    public bool TryHit(double currentTime, HitResult result, double gameplayRate = 1)
    {
        if (headJudged || LongNoteStarted || result == HitResult.None)
            return false;

        if (mode == BmsLongNoteMode.HellChargeNote && result == HitResult.Meh)
        {
            startHellChargeBodyAfterHeadPoor(currentTime, gameplayRate);
            return true;
        }

        headJudged = true;
        LongNoteStarted = result != HitResult.Meh;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        hooks.OnUserHeadJudged();

        var headEndpoint = endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, result);

        // CN/HCN score the head and tail separately; normal LN commits both timings with its final result.
        if (IsChargeMode)
            hooks.ApplyJudgementResult(result, [headEndpoint]);
        else
            pendingHeadEndpoint = headEndpoint;

        return true;
    }

    public bool TryRelease(double currentTime, double releaseOffset, BmsJudgementWindowTable tailTable, double gameplayRate = 1)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        if (!IsChargeMode)
        {
            applyLongNoteReleaseResult(tailTable, releaseOffset, currentTime, gameplayRate, automatic: false);
            return true;
        }

        if (mode == BmsLongNoteMode.HellChargeNote)
            hellChargeTracker.MarkReleased();

        applyChargeTailResult(tailTable, releaseOffset, currentTime, gameplayRate);
        return true;
    }

    public void CheckPassiveResult(double currentTime, double gameplayRate = 1)
    {
        var headTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: false);

        if (!LongNoteStarted)
        {
            if (!headTable.IsPastPassivePoorOffset(currentTime - ln.StartTime))
                return;

            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                startHellChargeBodyAfterHeadPoor(currentTime, gameplayRate);
                return;
            }

            headJudged = true;
            headJudgeOffset = currentTime - ln.StartTime;

            if (IsChargeMode)
            {
                hooks.ApplyJudgementResult(HitResult.Meh,
                    [endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh)]);
                return;
            }

            hooks.ApplyJudgementResult(HitResult.Meh,
                [endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh)]);
            TailJudged = true;
            return;
        }

        var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: true);
        var tailOffset = currentTime - ln.EndTime;

        if (IsChargeMode)
        {
            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, currentTime, gameplayRate);

            return;
        }

        if (tailOffset >= 0)
            applyLongNoteReleaseResult(headTable, tailOffset, currentTime, gameplayRate, automatic: true);
    }

    public double ChargeTailLifetimeEnd()
    {
        var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: true);
        return ln.EndTime + tailTable.SlowWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    /// <summary>
    /// Per-frame post-result work that is judgement, not rendering: charge-tail passive miss,
    /// retire decision, and HCN body ticks. The drawable calls this AFTER its hold-explosion pulse
    /// so the per-frame order matches the original <c>UpdateKindPostResultState</c>.
    /// </summary>
    public void UpdatePostResult(double currentTime, double elapsed, bool holding, double gameplayRate = 1)
    {
        if (headJudged && currentTime < ln.StartTime + headJudgeOffset)
        {
            Reset();
            return;
        }

        if (IsChargeMode && headJudged && !TailJudged)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: true);
            var tailOffset = currentTime - ln.EndTime;

            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, currentTime, gameplayRate);
        }

        if (LongNoteStarted && currentTime > ln.EndTime + tail_visibility_grace && (!IsChargeMode || TailJudged))
        {
            LongNoteStarted = false;
            hooks.Retire();
            return;
        }

        if (mode != BmsLongNoteMode.HellChargeNote || !LongNoteStarted)
            return;

        if (currentTime < ln.StartTime || currentTime > ln.EndTime)
            return;

        var chargeElapsed = boundedHellChargeElapsed(currentTime, elapsed);
        if (chargeElapsed <= 0)
            return;

        hellChargeTracker.Update(chargeElapsed, holding, (h, s) => hooks.ApplyHellChargeTick(h, s));
    }

    private void refreshMode()
    {
        var beatmap = ln.Beatmap;
        mode = beatmap.LockedLongNoteMode == BmsLongNoteMode.Undefined
            ? BmsLongNoteMode.LongNote
            : beatmap.LockedLongNoteMode;
        IsChargeMode = mode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote;
    }

    private void startHellChargeBodyAfterHeadPoor(double currentTime, double gameplayRate)
    {
        if (headJudged)
            return;

        headJudged = true;
        LongNoteStarted = true;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        hooks.OnHellChargeHeadPoor(currentTime, ChargeTailLifetimeEnd());
        hooks.ApplySyntheticEndpoint(HitResult.Meh,
            endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh));
    }

    private void applyLongNoteReleaseResult(
        BmsJudgementWindowTable resultTable,
        double tailOffset,
        double eventTime,
        double gameplayRate,
        bool automatic)
    {
        var useHeadOffset = automatic || Math.Abs(headJudgeOffset) > Math.Abs(tailOffset);
        var heldOffset = useHeadOffset ? headJudgeOffset : tailOffset;
        var result = resultTable.ResultForOffset(heldOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        var tailEndpoint = endpoint(
            BmsLongNoteEndpointKind.Tail,
            automatic ? ln.EndTime + heldOffset : eventTime,
            gameplayRate,
            endpointResult);
        hooks.ApplyJudgementResult(endpointResult, [pendingHeadEndpoint!.Value, tailEndpoint]);
        TailJudged = true;
        // A non-POOR tail stops the hold immediately (the drawable fades now); a POOR tail keeps the
        // body alive until retire. This mirrors the original clearVisualIfTailWasNotPoor's state write.
        if (endpointResult != HitResult.Meh)
            LongNoteStarted = false;
        hooks.ClearVisualIfTailWasNotPoor(endpointResult);
    }

    private void applyChargeTailResult(BmsJudgementWindowTable tailTable, double tailOffset, double eventTime, double gameplayRate)
    {
        if (TailJudged)
            return;

        var result = tailTable.ResultForOffset(tailOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        hooks.ApplySyntheticEndpoint(endpointResult,
            endpoint(BmsLongNoteEndpointKind.Tail, eventTime, gameplayRate, endpointResult));
        TailJudged = true;
        if (endpointResult != HitResult.Meh)
            LongNoteStarted = false;
        hooks.ClearVisualIfTailWasNotPoor(endpointResult);
    }

    private BmsLongNoteEndpointResult endpoint(
        BmsLongNoteEndpointKind kind,
        double eventTime,
        double gameplayRate,
        HitResult result)
        => new(ln, kind, eventTime, gameplayRate, result);

    private double boundedHellChargeElapsed(double currentTime, double elapsed)
    {
        var frameStart = currentTime - elapsed;
        var start = Math.Max(frameStart, ln.StartTime);
        var end = Math.Min(currentTime, ln.EndTime);
        return Math.Max(0, end - start);
    }
}
