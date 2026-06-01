using System.Collections.Generic;
using System;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModAutoplay : ModAutoplay
{
    public static Score CreateScoreWithBranchDecisions(IBeatmap beatmap, ModReplayData replayData)
    {
        var score = new Score
        {
            Replay = replayData.Replay,
            ScoreInfo =
            {
                Date = DateTimeOffset.Now,
                User = new APIUser
                {
                    Id = replayData.User.OnlineID,
                    Username = replayData.User.Username,
                    IsBot = replayData.User.IsBot,
                },
            },
        };

        if (beatmap is BmsBeatmap bmsBeatmap)
            BmsBranchReplayState.EnsureBranchReplayMod(score, bmsBeatmap.BranchDecisions);

        return score;
    }

    public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
    {
        var replay = new BmsAutoGenerator((BmsBeatmap)beatmap).Generate();

        if (beatmap is BmsBeatmap { BranchDecisions.Count: > 0 } bmsBeatmap)
            replay.Frames.Insert(0, new BmsReplayFrame(BmsReplayFrame.BRANCH_DECISION_FRAME_TIME, BmsChartParser.SerialiseBranchDecisions(bmsBeatmap.BranchDecisions)));

        return new ModReplayData(replay, new ModCreatedUser { Username = "osu!topus" });
    }

}
