using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResultScreenStatistics
{
    [TestCase(1280, 720, false)]
    [TestCase(1024, 600, true)]
    public void TestHistoricalBestComparison(int width, int height, bool missingStatistics)
    {
        TestBmsSoloResultsScreen screen = null!;
        BmsResultComparisonButton button = null!;
        PopoverContainer popovers = null!;
        ScoreInfo current = null!;
        ScoreInfo best = null!;

        AddStep("load result and persist local scores", () =>
        {
            current = createScore();
            current.Accuracy = 1542.0 / 1970;
            current.BeatmapHash = Guid.NewGuid().ToString();
            best = current.DeepClone();
            best.ID = Guid.NewGuid();
            best.BeatmapInfo = null;
            best.Date = DateTimeOffset.Now.AddDays(-1);
            best.Statistics[HitResult.Perfect] = 610;
            best.Statistics[HitResult.Great] = 330;
            best.Statistics[HitResult.Good] = 25;
            best.Statistics[HitResult.Ok] = 10;
            best.Statistics[HitResult.Miss] = 2;
            best.MaxCombo = 690;
            best.Accuracy = 1550.0 / 1970;
            if (missingStatistics)
                best.Statistics.Clear();
            var storedBest = best.DeepClone();
            storedBest.Ruleset = new BmsRuleset().RulesetInfo;
            storedBest.StatisticsJson = JsonConvert.SerializeObject(best.Statistics);
            storedBest.MaximumStatisticsJson = JsonConvert.SerializeObject(best.MaximumStatistics);
            var storedCurrent = current.DeepClone();
            storedCurrent.BeatmapInfo = null;
            storedCurrent.Ruleset = new BmsRuleset().RulesetInfo;
            Realm.Write(r =>
            {
                r.Add(storedBest, true);
                r.Add(storedCurrent, true);
            });
            var stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.None,
                Size = new Vector2(width, height),
                Scale = new Vector2(Math.Min(Content.DrawWidth / width, Content.DrawHeight / height)),
            };
            Child = stack;
            stack.Push(screen = new TestBmsSoloResultsScreen(current));
        });
        AddUntilStep("comparison button ready", () => screen.IsLoaded && OverviewFitsViewport(screen)
                                                                      && screen.ChildrenOfType<BmsResultComparisonButton>().SingleOrDefault()?.IsLoaded == true);
        AddStep("open from judgement header", () =>
        {
            button = screen.ChildrenOfType<BmsResultComparisonButton>().Single();
            popovers = screen.ChildrenOfType<PopoverContainer>().Single();
            InputManager.MoveMouseTo(button);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("historical EXSCORE loaded without replay", () => popovers.CurrentPopover is BmsResultComparisonPopover comparison
                                                                       && comparison.ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "1550"));
        AddAssert("current excluded and EXSCORE difference shown", () => popovers.CurrentPopover.ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "-8"));
        AddAssert("judgement counts are compared or marked unknown", () => popovers.CurrentPopover.ChildrenOfType<SpriteText>()
            .Any(text => text.Text.ToString() == (missingStatistics ? "-" : "+11")));
        AddAssert("EXSCORE accuracy and combo are proportional to their achievable maximums", () =>
        {
            var rows = popovers.CurrentPopover.ChildrenOfType<BmsResultComparisonRow>().Take(3).ToArray();
            Assert.That(rows.Select(row => row.ChildrenOfType<BmsResultBar>().Single(bar => bar.Name == "Current comparison bar").Proportion),
                Is.EqualTo([1542.0 / 1970, 1542.0 / 1970, 653.0 / 985]).Within(0.000001));
            Assert.That(rows.Select(row => row.ChildrenOfType<BmsResultBar>().Single(bar => bar.Name == "Best comparison bar").Proportion),
                Is.EqualTo([1550.0 / 1970, 1550.0 / 1970, 690.0 / 985]).Within(0.000001));
            return true;
        });
        AddAssert("judgement categories and scores share the same count scale", () =>
        {
            var rows = popovers.CurrentPopover.ChildrenOfType<BmsResultComparisonRow>().ToArray();
            var perfectBars = rows[3].ChildrenOfType<BmsResultBar>().OrderByDescending(bar => bar.Name).ToArray();
            Assert.That(perfectBars.Select(bar => bar.Proportion), Is.EqualTo(missingStatistics ? [1.0] : new[] { 1.0, 610.0 / 621 }).Within(0.000001));
            var judgementRows = rows.Skip(3).ToArray();
            Assert.That(judgementRows.Select(row => row.ChildrenOfType<BmsResultBar>().Single(bar => bar.Name == "Current comparison bar").Proportion),
                Is.EqualTo(new[] { 621, 300, 41, 12, 8, 3 }.Select(count => count / 621.0)).Within(0.000001));
            if (!missingStatistics)
                Assert.That(judgementRows.Select(row => row.ChildrenOfType<BmsResultBar>().Single(bar => bar.Name == "Best comparison bar").Proportion),
                    Is.EqualTo(new[] { 610, 330, 25, 10, 8, 2 }.Select(count => count / 621.0)).Within(0.000001));
            Assert.That(rows.SelectMany(row => row.ChildrenOfType<BmsResultBar>()).Count(), Is.EqualTo(missingStatistics ? 12 : 18));
            return true;
        });
        AddUntilStep("overlapping bars have equal thickness and shorter bar in front", () => popovers.CurrentPopover.ChildrenOfType<BmsResultComparisonRow>().All(row =>
        {
            var bars = row.ChildrenOfType<BmsResultBar>().ToArray();
            if (bars.Length < 2)
                return true;

            var currentBar = bars.Single(bar => bar.Name == "Current comparison bar").ScreenSpaceDrawQuad.AABBFloat;
            var bestBar = bars.Single(bar => bar.Name == "Best comparison bar").ScreenSpaceDrawQuad.AABBFloat;
            return Math.Abs(currentBar.Centre.Y - bestBar.Centre.Y) < 0.1f && Math.Abs(currentBar.Height - bestBar.Height) < 0.1f
                                                                           && bars[0].Proportion >= bars[1].Proportion;
        }));
        AddUntilStep("popover fits small viewport", () =>
        {
            var bounds = popovers.CurrentPopover.ChildrenOfType<FillFlowContainer>()
                .Single(flow => flow.Name == "Result comparison popover content").ScreenSpaceDrawQuad.AABBFloat;
            var viewport = popovers.ScreenSpaceDrawQuad.AABBFloat;
            return bounds.Left >= viewport.Left && bounds.Right <= viewport.Right && bounds.Top >= viewport.Top && bounds.Bottom <= viewport.Bottom;
        });
        AddStep("close with icon", () =>
        {
            InputManager.MoveMouseTo(popovers.CurrentPopover.ChildrenOfType<IconButton>().Single());
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("popover closed", () => popovers.CurrentTarget == null);
        AddStep("reopen comparison", () =>
        {
            InputManager.MoveMouseTo(button);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("comparison reopened", () => popovers.CurrentTarget == button);
        AddStep("dismiss by clicking outside", () =>
        {
            InputManager.MoveMouseTo(screen.ToScreenSpace(new Vector2(screen.DrawWidth - 10, 10)));
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("outside click dismisses comparison", () => popovers.CurrentTarget == null);
        AddStep("reopen for keyboard dismissal", () =>
        {
            InputManager.MoveMouseTo(button);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("comparison ready for keyboard", () => popovers.CurrentTarget == button && popovers.CurrentPopover.IsLoaded);
        AddStep("dismiss with escape", () => InputManager.Key(Key.Escape));
        AddUntilStep("escape closes only comparison", () => popovers.CurrentTarget == null && screen.IsCurrentScreen());
        AddStep("remove comparison scores", () => Realm.Write(r =>
        {
            r.Remove(r.Find<ScoreInfo>(best.ID)!);
            r.Remove(r.Find<ScoreInfo>(current.ID)!);
        }));
    }

    [Test]
    public void TestHistoricalBestComparisonEmpty()
    {
        BmsResultComparisonButton button = null!;
        PopoverContainer popovers = null!;
        AddStep("show score without history", () =>
        {
            var score = createScore();
            score.BeatmapHash = Guid.NewGuid().ToString();
            Child = popovers = new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = button = new BmsResultComparisonButton(score) { Anchor = Anchor.Centre, Origin = Anchor.Centre },
            };
        });
        AddStep("open comparison", () =>
        {
            InputManager.MoveMouseTo(button);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("empty history message shown", () => popovers.CurrentPopover == null
                ? string.Empty
                : string.Concat(popovers.CurrentPopover.ChildrenOfType<SpriteText>().Select(text => text.Text.ToString())).Replace(" ", string.Empty),
            () => Does.Contain(Dependencies.Get<LocalisationManager>().GetLocalisedString(BmsStrings.ResultComparisonEmpty).Replace(" ", string.Empty)));
        AddAssert("empty state does not invent zero scores", () => !popovers.CurrentPopover.ChildrenOfType<GridContainer>().Any());
    }
}
