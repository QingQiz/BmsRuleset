using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Sprites;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsLeaderboardRank : OsuManualInputManagerTestScene
{
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Aquamarine);

    [TestCase(ScoreRank.S, "AAA", 320)]
    [TestCase(ScoreRank.A, "AA", 480)]
    [TestCase(ScoreRank.X, "S", 720)]
    [TestCase(ScoreRank.F, "F", 320)]
    public void TestLeaderboardRankAndLayout(ScoreRank rank, string expectedRank, float width)
    {
        BmsLeaderboardScore row = null!;
        var username = new string('S', 60);

        AddStep("load BMS leaderboard score", () => Child = new Container
        {
            Width = width,
            Height = BmsLeaderboardScore.HEIGHT,
            Child = row = new BmsLeaderboardScore(createScore(rank, username), false) { Rank = 123456 },
        });
        AddUntilStep("leaderboard loaded", () => row.IsLoaded);
        AddAssert("grade uses BMS lettering", () => row.ChildrenOfType<OsuSpriteText>()
            .Single(text => text.Name == "Leaderboard grade").Text.ToString(), () => Is.EqualTo(expectedRank));
        AddAssert("username unchanged", () => row.ChildrenOfType<TruncatingSpriteText>()
            .Single(text => text.Name == "Leaderboard username").Text.ToString(), () => Is.EqualTo(username));
        AddAssert("EXSCORE uses judgements", () => row.ChildrenOfType<OsuSpriteText>()
            .Single(text => text.Name == "Leaderboard EXSCORE").Text.ToString(), () => Is.EqualTo("205"));
        AddUntilStep("username fits before mods", () => row.ChildrenOfType<TruncatingSpriteText>()
            .Single(text => text.Name == "Leaderboard username").ScreenSpaceDrawQuad.AABBFloat.Right
            <= row.ChildrenOfType<ModIcon>().Min(icon => icon.ScreenSpaceDrawQuad.AABBFloat.Left));
        AddUntilStep("timestamp fits before mods", () => row.ChildrenOfType<TruncatingSpriteText>()
            .Single(text => text.Name == "Leaderboard timestamp").ScreenSpaceDrawQuad.AABBFloat.Right
            <= row.ChildrenOfType<ModIcon>().Min(icon => icon.ScreenSpaceDrawQuad.AABBFloat.Left));
        AddUntilStep("mods are vertically centred", () => row.ChildrenOfType<ModIcon>().All(icon =>
            Math.Abs(icon.ScreenSpaceDrawQuad.Centre.Y - row.ScreenSpaceDrawQuad.Centre.Y) < 1));
        AddAssert("grade has triangle background and right spacing", () =>
        {
            var badge = row.ChildrenOfType<Container>().Single(container => container.Name == "Leaderboard grade badge");
            var grade = badge.ChildrenOfType<OsuSpriteText>().Single();
            return row.ChildrenOfType<TrianglesV2>().Any()
                   && row.ScreenSpaceDrawQuad.AABBFloat.Right - grade.ScreenSpaceDrawQuad.AABBFloat.Right >= 6;
        });
        if (width >= 420)
            AddUntilStep("avatar is rounded", () => row.ChildrenOfType<Container>().Any(container => container.Masking && container.CornerRadius > 0
                && container.ChildrenOfType<ClickableAvatar>().Any()));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestSharedTooltipAndCourseActions(bool course)
    {
        BmsLeaderboardScore row = null!;
        BmsLeaderboardScoreTooltip tooltip = null!;
        var presented = false;
        AddStep("load score", () =>
        {
            row = new BmsLeaderboardScore(createScore(ScoreRank.S, "AAA"), false)
            {
                Action = () => presented = true,
                DeleteScore = course ? () => { } : null,
                DeleteConfirmation = course ? BmsStrings.CourseHistoryDeleteConfirmation : BmsStrings.LeaderboardDeleteConfirmation,
            };
            Child = new Container
            {
                Width = 600,
                Height = BmsLeaderboardScore.HEIGHT,
                Child = row,
            };
        });
        AddUntilStep("score loaded", () => row.IsLoaded);
        AddStep("hover score", () => InputManager.MoveMouseTo(row));
        AddUntilStep("BMS tooltip appears", () => (tooltip = this.ChildrenOfType<BmsLeaderboardScoreTooltip>().SingleOrDefault())?.IsPresent == true);
        AddUntilStep("BMS judgement rows shown", () => tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.JudgementRow>().Count(), () => Is.EqualTo(6));
        AddAssert("empty POOR has separate count", () => tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.JudgementRow>()
            .Single(cell => cell.Result == HitResult.Miss).Count, () => Is.EqualTo(7));
        AddAssert("tooltip contains EXSCORE and BMS grade", () =>
        {
            var texts = tooltip.ChildrenOfType<OsuSpriteText>().Select(text => text.Text.ToString()).ToArray();
            return texts.Contains("205 / 240") && texts.Contains("AAA") && !texts.Contains("950000");
        });
        AddUntilStep("tooltip has extended mod icons", () => tooltip.ChildrenOfType<ModIcon>().Count(), () => Is.EqualTo(row.Score.Mods.Length));
        AddAssert("judgements precede other statistics in the same list", () =>
        {
            var judgements = tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.JudgementRow>().ToArray();
            var otherStatistics = tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.StatisticRow>()
                .Where(statistic => statistic is not BmsLeaderboardScoreTooltip.JudgementRow).ToArray();
            return otherStatistics.Length > 0 && otherStatistics.All(statistic => statistic.Parent == judgements[0].Parent
                && statistic.ScreenSpaceDrawQuad.AABBFloat.Top >= judgements.Max(judgement => judgement.ScreenSpaceDrawQuad.AABBFloat.Bottom));
        });
        AddAssert("extended mods have a separate panel between statistics and score", () =>
        {
            var modsPanel = tooltip.ChildrenOfType<CompositeDrawable>().Single(drawable => drawable.Name == "Tooltip mods");
            var scorePanel = tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.TotalScoreRankPanel>().Single();
            return modsPanel.Parent == scorePanel.Parent
                   && modsPanel.ChildrenOfType<ModIcon>().Count() == row.Score.Mods.Length
                   && modsPanel.ChildrenOfType<ModIcon>().All(icon => icon.ShowExtendedInformation
                       && icon.ScreenSpaceDrawQuad.AABBFloat.Top >= tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.StatisticRow>()
                           .Max(statistic => statistic.ScreenSpaceDrawQuad.AABBFloat.Bottom)
                       && icon.ScreenSpaceDrawQuad.AABBFloat.Bottom <= scorePanel.ChildrenOfType<OsuSpriteText>()
                           .Min(text => text.ScreenSpaceDrawQuad.AABBFloat.Top));
        });
        AddStep("open score", () => InputManager.Click(MouseButton.Left));
        AddAssert("score callback invoked", () => presented);
        AddAssert("course owns deletion without replay file actions", () =>
        {
            var items = ((IHasContextMenu)row).ContextMenuItems.Select(item => item.Text.ToString()).ToArray();
            return items.Contains(BmsStrings.LeaderboardDelete.ToString()) == course
                   && !items.Contains(BmsStrings.LeaderboardExport.ToString())
                   && !items.Contains(BmsStrings.LeaderboardWatchReplay.ToString());
        });
        AddStep("copy mods", () => ((IHasContextMenu)row).ContextMenuItems.First().Action.Value());
        AddAssert("system mods excluded and settings detached", () => row.SelectedMods.Value.Count == 1
            && row.SelectedMods.Value[0] is BmsModMirror && !ReferenceEquals(row.SelectedMods.Value[0], row.Score.Mods[0]));
        AddStep("attach replay file", () => row.Score.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = "test" }, "replay.osr")));
        AddAssert("file actions remain exclusive to single scores", () => ((IHasContextMenu)row).ContextMenuItems
            .Any(item => item.Text.ToString() == BmsStrings.LeaderboardExport.ToString()), () => Is.EqualTo(!course));
        AddStep("leave score", () => InputManager.MoveMouseTo(new Vector2(-100)));
        AddUntilStep("tooltip hides", () => !tooltip.IsPresent);
        AddStep("reuse tooltip for another score", () => tooltip.SetContent(new ScoreInfo
        {
            Ruleset = new BmsRuleset().RulesetInfo,
            Rank = ScoreRank.F,
        }));
        AddUntilStep("previous judgements cleared", () => tooltip.ChildrenOfType<BmsLeaderboardScoreTooltip.JudgementRow>().All(cell => cell.Count == 0));
        AddAssert("previous mod icons cleared", () => !tooltip.ChildrenOfType<ModIcon>().Any());
        AddAssert("empty mod panel is hidden", () => !tooltip.ChildrenOfType<CompositeDrawable>().Single(drawable => drawable.Name == "Tooltip mods").IsPresent);
    }

    [Test]
    public void TestNativeRankOverlayAfterResize()
    {
        BmsLeaderboardScore row = null!;
        Container parent = null!;
        AddStep("load full row", () => Child = parent = new Container
        {
            Width = 760,
            Height = BmsLeaderboardScore.HEIGHT,
            Child = row = new BmsLeaderboardScore(createScore(ScoreRank.S, "Guest"), false) { Rank = 123456 },
        });
        AddUntilStep("row loaded", () => row.IsLoaded);
        AddStep("hover row", () => InputManager.MoveMouseTo(row.ScreenSpaceDrawQuad.TopLeft + new Vector2(80, 25)));
        AddAssert("full row keeps separate rank", () => !row.ChildrenOfType<Container>().Single(container => container.Name == "Leaderboard rank overlay").IsPresent);
        AddStep("resize while hovered", () => parent.Width = 320);
        AddUntilStep("compact row overlays rank on avatar", () => row.ChildrenOfType<Container>().Single(container => container.Name == "Leaderboard rank overlay").Alpha == 1);
        AddStep("leave row", () => InputManager.MoveMouseTo(new Vector2(-100)));
        AddUntilStep("avatar overlay fades out", () => !row.ChildrenOfType<Container>().Single(container => container.Name == "Leaderboard rank overlay").IsPresent);
    }

    [Test]
    public void TestExtendedModsAndDetailedTime()
    {
        BmsLeaderboardScore row = null!;
        BmsLeaderboardScoreTooltip tooltip = null!;
        var speed = new BmsModDoubleTime { SpeedChange = { Value = 1.25 } };
        AddStep("load score with adjusted speed", () =>
        {
            var score = createScore(ScoreRank.S, "QINGQIZ");
            score.Mods = [speed, new BmsModMirror()];
            Child = new Container
            {
                Width = 720,
                Height = BmsLeaderboardScore.HEIGHT,
                Child = row = new BmsLeaderboardScore(score, false),
            };
        });
        AddUntilStep("score loaded", () => row.IsLoaded);
        AddUntilStep("row shows speed setting", () => row.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == speed.ExtendedIconInformation));
        AddAssert("mod icons are larger", () => row.ChildrenOfType<ModIcon>().All(icon => icon.ScreenSpaceDrawQuad.AABBFloat.Height > 20));
        AddStep("use 24 hour time", () => Dependencies.Get<OsuConfigManager>().SetValue(OsuSetting.Prefer24HourTime, true));
        AddUntilStep("row shows full 24 hour timestamp", () => row.ChildrenOfType<OsuSpriteText>().Single(text => text.Name == "Leaderboard timestamp")
            .Text.ToString(), () => Is.EqualTo(BmsStrings.LeaderboardDate(row.Score.Date.ToLocalTime(), true).ToString()));
        AddStep("hover score", () => InputManager.MoveMouseTo(row));
        AddUntilStep("tooltip appears", () => (tooltip = this.ChildrenOfType<BmsLeaderboardScoreTooltip>().SingleOrDefault())?.IsPresent == true);
        AddUntilStep("tooltip shows speed setting", () => tooltip.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == speed.ExtendedIconInformation));
        AddStep("use 12 hour time", () => Dependencies.Get<OsuConfigManager>().SetValue(OsuSetting.Prefer24HourTime, false));
        AddUntilStep("row updates to 12 hour timestamp", () => row.ChildrenOfType<OsuSpriteText>().Single(text => text.Name == "Leaderboard timestamp")
            .Text.ToString(), () => Is.EqualTo(BmsStrings.LeaderboardDate(row.Score.Date.ToLocalTime(), false).ToString()));
        AddUntilStep("tooltip updates detailed timestamp", () => tooltip.ChildrenOfType<OsuSpriteText>()
            .Any(text => text.Text.ToString() == BmsStrings.LeaderboardDate(row.Score.Date.ToLocalTime(), false).ToString()));
    }

    private ScoreInfo createScore(ScoreRank rank, string username) => new()
    {
        Ruleset = new BmsRuleset().RulesetInfo,
        BeatmapInfo = Beatmap.Value.BeatmapInfo,
        User = new APIUser { Username = username },
        Rank = rank,
        Accuracy = 205.0 / 240,
        TotalScore = 950_000,
        MaxCombo = 100,
        Date = new DateTimeOffset(2026, 9, 7, 16, 23, 45, TimeSpan.FromHours(8)),
        Statistics = new()
        {
            [HitResult.Perfect] = 100,
            [HitResult.Great] = 5,
            [HitResult.Good] = 4,
            [HitResult.Ok] = 3,
            [HitResult.Meh] = 8,
            [HitResult.Miss] = 7,
        },
        MaximumStatistics = new() { [HitResult.Perfect] = 120 },
        Mods = [new BmsModMirror(), new BmsModClassGauge()],
    };
}
