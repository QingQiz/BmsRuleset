using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;

internal sealed class BmsChargeNoteJudgementRule
{
    internal bool TryPress(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, HitResult result, in BmsPressContext press)
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
        execution.ApplyMainResult(hold.Note, result,
            [hold.Endpoint(BmsLongNoteEndpointKind.Head, press.Frame.SimulationTime, press.Frame.GameplayRate, result)], press.Frame);
        return true;
    }

    internal bool TryRepress(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsPressContext press)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding || press.Frame.IsRewinding)
            return false;

        if (hold.IsScratch && press.ReverseScratch != hold.HeadReverseScratch)
            applyTail(state, execution, press.InputTime - hold.Note.EndTime, press.InputTime, press.Frame);
        var previous = state.State;
        hold.SetPendingRelease(press.InputTime, null);
        if (previous != state.State)
            execution.PublishState(hold.Note, state.State, press.Frame);
        return true;
    }

    internal bool TryRelease(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsReleaseContext release)
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
            execution.PublishState(hold.Note, state.State, release.Frame);
        }
        else
            applyTail(state, execution, offset, release.Frame.SimulationTime, release.Frame);

        return true;
    }

    internal void CheckPassive(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        if (frame.IsRewinding)
            return;

        ref var hold = ref state.Hold;
        if (execution.GetMainResult(hold.Note).HasResult)
        {
            if (state.State.HasPendingTail)
                Finish(state, execution, frame, 0, false);
            return;
        }

        resolveRelease(state, execution, frame);
        if (hold.TailJudged)
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
            missTail(state, execution, frame);
        }
        else if (hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.EndTime))
            missTail(state, execution, frame);

        if (hold.TailJudged)
            execution.PublishState(hold.Note, state.State, frame);
    }

    internal void Finish(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame, double elapsed, bool holding)
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
        var missedTail = hold.HeadJudged && !hold.TailJudged && hold.HeadWindow.IsPastPassivePoorOffset(frame.SimulationTime - hold.Note.EndTime);
        if (missedTail)
            missTail(state, execution, frame);
        if (hold.Started && hold.TailJudged && frame.SimulationTime > hold.Note.EndTime + 50)
        {
            hold.Started = false;
            execution.PublishState(hold.Note, state.State, frame);
        }
        else if (missedTail)
            execution.PublishState(hold.Note, state.State, frame);
    }

    internal bool CompleteAtEnd(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (!hold.Holding)
            return false;

        var released = hold.PendingRelease ?? frame.SimulationTime;
        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, released - hold.Note.EndTime, frame.SimulationTime, frame);
        if (hold.Started)
        {
            hold.Started = false;
            execution.PublishState(hold.Note, state.State, frame);
        }

        return true;
    }

    internal void Rewind(BmsChargeNoteRuntime state, double time)
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
            hold.EarlyReleaseOutsideWindow = false;
        }

        hold.Started = hold.HeadResult != HitResult.Meh && (!hold.TailJudged || hold.TailResult == HitResult.Meh)
                                                        && time <= hold.Note.EndTime + 50;
    }

    internal void Reset(BmsChargeNoteRuntime state) => state.Hold.Reset();

    private void resolveRelease(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.PendingRelease is not { } released || hold.TailJudged || released + hold.ReleaseMargin > frame.SimulationTime)
            return;

        hold.SetPendingRelease(frame.SimulationTime, null);
        applyTail(state, execution, released - hold.Note.EndTime, frame.SimulationTime, frame);
    }

    private static void missTail(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        execution.ApplySyntheticEndpoint(hold.Endpoint(BmsLongNoteEndpointKind.Tail, frame.SimulationTime, frame.GameplayRate, HitResult.Meh), frame);
        hold.TailJudged = true;
        hold.TailTime = frame.SimulationTime;
        hold.TailResult = HitResult.Meh;
    }

    private static void applyTail(BmsChargeNoteRuntime state, in BmsJudgementExecutionContext execution, double offset, double eventTime, in BmsFrameContext frame)
    {
        ref var hold = ref state.Hold;
        if (hold.TailJudged)
            return;

        var result = hold.TailWindow.ResultForOffset(offset);
        var earlyReleaseOutsideWindow = offset < 0 && result == HitResult.None;
        if (result == HitResult.None)
            result = HitResult.Meh;
        execution.ApplySyntheticEndpoint(hold.Endpoint(BmsLongNoteEndpointKind.Tail, hold.Note.EndTime + offset, frame.GameplayRate, result, eventTime), frame);
        hold.TailJudged = true;
        hold.TailTime = eventTime;
        hold.TailResult = result;
        hold.EarlyReleaseOutsideWindow = earlyReleaseOutsideWindow;
        if (result != HitResult.Meh)
            hold.Started = false;
        execution.PublishState(hold.Note, state.State, frame);
    }
}
