using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public partial class BmsReplayRecorder(Score score, BmsJudgementAlgorithm algorithm = BmsJudgementAlgorithm.Combo) : ReplayRecorder<BmsAction>(score)
{
    protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<BmsAction> actions, ReplayFrame previousFrame) =>
        new BmsReplayFrame(Time.Current, actions.ToArray())
        {
            // Keep the session choice with the input frames so archive export and score cloning
            // preserve it independently of the viewer's current settings.
            JudgementAlgorithm = previousFrame == null ? algorithm : null,
        };
}
