using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public class BmsReplayFrame : ReplayFrame
{
    public const double BRANCH_DECISION_FRAME_TIME = -1000000000;

    public List<BmsAction> Actions { get; } = [];

    public string BranchDecisions { get; set; } = string.Empty;

    public BmsReplayFrame(double time, params BmsAction[] actions)
        : base(time)
    {
        Actions.AddRange(actions);
    }

    public BmsReplayFrame(double time, string branchDecisions)
        : base(time)
    {
        BranchDecisions = branchDecisions;
    }

    public override bool IsEquivalentTo(ReplayFrame other) =>
        other is BmsReplayFrame bmsFrame
        && Math.Abs(Time - bmsFrame.Time) < 0.0001
        && BranchDecisions == bmsFrame.BranchDecisions
        && Actions.SequenceEqual(bmsFrame.Actions);
}
