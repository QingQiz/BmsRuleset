using System;
using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.States;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public class BmsFramedReplayInputHandler : FramedReplayInputHandler<BmsReplayFrame>
{
    private readonly Action<double>? onEnd;
    private bool advancing;
    private bool ended;

    public BmsFramedReplayInputHandler(Replay replay, Action<double>? onEnd = null)
        : base(replay)
    {
        this.onEnd = onEnd;
    }

    protected override bool IsImportant(BmsReplayFrame frame) => true;

    public override double? SetFrameFromTime(double time)
    {
        advancing = time >= CurrentTime;
        if (!advancing) ended = false;
        return base.SetFrameFromTime(time);
    }

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        do
        {
            inputs.Add(new BmsReplayState { PressedActions = CurrentFrame?.Actions ?? [] });
            if (!advancing || NextFrame?.Time != CurrentTime)
                break;

            base.SetFrameFromTime(CurrentTime);
        } while (true);

        if (advancing && !ended && !WaitingForFrame && NextFrame == null && CurrentFrame is { } terminal && terminal.Time == CurrentTime)
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
