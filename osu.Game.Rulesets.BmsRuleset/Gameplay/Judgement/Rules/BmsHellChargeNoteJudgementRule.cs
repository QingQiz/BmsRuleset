using System;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;

internal sealed class BmsHellChargeNoteJudgementRule
{
    internal bool TryPress(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, HitResult result, in BmsPressContext press)
    {
        ref var hold = ref state.Hold;
        if (press.Frame.IsRewinding || hold.HeadJudged || hold.Started || result == HitResult.None || execution.GetMainResult(hold.Note).HasResult)
            return false;

        if (result == HitResult.Meh)
        {
            startMissedHead(state, execution, press.Frame);
            execution.PublishState(hold.Note, state.State, press.Frame);
            return true;
        }

        state.Body.Reset(Math.Max(hold.Note.StartTime, press.Frame.SimulationTime));
        hold.HeadJudged = true;
        hold.Started = true;
        hold.HeadResult = result;
        hold.HeadOffset = press.Frame.SimulationTime - hold.Note.StartTime;
        hold.HeadReverseScratch = press.ReverseScratch;
        execution.PublishState(hold.Note, state.State, press.Frame);
        execution.ApplyMainResult(hold.Note, result,
            [hold.Endpoint(BmsLongNoteEndpointKind.Head, press.Frame.SimulationTime, press.Frame.GameplayRate, result)], press.Frame);
        return true;
    }

    internal bool TryRepress(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsPressContext press)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding || press.Frame.IsRewinding)
            return false;

        var previous = state.State;
        if (hold.IsScratch && press.ReverseScratch != hold.HeadReverseScratch)
            applyTail(state, execution, press.InputTime - hold.Note.EndTime, press.InputTime, press.Frame);
        hold.SetPendingRelease(press.InputTime, null);
        if (previous != state.State)
            execution.PublishState(hold.Note, state.State, press.Frame);
        return true;
    }

    internal bool TryRelease(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsReleaseContext release)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding || release.Frame.IsRewinding)
            return false;

        var offset = release.InputTime - hold.Note.EndTime;
        var result = hold.TailWindow.ResultForOffset(offset);
        // Charge scratch tails use reversal; releasing the head direction only drops an out-of-window hold.
        if (hold.IsScratch && (release.ReverseScratch != hold.HeadReverseScratch || result != HitResult.None))
            return false;

        if (offset < 0 && result is HitResult.None or HitResult.Ok && hold.ReleaseMargin > 0)
        {
            hold.SetPendingRelease(release.Frame.SimulationTime, release.Frame.SimulationTime);
        }
        else
            applyTail(state, execution, offset, release.Frame.SimulationTime, release.Frame);

        execution.PublishState(hold.Note, state.State, release.Frame);
        return true;
    }

    internal void CheckPassive(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (frame.IsRewinding)
            return;

        ref var hold = ref state.Hold;
        var previous = state.State;
        resolveRelease(state, execution, frame);
        if (!hold.TailJudged)
        {
            if (!hold.Started)
            {
                if (hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.StartTime))
                    startMissedHead(state, execution, frame);
            }
            else if (hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.EndTime))
                missTail(state, execution, frame);
        }

        if (previous != state.State)
            execution.PublishState(hold.Note, state.State, frame);
    }

    internal void Finish(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame, double elapsed, bool holding)
    {
        if (frame.IsRewinding)
            return;

        ref var hold = ref state.Hold;
        var previous = state.State;
        if (hold.HeadJudged && frame.SimulationTime < hold.Note.StartTime + hold.HeadOffset)
            Reset(state);
        else
        {
            updateBody(state, execution, frame, holding);
            resolveRelease(state, execution, frame);
            if (hold.HeadJudged && !hold.TailJudged && hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.EndTime))
                missTail(state, execution, frame);
            if (hold.Started && hold.TailJudged && frame.SimulationTime >= hold.Note.EndTime)
                hold.Started = false;
        }

        if (previous != state.State)
            execution.PublishState(hold.Note, state.State, frame);
    }

    internal bool CompleteAtEnd(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding)
            return false;

        var released = hold.PendingRelease ?? frame.SimulationTime;
        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, released - hold.Note.EndTime, frame.SimulationTime, frame);
        hold.Started = false;
        execution.PublishState(hold.Note, state.State, frame);
        return true;
    }

    internal void Rewind(BmsHellChargeNoteRuntime state, double time)
    {
        ref var hold = ref state.Hold;
        hold.RewindReleaseHistory(time);
        state.LastBodyTime = Math.Min(state.LastBodyTime, time);
        if (!hold.HeadJudged)
            return;

        if (time < hold.Note.StartTime + hold.HeadOffset)
        {
            Reset(state);
            return;
        }

        if (hold.TailTime > time)
        {
            hold.TailJudged = false;
            hold.TailTime = null;
            hold.TailResult = HitResult.None;
        }

        hold.Started = time < hold.Note.EndTime || !hold.TailJudged;
        state.Body.Rewind(time);
    }

    internal void Reset(BmsHellChargeNoteRuntime state)
    {
        state.Hold.Reset();
        state.Body.Reset();
        state.LastBodyTime = double.NegativeInfinity;
    }

    internal void UpdateBody(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame, double elapsed, bool holding)
    {
        var previous = state.State;
        updateBody(state, execution, frame, holding);
        if (previous != state.State)
            execution.PublishState(state.Hold.Note, state.State, frame);
    }

    private static void updateBody(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame, bool holding)
    {
        if (frame.IsRewinding || frame.SimulationTime < state.LastBodyTime || state.LastBodyTime >= state.Hold.Note.EndTime)
            return;

        ref var hold = ref state.Hold;
        if (!hold.Started) return;

        var end = Math.Min(frame.SimulationTime, hold.Note.EndTime);
        var successfulTail = hold.TailJudged && hold.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good;
        if (successfulTail && hold.TailTime is { } tailTime && tailTime > state.LastBodyTime && tailTime < end)
            state.Body.Update(tailTime, holding, hold.Note, execution, frame);
        state.Body.Update(end, holding || (successfulTail && hold.TailTime <= end), hold.Note, execution, frame);
        state.LastBodyTime = frame.SimulationTime;
        if (frame.SimulationTime >= hold.Note.EndTime) state.Body.End(hold.Note.EndTime);
    }

    internal double NextBodyTime(BmsHellChargeNoteRuntime state, bool holding)
        => !state.State.HasActiveBody
            ? double.PositiveInfinity
            : Math.Min(state.Hold.Note.EndTime,
                state.Body.NextTickTime(holding || (state.Hold.TailJudged && state.Hold.TailResult is HitResult.Perfect or HitResult.Great or HitResult.Good)));

    private static void startMissedHead(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.HeadJudged)
            return;

        hold.HeadJudged = true;
        hold.Started = true;
        hold.HeadResult = HitResult.Meh;
        hold.HeadOffset = frame.SimulationTime - hold.Note.StartTime;
        state.Body.Reset(Math.Max(hold.Note.StartTime, frame.SimulationTime));
        execution.PublishState(hold.Note, state.State, frame);
        execution.ApplySyntheticEndpoint(hold.Endpoint(BmsLongNoteEndpointKind.Head, frame.SimulationTime, frame.GameplayRate, HitResult.Meh), frame);
        missTail(state, execution, frame);
    }

    private void resolveRelease(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.PendingRelease is not { } released || hold.TailJudged || released + hold.ReleaseMargin > frame.SimulationTime)
            return;

        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, released - hold.Note.EndTime, frame.SimulationTime, frame);
    }

    private static void missTail(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        execution.ApplySyntheticEndpoint(hold.Endpoint(BmsLongNoteEndpointKind.Tail, frame.SimulationTime, frame.GameplayRate, HitResult.Meh), frame);
        hold.TailJudged = true;
        hold.TailTime = frame.SimulationTime;
        hold.TailResult = HitResult.Meh;
    }

    private static void applyTail(BmsHellChargeNoteRuntime state, in BmsJudgementExecutionContext execution, double offset, double eventTime, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.TailJudged)
            return;

        var result = hold.TailWindow.ResultForOffset(offset);
        if (result == HitResult.None)
            result = HitResult.Meh;
        execution.ApplySyntheticEndpoint(hold.Endpoint(BmsLongNoteEndpointKind.Tail, hold.Note.EndTime + offset, frame.GameplayRate, result, eventTime), frame);
        hold.TailJudged = true;
        hold.TailTime = eventTime;
        hold.TailResult = result;
    }
}
