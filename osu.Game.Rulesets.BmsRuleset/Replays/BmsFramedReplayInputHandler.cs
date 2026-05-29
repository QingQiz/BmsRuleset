using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.StateChanges;
using osu.Game.Replays;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsFramedReplayInputHandler : FramedReplayInputHandler<BmsReplayFrame>
{
    private readonly IReadOnlyList<BmsReplayFrame> frames;

    private BmsReplayFrame? currentFrame;

    public BmsFramedReplayInputHandler(Replay replay)
        : base(replay)
    {
        frames = replay.Frames.OfType<BmsReplayFrame>().OrderBy(f => f.Time).ToArray();
    }

    public override double? SetFrameFromTime(double time)
    {
        var index = findFrameAt(time);

        currentFrame = index >= 0 ? frames[index] : null;

        return time;
    }

    protected override bool IsImportant(BmsReplayFrame frame) => true;

    protected override void CollectReplayInputs(List<IInput> inputs)
    {
        inputs.Add(new ReplayState<BmsAction> { PressedActions = currentFrame?.Actions ?? [] });
    }

    private int findFrameAt(double time)
    {
        var low = 0;
        var high = frames.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (frames[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low - 1;
    }
}
