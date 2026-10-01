using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public class BmsFramedReplayInputHandler(Replay replay) : FramedReplayInputHandler<BmsReplayFrame>(replay)
{
    // Old AS replays omit automated keys. New recordings and live streams supply their own actions.
    internal bool IncludesGeneratedInputs { get; } = !replay.HasReceivedAllFrames
        || replay.Frames.OfType<BmsReplayFrame>().Any(f => f.IncludesGeneratedInputs);

    protected override bool IsImportant(BmsReplayFrame frame) => true;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        inputs.Add(new ReplayState<BmsAction> { PressedActions = CurrentFrame?.Actions ?? [] });
    }
}
