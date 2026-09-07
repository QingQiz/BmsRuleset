using System.Linq;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Ranking;

internal static class BmsLeaderboardRankDisplay
{
    internal static BeatmapLeaderboardScore WithBmsRank(this BeatmapLeaderboardScore row)
    {
        if (row.Score.Ruleset.ShortName == Constant.SHORT_NAME)
            row.OnLoadComplete += _ => applyRank(row);

        return row;
    }

    private static void applyRank(BeatmapLeaderboardScore row)
    {
        // Restrict the lookup to the score area: usernames can also match a native rank letter.
        var scoreArea = row.ChildrenOfType<Container>().Single(container => container.Name == "Right content");
        var nativeText = DrawableRank.GetRankLetter(row.Score.Rank);
        var rankText = scoreArea.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == nativeText);
        rankText.Text = BmsRankDisplay.GetRankLetter(row.Score.Rank);
        rankText.Spacing = new Vector2(BmsRankDisplay.GetLetterSpacing(row.Score.Rank), 0);
    }
}
