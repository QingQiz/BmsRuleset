using System.Collections.Generic;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsFramedReplayInputHandler(Replay replay) : FramedReplayInputHandler<BmsReplayFrame>(replay)
{
    protected override bool IsImportant(BmsReplayFrame frame) => frame.Actions.Count > 0;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        inputs.Add(new ReplayState<BmsAction> { PressedActions = CurrentFrame?.Actions ?? [] });
    }
}
