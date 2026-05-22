using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsReplayFrame : ReplayFrame
{
    public List<BmsAction> Actions { get; } = [];

    public BmsReplayFrame(double time, params BmsAction[] actions)
        : base(time)
    {
        Actions.AddRange(actions);
    }

    public override bool IsEquivalentTo(ReplayFrame other) =>
        other is BmsReplayFrame bmsFrame && Math.Abs(Time - bmsFrame.Time) < 0.0001 && Actions.SequenceEqual(bmsFrame.Actions);
}
