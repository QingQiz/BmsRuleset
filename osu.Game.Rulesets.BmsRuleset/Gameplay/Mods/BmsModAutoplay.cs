using System;
using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

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

        return new ModReplayData(replay, new ModCreatedUser { Username = "osu!topus" });
    }

}
