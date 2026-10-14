using System;
using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.States;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;

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
            inputs.Add(new ReplayState<BmsAction> { PressedActions = CurrentFrame?.Actions ?? [] });
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
}
