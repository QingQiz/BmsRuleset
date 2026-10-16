using System.Collections.Generic;
using System.Linq;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public class BmsReplayFrame : ReplayFrame
{
    // Replay.DeepClone preserves frames but drops subclass fields, so keep the archive header on the first input.
    internal int Version { get; init; } = BmsReplayArchive.CURRENT_VERSION;

    internal BmsJudgementAlgorithm Algorithm { get; init; }

    internal static int GetVersion(Replay replay)
        => replay.Frames.OfType<BmsReplayFrame>().FirstOrDefault()?.Version ?? BmsReplayArchive.CURRENT_VERSION;

    internal static BmsJudgementAlgorithm GetAlgorithm(Replay replay)
        => replay.Frames.OfType<BmsReplayFrame>().FirstOrDefault()?.Algorithm ?? BmsJudgementAlgorithm.Combo;

    // deserialization requires a setter
    // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
    public List<BmsAction> Actions { get; set; } = [];

    // deserialization requires a default ctor
    // ReSharper disable once UnusedMember.Global
    public BmsReplayFrame()
    {
    }

    public BmsReplayFrame(double time, params BmsAction[] actions)
        : base(time)
    {
        Actions.AddRange(actions);
    }

    public override bool IsEquivalentTo(ReplayFrame other) =>
        other is BmsReplayFrame bmsFrame
        && Time == bmsFrame.Time
        && Actions.SequenceEqual(bmsFrame.Actions);
}
