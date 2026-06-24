using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsFramedReplayInputHandler : FramedReplayInputHandler<BmsReplayFrame>
{
    public BmsFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
    }

    protected override bool IsImportant(BmsReplayFrame frame) => true;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        inputs.Add(new ReplayState<BmsAction> { PressedActions = CurrentFrame?.Actions ?? [] });
    }
}
