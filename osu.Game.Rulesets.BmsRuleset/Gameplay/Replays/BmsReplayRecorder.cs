using System.Collections.Generic;
using System.Linq;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public partial class BmsReplayRecorder(
    Score score,
    BmsJudgementAlgorithm algorithm = BmsJudgementAlgorithm.Combo) : ReplayRecorder<BmsAction>(score)
{
    private readonly Replay replay = score.Replay;
    private BmsReplayFrame? terminal;

    public override void RecordFrame(bool important)
    {
        if (terminal != null && ReferenceEquals(replay.Frames.LastOrDefault(), terminal))
            replay.Frames.RemoveAt(replay.Frames.Count - 1);

        if (important || replay.Frames.Count == 0)
            base.RecordFrame(important);
        if (replay.Frames.LastOrDefault() is BmsReplayFrame last && last.Time != Time.Current)
        {
            // One mutable endpoint preserves early termination without accumulating idle samples.
            terminal ??= new BmsReplayFrame();
            terminal.Time = Time.Current;
            terminal.Actions.Clear();
            terminal.Actions.AddRange(last.Actions);
            replay.Frames.Add(terminal);
        }
    }

    protected override ReplayFrame? HandleFrame(Vector2 mousePosition, List<BmsAction> actions, ReplayFrame? previousFrame)
    {
        if (previousFrame is BmsReplayFrame previous && previous.Actions.Count == actions.Count && previous.Actions.All(actions.Contains))
            return null;

        return new BmsReplayFrame(Time.Current, actions.ToArray())
        {
            Algorithm = previousFrame == null ? algorithm : BmsJudgementAlgorithm.Combo,
        };
    }
}
