using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsLeaderboardRank : OsuTestScene
{
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Aquamarine);

    [TestCase(true, true, ScoreRank.S, "AAA")]
    [TestCase(true, true, ScoreRank.A, "AA")]
    [TestCase(true, true, ScoreRank.X, "S")]
    [TestCase(false, true, ScoreRank.S, "S")]
    [TestCase(true, false, ScoreRank.S, "S")]
    public void TestLeaderboardRankPreservesUsername(bool bms, bool customise, ScoreRank rank, string expectedRank)
    {
        BeatmapLeaderboardScore row = null!;
        var username = DrawableRank.GetRankLetter(rank);

        AddStep("load leaderboard score", () =>
        {
            var bmsRuleset = new BmsRuleset().RulesetInfo;
            row = new BeatmapLeaderboardScore(new ScoreInfo
            {
                Ruleset = bms ? bmsRuleset : new RulesetInfo { ShortName = "osu", OnlineID = 0 },
                BeatmapInfo = Beatmap.Value.BeatmapInfo,
                User = new APIUser { Username = username },
                Rank = rank,
                Accuracy = 0.95,
                TotalScore = 950_000,
            });
            Child = customise ? row.WithBmsRank() : row;
        });
        AddUntilStep("leaderboard loaded", () => row.IsLoaded);
        AddAssert("grade uses ruleset lettering", () => row.ChildrenOfType<OsuSpriteText>()
            .Single(text => text.Font.Equals(OsuFont.Numeric.With(size: 14))).Text.ToString(), () => Is.EqualTo(expectedRank));
        AddAssert("username keeps original lettering", () => row.ChildrenOfType<OsuSpriteText>()
            .Any(text => text.Font.Equals(OsuFont.Style.Heading2) && text.Text.ToString() == username));
    }
}
