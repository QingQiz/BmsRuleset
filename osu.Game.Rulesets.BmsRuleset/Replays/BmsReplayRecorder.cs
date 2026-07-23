using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public partial class BmsReplayRecorder(Score score) : ReplayRecorder<BmsAction>(score)
{
    protected override ReplayFrame HandleFrame(Vector2 mousePosition, List<BmsAction> actions, ReplayFrame previousFrame) =>
        new BmsReplayFrame(Time.Current, actions.ToArray());
}
