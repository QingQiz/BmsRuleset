using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsLeaderboardAppearance : OsuManualInputManagerTestScene
{
    [Cached]
    private readonly OverlayColourProvider colours = new(OverlayColourScheme.Aquamarine);

    public TestSceneBmsLeaderboardAppearance()
    {
        AddStep("native and BMS comparison", showComparison);
        AddUntilStep("all score rows loaded", () => this.ChildrenOfType<BmsLeaderboardScore>().Count(row => row.IsLoaded) == 5);
        AddUntilStep("score content remains horizontal", () => this.ChildrenOfType<BmsLeaderboardScore>().SelectMany(row => row.ChildrenOfType<OsuSpriteText>())
            .Where(text => text.IsPresent).All(text => Math.Abs(text.ScreenSpaceDrawQuad.TopLeft.X - text.ScreenSpaceDrawQuad.BottomLeft.X) < 0.5f));
        AddUntilStep("narrow row keeps username readable", () => !this.ChildrenOfType<BmsLeaderboardScore>().Single(row => row.Parent.Width == 320)
            .ChildrenOfType<TruncatingSpriteText>().Single(text => text.Name == "Leaderboard username").IsTruncated);
        AddStep("compact rows", () => this.ChildrenOfType<BmsLeaderboardScore>().First().Parent.Width = 480);
        AddStep("restore full rows", () => this.ChildrenOfType<BmsLeaderboardScore>().First().Parent.Width = 760);
    }

    private void showComparison()
    {
        var noMods = score(ScoreRank.S, []);
        var withMods = score(ScoreRank.A, [new BmsModMirror(), new BmsModDoubleTime { SpeedChange = { Value = 1.25 } }, new BmsModHardGauge()]);
        var failed = score(ScoreRank.F, [new BmsModMirror()]);
        failed.User = new APIUser { Username = "Long username for compact leaderboard" };

        Child = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                label("osu!", 0),
                row(new BeatmapLeaderboardScore(noMods) { Rank = 1 }, 20, 760),
                label("BMS", 84),
                row(new BmsLeaderboardScore(noMods) { Rank = 1 }, 104, 760),
                row(new BmsLeaderboardScore(withMods) { Rank = 2 }, 166, 760),
                row(new BmsLeaderboardScore(failed) { Rank = 1234 }, 228, 380),
                row(new BmsLeaderboardScore(withMods) { Rank = 123456 }, 290, 320),
                row(new BmsLeaderboardScore(withMods) { Rank = 12 }, 352, 520),
                new Container
                {
                    Position = new Vector2(0, 440),
                    AutoSizeAxes = Axes.Both,
                    Child = new BeatmapLeaderboardScore.LeaderboardScoreTooltip(colours).With(tooltip =>
                    {
                        tooltip.OnLoadComplete += _ =>
                        {
                            tooltip.SetContent(noMods);
                            tooltip.Show();
                        };
                    }),
                },
                tooltip(noMods, 210),
                tooltip(withMods, 460),
            ],
        };
    }

    private Drawable tooltip(ScoreInfo scoreInfo, float x) => new BmsLeaderboardScoreTooltip(colours)
    {
        Position = new Vector2(x, 440),
    }.With(tooltip =>
    {
        tooltip.OnLoadComplete += _ =>
        {
            tooltip.SetContent(scoreInfo);
            tooltip.Show();
        };
    });

    private static Drawable row(Drawable row, float y, float width) => new Container
    {
        X = 16,
        Y = y,
        Width = width,
        Height = BmsLeaderboardScore.HEIGHT,
        Child = row,
    };

    private static Drawable label(string text, float y) => new OsuSpriteText { Text = text, Y = y, Font = OsuFont.GetFont(size: 14) };

    private ScoreInfo score(ScoreRank rank, Mod[] mods) => new()
    {
        Ruleset = new BmsRuleset().RulesetInfo,
        BeatmapInfo = Beatmap.Value.BeatmapInfo,
        User = new APIUser { Username = "Guest" },
        Date = new DateTimeOffset(2026, 7, 7, 16, 23, 45, TimeSpan.FromHours(8)),
        Rank = rank,
        Accuracy = 0.8457,
        MaxCombo = 703,
        TotalScore = 827267,
        PP = 0,
        Statistics = new Dictionary<HitResult, int>
        {
            [HitResult.Perfect] = 1800,
            [HitResult.Great] = 566,
            [HitResult.Good] = 50,
            [HitResult.Ok] = 12,
            [HitResult.Meh] = 10,
            [HitResult.Miss] = 8,
        },
        MaximumStatistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = 2463 },
        Mods = mods,
    };
}
