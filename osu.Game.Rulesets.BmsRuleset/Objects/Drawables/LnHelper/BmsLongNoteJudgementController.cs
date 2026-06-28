using System;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

internal sealed class BmsLongNoteJudgementController
{

    public bool LongNoteStarted { get; private set; }

    public bool TailJudged { get; private set; }

    public bool IsChargeMode { get; private set; }

    private const double passive_poor_lifetime_margin = 100;
    private const double tail_visibility_grace = 50;
    private readonly BmsHellChargeBodyTracker hellChargeTracker = new();

    private IBmsLongNoteHooks hooks = null!;
    private BmsLongNote ln = null!;
    private BmsLongNoteMode mode;

    private bool headJudged;
    private double headJudgeOffset;

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
        hellChargeTracker.Reset();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ln != null) refreshMode();
    }

    public bool TryHit(double currentTime, HitResult result)
    {
        if (headJudged || LongNoteStarted || result == HitResult.None)
            return false;

        if (mode == BmsLongNoteMode.HellChargeNote && result == HitResult.Meh)
        {
            startHellChargeBodyAfterHeadPoor(currentTime);
            return true;
        }

        headJudged = true;
        LongNoteStarted = result != HitResult.Meh;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        hooks.OnUserHeadJudged();

        // CN/HCN score the head and tail as separate beatoraja-style events.
        if (IsChargeMode)
            hooks.ApplyJudgementResult(result);

        return true;
    }

    public bool TryRelease(double currentTime, double releaseOffset, BmsJudgementWindowTable tailTable)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        if (!IsChargeMode)
        {
            applyLongNoteReleaseResult(tailTable, releaseOffset);
            return true;
        }

        if (mode == BmsLongNoteMode.HellChargeNote)
            hellChargeTracker.MarkReleased();

        applyChargeTailResult(tailTable, releaseOffset, currentTime);
        return true;
    }

    public void CheckPassiveResult(double currentTime)
    {
        var headTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.Beatmap.Rank, tail: false);

        if (!LongNoteStarted)
        {
            if (!headTable.IsPastPassivePoorOffset(currentTime - ln.StartTime))
                return;

            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                startHellChargeBodyAfterHeadPoor(currentTime);
                return;
            }

            if (IsChargeMode)
            {
                // CN: missed head -> POOR for head, then another POOR for tail.
                hooks.ApplyJudgementResult(HitResult.Meh);
                hooks.ApplySyntheticTailEndpoint(ln.EndTime, currentTime, HitResult.Meh);
                TailJudged = true;
                return;
            }

            hooks.ApplyJudgementResult(HitResult.Meh);
            TailJudged = true;
            return;
        }

        var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.Beatmap.Rank, tail: true);
        var tailOffset = currentTime - ln.EndTime;

        if (IsChargeMode)
        {
            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, currentTime);

            return;
        }

        if (tailOffset >= 0)
            applyLongNoteReleaseResult(tailTable, tailOffset);
    }

    public double ChargeTailLifetimeEnd()
    {
        var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.Beatmap.Rank, tail: true);
        return ln.EndTime + tailTable.LateWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    /// <summary>
    /// Per-frame post-result work that is judgement, not rendering: charge-tail passive miss,
    /// retire decision, and HCN body ticks. The drawable calls this AFTER its hold-explosion pulse
    /// so the per-frame order matches the original <c>UpdateKindPostResultState</c>.
    /// </summary>
    public void UpdatePostResult(double currentTime, double elapsed, bool holding)
    {
        if (IsChargeMode && LongNoteStarted && !TailJudged)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.Beatmap.Rank, tail: true);
            var tailOffset = currentTime - ln.EndTime;

            if (tailTable.IsPastPassivePoorOffset(tailOffset))
                applyChargeTailResult(tailTable, tailOffset, currentTime);
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

    private void startHellChargeBodyAfterHeadPoor(double currentTime)
    {
        if (headJudged)
            return;

        headJudged = true;
        LongNoteStarted = true;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        hooks.OnHellChargeHeadPoor(currentTime, ChargeTailLifetimeEnd());
    }

    private void applyLongNoteReleaseResult(BmsJudgementWindowTable tailTable, double tailOffset)
    {
        var heldOffset = Math.Abs(headJudgeOffset) > Math.Abs(tailOffset) ? headJudgeOffset : tailOffset;
        var result = tailTable.ResultForOffset(heldOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        hooks.ApplyJudgementResult(endpointResult);
        TailJudged = true;
        // A non-POOR tail stops the hold immediately (the drawable fades now); a POOR tail keeps the
        // body alive until retire. This mirrors the original clearVisualIfTailWasNotPoor's state write.
        if (endpointResult != HitResult.Meh)
            LongNoteStarted = false;
        hooks.ClearVisualIfTailWasNotPoor(endpointResult);
    }

    private void applyChargeTailResult(BmsJudgementWindowTable tailTable, double tailOffset, double eventTime)
    {
        if (TailJudged)
            return;

        var result = tailTable.ResultForOffset(tailOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        hooks.ApplySyntheticTailEndpoint(ln.EndTime, eventTime, endpointResult);
        TailJudged = true;
        if (endpointResult != HitResult.Meh)
            LongNoteStarted = false;
        hooks.ClearVisualIfTailWasNotPoor(endpointResult);
    }

    private double boundedHellChargeElapsed(double currentTime, double elapsed)
    {
        var frameStart = currentTime - elapsed;
        var start = Math.Max(frameStart, ln.StartTime);
        var end = Math.Min(currentTime, ln.EndTime);
        return Math.Max(0, end - start);
    }
}
