using System;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsLongNoteJudgementRule
{
    internal bool TryPress(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, HitResult result, in BmsPressContext press)
    {
        ref var hold = ref state.Hold;
        if (press.Frame.IsRewinding || hold.HeadJudged || hold.Started || result == HitResult.None || execution.GetMainResult(hold.Note).HasResult)
            return false;

        hold.HeadJudged = true;
        hold.Started = result != HitResult.Meh;
        hold.HeadResult = result;
        hold.HeadOffset = press.Frame.SimulationTime - hold.Note.StartTime;
        hold.HeadReverseScratch = press.ReverseScratch;
        execution.PublishState(hold.Note, state.State, press.Frame);
        state.HeadEndpoint = hold.Endpoint(BmsLongNoteEndpointKind.Head, press.Frame.SimulationTime, press.Frame.GameplayRate, result);
        return true;
    }

    internal bool TryRepress(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsPressContext press)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding || press.Frame.IsRewinding)
            return false;
        var previous = state.State;
        hold.SetPendingRelease(press.InputTime, null);
        if (previous != state.State)
            execution.PublishState(hold.Note, state.State, press.Frame);
        return true;
    }

    internal bool TryRelease(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsReleaseContext release)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding || release.Frame.IsRewinding || hold.IsScratch && release.ReverseScratch != hold.HeadReverseScratch)
            return false;
        var offset = release.InputTime - hold.Note.EndTime;
        var result = hold.TailWindow.ResultForOffset(offset);
        if (offset < 0 && result is HitResult.None or HitResult.Ok && hold.ReleaseMargin > 0)
        {
            hold.SetPendingRelease(release.Frame.SimulationTime, release.Frame.SimulationTime);
            execution.PublishState(hold.Note, state.State, release.Frame);
        }
        else
            applyTail(state, execution, hold.TailWindow, offset, release.Frame, false);
        return true;
    }

    internal void CheckPassive(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (frame.IsRewinding)
            return;
        resolveRelease(state, execution, frame);
        ref var hold = ref state.Hold;
        if (hold.TailJudged || execution.GetMainResult(hold.Note).HasResult)
            return;
        if (!hold.Started)
        {
            if (!hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.StartTime))
                return;
            hold.HeadJudged = true;
            hold.HeadResult = HitResult.Meh;
            hold.HeadOffset = frame.SimulationTime - hold.Note.StartTime;
            execution.ApplyMainResult(hold.Note, HitResult.Meh,
                [hold.Endpoint(BmsLongNoteEndpointKind.Head, frame.SimulationTime, frame.GameplayRate, HitResult.Meh)], frame);
            hold.TailJudged = true;
            hold.TailTime = frame.SimulationTime;
            hold.TailResult = HitResult.Meh;
            execution.PublishState(hold.Note, state.State, frame);
        }
        else if (frame.SimulationTime >= hold.Note.EndTime)
            applyTail(state, execution, hold.HeadWindow, frame.SimulationTime - hold.Note.EndTime, frame, true);
    }

    internal void Finish(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame, double elapsed, bool holding)
    {
        if (frame.IsRewinding)
            return;
        ref var hold = ref state.Hold;
        if (hold.HeadJudged && frame.SimulationTime < hold.Note.StartTime + hold.HeadOffset)
        {
            Reset(state);
            execution.PublishState(hold.Note, state.State, frame);
            return;
        }
        resolveRelease(state, execution, frame);
        if (hold.Started && frame.SimulationTime > hold.Note.EndTime + 50)
        {
            hold.Started = false;
            execution.PublishState(hold.Note, state.State, frame);
        }
    }

    internal bool CompleteAtPause(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding)
            return false;
        var released = hold.PendingRelease ?? frame.SimulationTime;
        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, hold.TailWindow, released - hold.Note.EndTime, frame, false);
        if (hold.Started)
        {
            hold.Started = false;
            execution.PublishState(hold.Note, state.State, frame with { IsPauseCompletion = true });
        }
        return true;
    }

    internal void Rewind(BmsLongNoteRuntime state, double time)
    {
        ref var hold = ref state.Hold;
        hold.RewindReleaseHistory(time);
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
        hold.Started = hold.HeadResult != HitResult.Meh && (!hold.TailJudged || hold.TailResult == HitResult.Meh)
                      && time <= hold.Note.EndTime + 50;
    }

    internal void Reset(BmsLongNoteRuntime state)
    {
        state.Hold.Reset();
        state.HeadEndpoint = null;
    }

    private void resolveRelease(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.PendingRelease is not { } released || hold.TailJudged || released + hold.ReleaseMargin > frame.SimulationTime)
            return;
        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, hold.TailWindow, released - hold.Note.EndTime, frame, false);
    }

    private static void applyTail(BmsLongNoteRuntime state, in BmsJudgementExecutionContext execution,
                                 BmsJudgementWindowTable window, double offset, in BmsFrameContext frame, bool automatic)
    {
        ref var hold = ref state.Hold;
        var heldOffset = automatic || Math.Abs(hold.HeadOffset) > Math.Abs(offset) ? hold.HeadOffset : offset;
        var released = window.ResultForOffset(offset);
        var result = automatic ? hold.HeadResult : (HitResult)Math.Min((int)hold.HeadResult, (int)(released == HitResult.None ? HitResult.Meh : released));
        if (!automatic && heldOffset < 0 && result is HitResult.Meh or HitResult.Ok)
            result = HitResult.Ok;
        var tail = hold.Endpoint(BmsLongNoteEndpointKind.Tail, hold.Note.EndTime + (automatic ? heldOffset : offset), frame.GameplayRate, result, frame.SimulationTime);
        // Result callbacks observe the previous tail and hold state, including the pending head timings.
        execution.ApplyMainResult(hold.Note, result, [state.HeadEndpoint!.Value, tail], frame);
        hold.TailJudged = true;
        hold.TailTime = frame.SimulationTime;
        hold.TailResult = result;
        if (result != HitResult.Meh)
            hold.Started = false;
        execution.PublishState(hold.Note, state.State, frame);
    }
}
