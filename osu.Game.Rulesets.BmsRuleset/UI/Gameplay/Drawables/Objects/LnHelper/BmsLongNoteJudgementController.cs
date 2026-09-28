using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;

internal sealed class BmsLongNoteJudgementController
{

    public bool LongNoteStarted { get; private set; }

    public bool TailJudged { get; private set; }

    public bool IsChargeMode { get; private set; }

    public bool HeadJudged => headJudged;

    public double NextHeadJudgementTime => headJudged ? double.PositiveInfinity : ln.StartTime + headTable.SlowWindowFor(HitResult.Ok);

    public bool ShouldShowHeldVisual(bool keyPressed)
        => LongNoteStarted
           && (keyPressed || mode == BmsLongNoteMode.HellChargeNote && hasSuccessfulTail)
           && (!TailJudged || mode == BmsLongNoteMode.HellChargeNote);

    private const double passive_poor_lifetime_margin = 100;
    private const double tail_visibility_grace = 50;
    private readonly BmsHellChargeBodyTracker hellChargeTracker = new();
    private readonly Action<bool, double> applyHellChargeTick;

    private IBmsLongNoteHooks hooks = null!;
    private BmsLongNote ln = null!;
    private BmsJudgementWindowTable headTable = null!;
    private BmsJudgementWindowTable tailTable = null!;
    private BmsLongNoteMode mode;
    private bool headReverseScratch;

    private bool isScratch => BmsLayout.IsScratchColumn(ln.Column, ln.Beatmap.LayoutVariant);

    private double? pendingReleaseTime;
    private readonly List<(double Time, double? Release)> releaseHistory = [];

    private void setPendingRelease(double time, double? release)
    {
        pendingReleaseTime = release;
        releaseHistory.Add((time, release));
    }

    private double releaseMargin => BmsJudgementProfileProvider.IsPms(ln.Beatmap.LayoutVariant) ? 200 : 0;

    public bool TryRepress(double currentTime, bool reverseScratch = false, double gameplayRate = 1)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        if (isScratch && IsChargeMode && reverseScratch != headReverseScratch)
            applyChargeTailResult(tailTable, currentTime - ln.EndTime, currentTime, gameplayRate);

        setPendingRelease(currentTime, null);
        return true;
    }

    private bool headJudged;
    private double headJudgeOffset;
    private BmsLongNoteEndpointResult? pendingHeadEndpoint;
    private double? tailJudgementTime;
    private double lastBodyUpdateTime = double.NegativeInfinity;
    private HitResult headResult;
    private HitResult tailResult;

    // Successful HCN tails keep recovering through the remaining body, so their head must
    // stay pinned even after the player releases the key or finishes the scratch reversal.
    private bool hasSuccessfulTail => TailJudged && tailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;

    private bool hasPassedVisualEnd(double currentTime)
        => mode == BmsLongNoteMode.HellChargeNote
            ? currentTime >= ln.EndTime
            : currentTime > ln.EndTime + tail_visibility_grace;

    public BmsLongNoteJudgementController()
    {
        // HCN checks its body every frame. Reuse the callback while reading the current hooks
        // so rebinding a lifetime entry to another pooled drawable cannot target the old one.
        applyHellChargeTick = (holding, scale) => hooks.ApplyHellChargeTick(holding, scale);
    }

    public void Bind(BmsLongNote hitObject, IBmsLongNoteHooks hooks, bool preserveHellChargeHistory = true)
    {
        ln = hitObject;
        this.hooks = hooks;
        hellChargeTracker.HistoryEnabled = preserveHellChargeHistory;
        headTable = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.EffectiveJudgementRate, tail: false);
        tailTable = BmsJudgementProfileProvider.GetTable(hitObject.Beatmap.LayoutVariant, hitObject.Column, hitObject.EffectiveJudgementRate, tail: true);
        refreshMode();
    }

    public void Reset()
    {
        pendingReleaseTime = null;
        releaseHistory.Clear();
        headJudged = false;
        LongNoteStarted = false;
        TailJudged = false;
        headJudgeOffset = 0;
        headReverseScratch = false;
        pendingHeadEndpoint = null;
        tailJudgementTime = null;
        lastBodyUpdateTime = double.NegativeInfinity;
        headResult = tailResult = HitResult.None;
        hellChargeTracker.Reset();
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (ln != null) refreshMode();
    }

    public void Rewind(double currentTime)
    {
        while (releaseHistory.Count > 0 && releaseHistory[^1].Time > currentTime)
            releaseHistory.RemoveAt(releaseHistory.Count - 1);
        pendingReleaseTime = releaseHistory.Count > 0 ? releaseHistory[^1].Release : null;
        lastBodyUpdateTime = Math.Min(lastBodyUpdateTime, currentTime);

        if (!headJudged)
            return;

        if (currentTime < ln.StartTime + headJudgeOffset)
        {
            Reset();
            return;
        }

        if (tailJudgementTime > currentTime)
        {
            TailJudged = false;
            tailJudgementTime = null;
            tailResult = HitResult.None;
        }

        // A completed drawable may be back in the pool. Its head still belongs to the current
        // attempt when seeking into the body, including an uncommitted normal-LN head.
        LongNoteStarted = (headResult != HitResult.Meh || mode == BmsLongNoteMode.HellChargeNote)
                          && (!TailJudged || tailResult == HitResult.Meh || mode == BmsLongNoteMode.HellChargeNote)
                          && (!hasPassedVisualEnd(currentTime) || mode == BmsLongNoteMode.HellChargeNote && !TailJudged);
        hellChargeTracker.Rewind(currentTime);
    }

    public bool TryHit(double currentTime, HitResult result, double gameplayRate = 1, bool reverseScratch = false)
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
        headResult = result;
        headJudgeOffset = currentTime - ln.StartTime;
        headReverseScratch = reverseScratch;
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

    public bool TryRelease(double currentTime, double releaseOffset, BmsJudgementWindowTable tailTable, double gameplayRate = 1, bool reverseScratch = false)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        var releaseResult = tailTable.ResultForOffset(releaseOffset);
        // BSS tails are judged by reversing the head's direction. Releasing that direction
        // only drops a charge scratch when it is outside every tail judgement window.
        if (isScratch && (reverseScratch != headReverseScratch || IsChargeMode && releaseResult != HitResult.None))
            return false;

        if (releaseOffset < 0 && releaseResult is HitResult.None or HitResult.Ok && releaseMargin > 0)
        {
            setPendingRelease(currentTime, currentTime);
            return true;
        }

        if (!IsChargeMode)
        {
            applyLongNoteReleaseResult(tailTable, releaseOffset, currentTime, gameplayRate, automatic: false);
            return true;
        }

        if (mode == BmsLongNoteMode.HellChargeNote)
            hellChargeTracker.MarkReleased(currentTime);

        applyChargeTailResult(tailTable, releaseOffset, currentTime, gameplayRate);
        return true;
    }

    /// <summary>
    ///     Completes the active long-note judgement at a pause boundary. The pause is a
    ///     judgement boundary, so the tail is scored at the current time and the controller
    ///     no longer needs to carry an active body through the resume rewind.
    /// </summary>
    public bool CompleteAtPause(double currentTime, double gameplayRate = 1)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        var releaseTime = pendingReleaseTime ?? currentTime;
        // A pause finalises any deferred release instead of leaving a pending release attached
        // to a controller that will be kept authoritative throughout the resume rewind.
        setPendingRelease(currentTime, null);

        if (!IsChargeMode)
            applyLongNoteReleaseResult(tailTable, releaseTime - ln.EndTime, currentTime, gameplayRate, automatic: false);
        else
            applyChargeTailResult(tailTable, releaseTime - ln.EndTime, currentTime, gameplayRate);

        // A pause-completed tail is final even for HCN. Do not continue body ticks after the
        // boundary, otherwise resume would still depend on the discarded intermediate state.
        LongNoteStarted = false;

        return true;
    }

    public void CheckPassiveResult(double currentTime, double gameplayRate = 1)
    {
        resolvePendingRelease(currentTime, gameplayRate);
        if (TailJudged)
            return;

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
            headResult = HitResult.Meh;
            headJudgeOffset = currentTime - ln.StartTime;

            if (IsChargeMode)
            {
                hooks.ApplyJudgementResult(HitResult.Meh,
                    [endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh)]);
                applyMissedTail(currentTime, gameplayRate);
                return;
            }

            hooks.ApplyJudgementResult(HitResult.Meh,
                [endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh)]);
            TailJudged = true;
            tailJudgementTime = currentTime;
            tailResult = HitResult.Meh;
            return;
        }

        var tailOffset = currentTime - ln.EndTime;

        if (IsChargeMode)
        {
            if (headTable.IsPastPassivePoorOffset(tailOffset))
                applyMissedTail(currentTime, gameplayRate);

            return;
        }

        if (tailOffset >= 0)
            applyLongNoteReleaseResult(headTable, tailOffset, currentTime, gameplayRate, automatic: true);
    }

    public double ChargeTailLifetimeEnd()
    {
        return ln.EndTime + headTable.SlowWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    /// <summary>
    /// Completes passive judgement and retirement after input has been processed.
    /// </summary>
    public void UpdatePostResult(double currentTime, double elapsed, bool holding, double gameplayRate = 1)
    {
        if (headJudged && currentTime < ln.StartTime + headJudgeOffset)
        {
            Reset();
            return;
        }

        UpdateHellChargeBody(currentTime, elapsed, holding);
        resolvePendingRelease(currentTime, gameplayRate);

        if (IsChargeMode && headJudged && !TailJudged)
        {
            var tailOffset = currentTime - ln.EndTime;

            if (headTable.IsPastPassivePoorOffset(tailOffset))
                applyMissedTail(currentTime, gameplayRate);
        }

        if (LongNoteStarted && hasPassedVisualEnd(currentTime) && (!IsChargeMode || TailJudged))
        {
            LongNoteStarted = false;
            hooks.Retire();
        }
    }

    public void UpdateHellChargeBody(double currentTime, double elapsed, bool holding)
    {
        if (mode != BmsLongNoteMode.HellChargeNote || elapsed <= 0 || currentTime <= lastBodyUpdateTime)
            return;

        lastBodyUpdateTime = currentTime;

        // JudgeManager processes the body before key changes and passive misses. A head
        // committed at this timestamp cannot contribute any body time until the next frame.
        if (!LongNoteStarted || currentTime <= ln.StartTime + headJudgeOffset)
            return;

        if (currentTime >= ln.EndTime)
        {
            hellChargeTracker.End(currentTime);
            return;
        }

        if (currentTime < ln.StartTime)
            return;

        var chargeElapsed = boundedHellChargeElapsed(currentTime, elapsed);
        if (chargeElapsed <= 0)
            return;

        var successfulTail = hasSuccessfulTail && tailJudgementTime < currentTime;
        hellChargeTracker.Update(chargeElapsed, holding || successfulTail, applyHellChargeTick, currentTime);
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
        headResult = HitResult.Meh;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        hooks.OnHellChargeHeadPoor(currentTime, ChargeTailLifetimeEnd());
        hooks.ApplySyntheticEndpoint(HitResult.Meh,
            endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh));
        applyMissedTail(currentTime, gameplayRate);
    }

    private void applyMissedTail(double time, double gameplayRate)
    {
        hooks.ApplySyntheticEndpoint(HitResult.Meh, endpoint(BmsLongNoteEndpointKind.Tail, time, gameplayRate, HitResult.Meh));
        TailJudged = true;
        tailJudgementTime = time;
        tailResult = HitResult.Meh;
    }

    private void resolvePendingRelease(double currentTime, double gameplayRate)
    {
        if (pendingReleaseTime is not { } released || TailJudged)
            return;
        if (released + releaseMargin > currentTime)
            return;

        setPendingRelease(currentTime, null);
        if (IsChargeMode)
            applyChargeTailResult(tailTable, released - ln.EndTime, currentTime, gameplayRate);
        else
            applyLongNoteReleaseResult(tailTable, released - ln.EndTime, currentTime, gameplayRate, false);
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
        var releaseResult = resultTable.ResultForOffset(tailOffset);
        var endpointResult = automatic
            ? headResult
            : (HitResult)Math.Min((int)headResult, (int)(releaseResult == HitResult.None ? HitResult.Meh : releaseResult));
        // An early normal-LN drop is BAD, even outside the tail windows.
        if (!automatic && heldOffset < 0 && endpointResult is HitResult.Meh or HitResult.Ok)
            endpointResult = HitResult.Ok;

        var tailEndpoint = endpoint(
            BmsLongNoteEndpointKind.Tail,
            automatic ? ln.EndTime + heldOffset : ln.EndTime + tailOffset,
            gameplayRate,
            endpointResult,
            eventTime);
        hooks.ApplyJudgementResult(endpointResult, [pendingHeadEndpoint!.Value, tailEndpoint]);
        TailJudged = true;
        tailJudgementTime = eventTime;
        tailResult = endpointResult;
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
            endpoint(BmsLongNoteEndpointKind.Tail, ln.EndTime + tailOffset, gameplayRate, endpointResult, eventTime));
        TailJudged = true;
        tailJudgementTime = eventTime;
        tailResult = endpointResult;
        if (mode != BmsLongNoteMode.HellChargeNote)
        {
            if (endpointResult != HitResult.Meh)
                LongNoteStarted = false;
            hooks.ClearVisualIfTailWasNotPoor(endpointResult);
        }
    }

    private BmsLongNoteEndpointResult endpoint(
        BmsLongNoteEndpointKind kind,
        double eventTime,
        double gameplayRate,
        HitResult result,
        double? applicationTime = null)
        => new(ln, kind, eventTime, gameplayRate, result, applicationTime ?? eventTime);

    private double boundedHellChargeElapsed(double currentTime, double elapsed)
    {
        var frameStart = currentTime - elapsed;
        var start = Math.Max(frameStart, ln.StartTime);
        var end = Math.Min(currentTime, ln.EndTime);
        return Math.Max(0, end - start);
    }
}
