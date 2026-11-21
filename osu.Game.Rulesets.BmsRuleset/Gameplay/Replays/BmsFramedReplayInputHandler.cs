using System;
using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.States;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal readonly record struct BmsReplayPosition(double Time, BmsReplayFrame? LastFrame);

public class BmsFramedReplayInputHandler(Replay replay, Action<double>? onEnd = null) : FramedReplayInputHandler<BmsReplayFrame>(replay)
{
    internal BmsManiaReplayCadence? ManiaCadence { get; init; }

    internal bool IsJudgementFrame { get; private set; } = true;

    private bool advancing;
    private bool ended;
    private Func<double?>? getSeekTarget;
    private Func<double, BmsReplayPosition?, bool>? rebuild;
    private Action? seekInputApplied;
    private bool rebuiltInput;
    private bool seekRequested;

    internal void RequestSeek() => seekRequested = true;

    internal void ConfigureSeeking(Func<double?> getTarget, Func<double, BmsReplayPosition?, bool> rebuildState, Action inputApplied)
    {
        getSeekTarget = getTarget;
        rebuild = rebuildState;
        seekInputApplied = inputApplied;
    }

    protected override bool IsImportant(BmsReplayFrame frame) => true;

    public override double? SetFrameFromTime(double time)
    {
        IsJudgementFrame = ManiaCadence == null;
        var target = getSeekTarget?.Invoke() ?? time;
        // Explicit seeks can skip frame stability; ordinary playback must still deliver each input.
        if (seekRequested || target < CurrentTime)
        {
            if (!double.IsFinite(target))
                return null;

            // The frame identity distinguishes inputs sharing the same judgement timestamp.
            var position = double.IsFinite(CurrentTime) ? new BmsReplayPosition(CurrentTime, CurrentFrame) : (BmsReplayPosition?)null;
            if (rebuild != null && !rebuild(target, position))
                return null;

            seekRequested = false;
            // Frame stability has already shortened its proposed step. Return the original
            // seek target so the entire reconstruction is presented in one update.
            var accurate = FrameAccuratePlayback;
            FrameAccuratePlayback = false;
            try
            {
                while (base.SetFrameFromTime(target) is { } current && (current != target || NextFrame?.Time <= target))
                {
                }
            }
            finally
            {
                FrameAccuratePlayback = accurate;
            }

            advancing = false;
            ended = HasFrames && target >= Frames[^1].Time;
            rebuiltInput = true;
            return target;
        }

        advancing = time >= CurrentTime;
        if (!advancing) ended = false;
        if (!advancing || ManiaCadence is not { } cadence)
            return base.SetFrameFromTime(time);

        // Advance visuals to the host time, but split at every judgement step or input frame.
        var idleTime = cadence.NextIdleTimeAfter(CurrentTime);
        var previous = CurrentFrame;
        var next = base.SetFrameFromTime(Math.Min(time, idleTime));
        IsJudgementFrame = next.HasValue && (next == idleTime || !ReferenceEquals(previous, CurrentFrame));
        return next;
    }

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        do
        {
            inputs.Add(new BmsReplayState { PressedActions = CurrentFrame?.Actions ?? [] });
            if (ManiaCadence != null || !advancing || NextFrame?.Time != CurrentTime)
                break;

            base.SetFrameFromTime(CurrentTime);
        } while (true);

        if (rebuiltInput)
        {
            rebuiltInput = false;
            inputs.Add(new EndInput(() => seekInputApplied?.Invoke()));
        }

        if (advancing && !ended && NextFrame == null && CurrentFrame is { } terminal && terminal.Time == CurrentTime)
        {
            ended = true;
            if (onEnd != null)
                inputs.Add(new EndInput(() => onEnd(terminal.Time)));
        }
    }

    private sealed class EndInput(Action callback) : IInput
    {
        public void Apply(InputState inputState, IInputStateChangeHandler handler) => callback();
    }

    // Reimplement IInput because the framework's non-virtual Apply builds hash sets for
    // the small action lists. Keep its state and event types for the input manager.
    private sealed class BmsReplayState : ReplayState<BmsAction>, IInput
    {
        void IInput.Apply(InputState state, IInputStateChangeHandler handler)
        {
            if (state is not RulesetInputManagerInputState<BmsAction> inputState)
                throw new InvalidOperationException($"{nameof(BmsReplayState)} requires a {nameof(RulesetInputManagerInputState<BmsAction>)}");

            var previous = inputState.LastReplayState?.PressedActions;
            BmsAction[] released = [];
            BmsAction[] pressed = [];
            if (previous == null || previous.Count == 0)
                pressed = PressedActions.ToArray();
            else if (PressedActions.Count == 0)
                released = previous.ToArray();
            else if (!ReferenceEquals(previous, PressedActions))
            {
                released = difference(previous, PressedActions);
                pressed = difference(PressedActions, previous);
            }

            inputState.LastReplayState = this;
            handler.HandleInputStateChange(new ReplayStateChangeEvent<BmsAction>(state, this, released, pressed));
        }

        private static BmsAction[] difference(List<BmsAction> actions, List<BmsAction> excluded)
        {
            var count = 0;
            for (var i = 0; i < actions.Count; i++)
                if (!excluded.Contains(actions[i]) && actions.IndexOf(actions[i]) == i)
                    count++;

            if (count == 0)
                return [];

            var result = new BmsAction[count];
            var index = 0;
            for (var i = 0; i < actions.Count; i++)
                if (!excluded.Contains(actions[i]) && actions.IndexOf(actions[i]) == i)
                    result[index++] = actions[i];
            return result;
        }
    }
}
