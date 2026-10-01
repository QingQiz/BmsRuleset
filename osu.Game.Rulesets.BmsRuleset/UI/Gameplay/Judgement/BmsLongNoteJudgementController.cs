using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsLongNoteJudgementController
{

    public bool LongNoteStarted { get; private set; }

    public bool TailJudged { get; private set; }

    public bool IsChargeMode { get; private set; }

    internal bool IsHolding => LongNoteStarted && !TailJudged;

    internal bool RequiresActiveUpdate => LongNoteStarted || IsChargeMode && HeadJudged && !TailJudged;

    public bool HeadJudged => headJudged;

    internal BmsLongNoteState State => new(headJudged, LongNoteStarted, TailJudged, headResult,
        headJudged ? ln.StartTime + headJudgeOffset : null, tailResult, tailJudgementTime, pendingReleaseTime,
        mode == BmsLongNoteMode.HellChargeNote && LongNoteStarted && lastBodyUpdateTime < ln.EndTime);

    public double NextHeadJudgementTime => headJudged ? double.PositiveInfinity : ln.StartTime + headTable.SlowWindowFor(HitResult.Ok);

    public bool ShouldShowHeldVisual(bool keyPressed)
        => LongNoteStarted
           && (keyPressed || mode == BmsLongNoteMode.HellChargeNote && hasSuccessfulTail)
           && (!TailJudged || mode == BmsLongNoteMode.HellChargeNote);

    private const double passive_poor_lifetime_margin = 100;
    private const double tail_visibility_grace = 50;
    private readonly BmsHellChargeBodyTracker hellChargeTracker = new();

    private readonly BmsLongNote ln;
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

    public bool TryRepress(IBmsLongNoteJudgementSink sink, double currentTime, bool reverseScratch = false, double gameplayRate = 1)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        if (isScratch && IsChargeMode && reverseScratch != headReverseScratch)
            applyChargeTailResult(sink, tailTable, currentTime - ln.EndTime, currentTime, gameplayRate);

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

    public BmsLongNoteJudgementController(BmsLongNote hitObject, bool preserveHellChargeHistory = true)
    {
        ln = hitObject;
        RefreshConfiguration(preserveHellChargeHistory);
    }

    internal void RefreshConfiguration(bool preserveHellChargeHistory)
    {
        // Defaults can be reapplied to an existing chart object without replacing its history.
        hellChargeTracker.HistoryEnabled = preserveHellChargeHistory;
        headTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: false);
        tailTable = BmsJudgementProfileProvider.GetTable(ln.Beatmap.LayoutVariant, ln.Column, ln.EffectiveJudgementRate, tail: true);
        refreshMode();
    }

    private void refreshMode()
    {
        mode = ln.Beatmap.LockedLongNoteMode == BmsLongNoteMode.Undefined
            ? BmsLongNoteMode.LongNote
            : ln.Beatmap.LockedLongNoteMode;
        IsChargeMode = mode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote;
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
        refreshMode();
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

    public bool TryHit(IBmsLongNoteJudgementSink sink, double currentTime, HitResult result, double gameplayRate = 1, bool reverseScratch = false)
    {
        if (headJudged || LongNoteStarted || result == HitResult.None)
            return false;

        if (mode == BmsLongNoteMode.HellChargeNote && result == HitResult.Meh)
        {
            startHellChargeBodyAfterHeadPoor(sink, currentTime, gameplayRate);
            return true;
        }

        headJudged = true;
        LongNoteStarted = result != HitResult.Meh;
        headResult = result;
        headJudgeOffset = currentTime - ln.StartTime;
        headReverseScratch = reverseScratch;
        hellChargeTracker.Reset();
        sink.OnUserHeadJudged(ln);

        var headEndpoint = endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, result);

        // CN/HCN score the head and tail separately; normal LN commits both timings with its final result.
        if (IsChargeMode)
            sink.ApplyJudgementResult(ln, result, [headEndpoint]);
        else
            pendingHeadEndpoint = headEndpoint;

        return true;
    }

    public bool TryRelease(IBmsLongNoteJudgementSink sink, double currentTime, double releaseOffset, BmsJudgementWindowTable tailTable, double gameplayRate = 1, bool reverseScratch = false)
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
            applyLongNoteReleaseResult(sink, tailTable, releaseOffset, currentTime, gameplayRate, automatic: false);
            return true;
        }

        if (mode == BmsLongNoteMode.HellChargeNote)
            hellChargeTracker.MarkReleased(currentTime);

        applyChargeTailResult(sink, tailTable, releaseOffset, currentTime, gameplayRate);
        return true;
    }

    /// <summary>
    ///     Completes the active long-note judgement at a pause boundary. The pause is a
    ///     judgement boundary, so the tail is scored at the current time and the controller
    ///     no longer needs to carry an active body through the resume rewind.
    /// </summary>
    public bool CompleteAtPause(IBmsLongNoteJudgementSink sink, double currentTime, double gameplayRate = 1)
    {
        if (!LongNoteStarted || TailJudged)
            return false;

        var releaseTime = pendingReleaseTime ?? currentTime;
        // A pause finalises any deferred release instead of leaving a pending release attached
        // to a controller that will be kept authoritative throughout the resume rewind.
        setPendingRelease(currentTime, null);

        if (!IsChargeMode)
            applyLongNoteReleaseResult(sink, tailTable, releaseTime - ln.EndTime, currentTime, gameplayRate, automatic: false);
        else
            applyChargeTailResult(sink, tailTable, releaseTime - ln.EndTime, currentTime, gameplayRate);

        // A pause-completed tail is final even for HCN. Do not continue body ticks after the
        // boundary, otherwise resume would still depend on the discarded intermediate state.
        LongNoteStarted = false;

        return true;
    }

    public void CheckPassiveResult(IBmsLongNoteJudgementSink sink, double currentTime, double gameplayRate = 1)
    {
        resolvePendingRelease(sink, currentTime, gameplayRate);
        if (TailJudged)
            return;

        if (!LongNoteStarted)
        {
            if (!headTable.IsPastPassivePoorOffset(currentTime - ln.StartTime))
                return;

            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                startHellChargeBodyAfterHeadPoor(sink, currentTime, gameplayRate);
                return;
            }

            headJudged = true;
            headResult = HitResult.Meh;
            headJudgeOffset = currentTime - ln.StartTime;

            if (IsChargeMode)
            {
                sink.ApplyJudgementResult(ln, HitResult.Meh,
                    [endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh)]);
                applyMissedTail(sink, currentTime, gameplayRate);
                return;
            }

            sink.ApplyJudgementResult(ln, HitResult.Meh,
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
                applyMissedTail(sink, currentTime, gameplayRate);

            return;
        }

        if (tailOffset >= 0)
            applyLongNoteReleaseResult(sink, headTable, tailOffset, currentTime, gameplayRate, automatic: true);
    }

    public double ChargeTailLifetimeEnd()
    {
        return ln.EndTime + headTable.SlowWindowFor(HitResult.Ok) + passive_poor_lifetime_margin;
    }

    /// <summary>
    /// Completes passive judgement and retirement after input has been processed.
    /// </summary>
    public void UpdatePostResult(IBmsLongNoteJudgementSink sink, double currentTime, double elapsed, bool holding, double gameplayRate = 1)
    {
        if (headJudged && currentTime < ln.StartTime + headJudgeOffset)
        {
            Reset();
            return;
        }

        UpdateHellChargeBody(sink, currentTime, elapsed, holding);
        resolvePendingRelease(sink, currentTime, gameplayRate);

        if (IsChargeMode && headJudged && !TailJudged)
        {
            var tailOffset = currentTime - ln.EndTime;

            if (headTable.IsPastPassivePoorOffset(tailOffset))
                applyMissedTail(sink, currentTime, gameplayRate);
        }

        if (LongNoteStarted && hasPassedVisualEnd(currentTime) && (!IsChargeMode || TailJudged))
        {
            LongNoteStarted = false;
            sink.Retire(ln);
        }
    }

    public void UpdateHellChargeBody(IBmsLongNoteJudgementSink sink, double currentTime, double elapsed, bool holding)
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
        hellChargeTracker.Update(chargeElapsed, holding || successfulTail, sink.HellChargeTick, currentTime);
    }

    private void startHellChargeBodyAfterHeadPoor(IBmsLongNoteJudgementSink sink, double currentTime, double gameplayRate)
    {
        if (headJudged)
            return;

        headJudged = true;
        LongNoteStarted = true;
        headResult = HitResult.Meh;
        headJudgeOffset = currentTime - ln.StartTime;
        hellChargeTracker.Reset();
        sink.OnHellChargeHeadPoor(ln, currentTime, ChargeTailLifetimeEnd());
        sink.ApplySyntheticEndpoint(endpoint(BmsLongNoteEndpointKind.Head, currentTime, gameplayRate, HitResult.Meh));
        applyMissedTail(sink, currentTime, gameplayRate);
    }

    private void applyMissedTail(IBmsLongNoteJudgementSink sink, double time, double gameplayRate)
    {
        sink.ApplySyntheticEndpoint(endpoint(BmsLongNoteEndpointKind.Tail, time, gameplayRate, HitResult.Meh));
        TailJudged = true;
        tailJudgementTime = time;
        tailResult = HitResult.Meh;
    }

    private void resolvePendingRelease(IBmsLongNoteJudgementSink sink, double currentTime, double gameplayRate)
    {
        if (pendingReleaseTime is not { } released || TailJudged)
            return;
        if (released + releaseMargin > currentTime)
            return;

        setPendingRelease(currentTime, null);
        if (IsChargeMode)
            applyChargeTailResult(sink, tailTable, released - ln.EndTime, currentTime, gameplayRate);
        else
            applyLongNoteReleaseResult(sink, tailTable, released - ln.EndTime, currentTime, gameplayRate, false);
    }

    private void applyLongNoteReleaseResult(
        IBmsLongNoteJudgementSink sink,
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
        sink.ApplyJudgementResult(ln, endpointResult, [pendingHeadEndpoint!.Value, tailEndpoint]);
        TailJudged = true;
        tailJudgementTime = eventTime;
        tailResult = endpointResult;
        // A non-POOR tail stops the hold immediately (the drawable fades now); a POOR tail keeps the
        // body alive until retire. This mirrors the original clearVisualIfTailWasNotPoor's state write.
        if (endpointResult != HitResult.Meh)
            LongNoteStarted = false;
        sink.ClearVisualIfTailWasNotPoor(ln, endpointResult);
    }

    private void applyChargeTailResult(IBmsLongNoteJudgementSink sink, BmsJudgementWindowTable tailTable, double tailOffset, double eventTime, double gameplayRate)
    {
        if (TailJudged)
            return;

        var result = tailTable.ResultForOffset(tailOffset);
        var endpointResult = result == HitResult.None ? HitResult.Meh : result;

        sink.ApplySyntheticEndpoint(endpoint(BmsLongNoteEndpointKind.Tail, ln.EndTime + tailOffset, gameplayRate, endpointResult, eventTime));
        TailJudged = true;
        tailJudgementTime = eventTime;
        tailResult = endpointResult;
        if (mode != BmsLongNoteMode.HellChargeNote)
        {
            if (endpointResult != HitResult.Meh)
                LongNoteStarted = false;
            sink.ClearVisualIfTailWasNotPoor(ln, endpointResult);
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
