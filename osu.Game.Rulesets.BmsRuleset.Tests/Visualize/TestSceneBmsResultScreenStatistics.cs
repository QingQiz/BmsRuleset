using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Ranking.Statistics.User;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsResultScreenStatistics : OsuManualInputManagerTestScene
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestNativeEntryReplacedBeforeLoading(bool gameplayRequest)
    {
        ResultsScreen original = null!;
        BmsResultsScreen screen = null!;
        ScoreInfo score = null!;
        var hitCount = 0;

        AddStep("push through native entry", () =>
        {
            score = createScore();
            hitCount = score.HitEvents.Count;
            var stack = new OsuScreenStack { RelativeSizeAxes = Axes.Both };
            Child = stack;
            original = gameplayRequest
                ? new BmsResultsScreenRequest(new BmsResultsScreen(score) { AllowWatchingReplay = false })
                : new SoloResultsScreen(score) { AllowWatchingReplay = false };
            stack.Push(original);
            screen = (BmsResultsScreen)stack.CurrentScreen;
        });
        AddUntilStep("owned result has loaded charts", () => screen.IsLoaded && screen.ChildrenOfType<BmsHitOffsetStatistic>().Any());
        AddAssert("native screen never loaded", () => !original.IsLoaded);
        AddAssert("native score list never created", () => screen.ChildrenOfType<ScorePanelList>(), () => Is.Empty);
        AddAssert("score and hit events survive replacement", () => ReferenceEquals(screen.Score, score) && score.HitEvents.Count == hitCount);
        AddStep("exit result", () => screen.Exit());
        AddUntilStep("hit events released on exit", () => score.HitEvents, () => Is.Empty);
    }

    [TestCase(1280, 720)]
    [TestCase(1024, 600)]
    [TestCase(1600, 900)]
    public void TestStatisticsFitViewportAndStayOpen(int width, int height)
    {
        TestBmsSoloResultsScreen screen = null!;
        Dictionary<MarqueeContainer, float> metadataPositions = null!;
        AddStep("load sized results screen", () =>
        {
            var stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.None,
                Size = new Vector2(width, height),
                Scale = new Vector2(Math.Min(Content.DrawWidth / width, Content.DrawHeight / height)),
            };
            Child = stack;
            stack.Push(screen = new TestBmsSoloResultsScreen(createScore()));
        });
        AddUntilStep("all charts loaded automatically", () => screen.IsLoaded
                                                              && screen.ChildrenOfType<BmsHitOffsetStatistic>().Any()
                                                              && screen.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);
        AddUntilStep("all charts fit viewport", () => ChartsFitViewport(screen));
        AddUntilStep("result overview fills left column", () => OverviewFitsViewport(screen));
        AddAssert("native score list absent", () => screen.ChildrenOfType<ScorePanelList>(), () => Is.Empty);
        AddAssert("solo header uses the local difficulty icon", () => screen.ChildrenOfType<BmsResultModDisplay>().Single()
            .ChildrenOfType<BmsResultDifficultyIcon>().Single(icon => icon.ShowTooltip).ChildrenOfType<BmsRulesetIcon>().Any());
        AddAssert("rank text fits inside rating circle", () =>
        {
            var circle = screen.ChildrenOfType<BmsResultOverview>().Single().ChildrenOfType<BmsAccuracyCircle>().Single();
            var text = circle.ChildrenOfType<GlowingSpriteText>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var bounds = circle.ScreenSpaceDrawQuad.AABBFloat;
            return text.Width < bounds.Width * 0.8f && text.Height < bounds.Height * 0.8f;
        });
        AddAssert("rank composition fills the narrowed column", () =>
        {
            var overview = screen.ChildrenOfType<BmsResultOverview>().Single();
            var circle = overview.ChildrenOfType<BmsAccuracyCircle>().Single().ScreenSpaceDrawQuad.AABBFloat;
            return circle.Width >= overview.ScreenSpaceDrawQuad.AABBFloat.Width * 0.6f;
        });
        AddStep("record metadata positions", () => metadataPositions = screen.ChildrenOfType<BmsResultOverview>().Single()
            .ChildrenOfType<MarqueeContainer>().ToDictionary(row => row, row => row.ChildrenOfType<FillFlowContainer>().Single().X));
        AddAssert("banner contains title artist and difficulty only", () => metadataPositions.Keys
            .Select(row => row.ChildrenOfType<SpriteText>().First().Text.ToString()),
            () => Is.EqualTo(new[] { "BMS Result Showcase", "visual-test artist", "[7K Hyper]" }));
        AddUntilStep("short metadata centres and long metadata scrolls", () => metadataPositions.All(pair =>
        {
            var row = pair.Key;
            var text = row.ChildrenOfType<SpriteText>().First();
            if (text.DrawWidth > row.DrawWidth)
                return !text.Truncate && Math.Abs(row.ChildrenOfType<FillFlowContainer>().Single().X - pair.Value) > 1;

            return Math.Abs(text.ScreenSpaceDrawQuad.AABBFloat.Centre.X - row.ScreenSpaceDrawQuad.AABBFloat.Centre.X) < 1;
        }));
        AddAssert("overview text stays within its cells", () => screen.ChildrenOfType<CompositeDrawable>()
            .Where(cell => cell is BmsResultFittedText or BmsResultTextGroup).All(cell =>
            {
                var bounds = cell.ScreenSpaceDrawQuad.AABBFloat;
                return cell.ChildrenOfType<SpriteText>().All(text =>
                {
                    var textBounds = text.ScreenSpaceDrawQuad.AABBFloat;
                    return textBounds.Left >= bounds.Left - 1 && textBounds.Right <= bounds.Right + 1
                                                              && textBounds.Top >= bounds.Top - 1 && textBounds.Bottom <= bounds.Bottom + 1;
                });
            }));
        AddAssert("judgement counts have a gap after the bars", () => screen.ChildrenOfType<BmsResultJudgementRow>().All(row =>
        {
            var bar = row.ChildrenOfType<BmsResultBar>().Single();
            var count = row.ChildrenOfType<SpriteText>().Last();
            var gap = count.ScreenSpaceDrawQuad.AABBFloat.Left - bar.ScreenSpaceDrawQuad.AABBFloat.Right;
            return gap >= 12 * row.ScreenSpaceDrawQuad.AABBFloat.Width / row.DrawWidth;
        }));
        AddAssert("judgement bars stay inside their rows", () => screen.ChildrenOfType<BmsResultJudgementRow>().All(row =>
        {
            var bar = row.ChildrenOfType<BmsResultBar>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var bounds = row.ScreenSpaceDrawQuad.AABBFloat;
            return bar.Top > bounds.Top && bar.Bottom < bounds.Bottom;
        }));
        AddAssert("fast slow sits between combo and judgements", () =>
        {
            var balance = screen.ChildrenOfType<BmsResultFastSlow>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var combo = screen.ChildrenOfType<GridContainer>().Single(container => container.Name == "Result combo").ScreenSpaceDrawQuad.AABBFloat;
            var judgements = screen.ChildrenOfType<BmsResultJudgementRow>().First().ScreenSpaceDrawQuad.AABBFloat;
            return balance.Top > combo.Bottom && balance.Bottom < judgements.Top;
        });
        AddAssert("fast slow marker stays at the bar midpoint", () =>
        {
            var balance = screen.ChildrenOfType<BmsResultFastSlow>().Single();
            var bar = balance.ChildrenOfType<CircularContainer>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var marker = balance.ChildrenOfType<Box>().Single(box => box.Name == "Fast slow midpoint").ScreenSpaceDrawQuad.AABBFloat;
            return Math.Abs(marker.Centre.X - bar.Centre.X) < 1 && marker.Top < bar.Top && marker.Bottom > bar.Bottom;
        });
        AddAssert("fast slow numbers align with the bar centre", () =>
        {
            var balance = screen.ChildrenOfType<BmsResultFastSlow>().Single();
            var bar = balance.ChildrenOfType<CircularContainer>().Single().ScreenSpaceDrawQuad.AABBFloat;
            return balance.ChildrenOfType<BmsResultFittedText>().Where(text => text.Name is "Fast count" or "Slow count")
                .All(text => Math.Abs(text.ChildrenOfType<SpriteText>().Single().ScreenSpaceDrawQuad.AABBFloat.Centre.Y - bar.Centre.Y) < 1);
        });
        AddAssert("gauge and timeline share a row", () =>
        {
            var gauge = screen.ChildrenOfType<BmsGaugeHistoryGraph>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var timeline = screen.ChildrenOfType<BmsTimelineStatistic>().Single().ScreenSpaceDrawQuad.AABBFloat;
            return Math.Abs(gauge.Top - timeline.Top) < 1 && gauge.Right < timeline.Left;
        });
        AddAssert("scatter and offset each occupy a full row", () => ChartsUseExpectedRows(screen));
        AddAssert("native tags absent", () => screen.ChildrenOfType<UserTagControl>(), () => Is.Empty);
        AddAssert("chart labels fit their cells", () => screen.ChildrenOfType<BmsResultStatisticsGrid>().Single()
            .ChildrenOfType<SpriteText>().Where(text =>
            {
                var cell = text.Parent;
                while (cell != null && cell is not IBmsResultStatistic)
                    cell = cell.Parent;
                if (cell == null)
                    return false;

                var textBounds = text.ScreenSpaceDrawQuad.AABBFloat;
                var cellBounds = cell.ScreenSpaceDrawQuad.AABBFloat;
                return textBounds.Left < cellBounds.Left - 1 || textBounds.Right > cellBounds.Right + 1;
            }).Select(text => text.Text.ToString()).ToArray(), () => Is.Empty);
        AddAssert("offset tick labels do not overlap", () =>
        {
            var labels = screen.ChildrenOfType<Container>().Single(container => container.Name == "Hit offset axis")
                .ChildrenOfType<SpriteText>().Where(text => text.IsPresent)
                .Select(text => text.ScreenSpaceDrawQuad.AABBFloat).OrderBy(bounds => bounds.Left).ToArray();
            return labels.Zip(labels.Skip(1)).All(pair => pair.First.Right < pair.Second.Left);
        });
        AddStep("click result overview", () =>
        {
            InputManager.MoveMouseTo(screen.ChildrenOfType<BmsResultOverview>().Single());
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("score click keeps statistics open", () => screen.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);
        AddStep("press enter", () => InputManager.Key(Key.Enter));
        AddAssert("enter keeps statistics open", () => screen.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);
        AddAssert("back does not consume a statistics collapse", () => screen.OnBackButton(), () => Is.False);
        AddStep("expand per-key offset charts", () =>
        {
            var offset = screen.ChildrenOfType<BmsHitOffsetStatistic>().Single();
            InputManager.MoveMouseTo(offset);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("per-key rows expanded", () => screen.ChildrenOfType<BmsHitOffsetStatistic>().Single().ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "Scratch"));
        AddUntilStep("expanded charts can scroll", () => screen.ChildrenOfType<BmsResultStatisticsGrid>().Single()
            .ChildrenOfType<OsuScrollContainer>().Single().ScrollableExtent > 0);
    }

    internal static bool ChartsFitViewport(Drawable screen)
    {
        var grid = screen.ChildrenOfType<BmsResultStatisticsGrid>().SingleOrDefault(drawable => drawable.IsPresent && drawable.FindClosestParent<BmsStatisticsPanel>()!.State.Value == Visibility.Visible);
        if (grid == null)
            return false;

        var bounds = grid.ScreenSpaceDrawQuad.AABBFloat;
        var charts = grid.ChildrenOfType<Drawable>().Where(drawable => drawable is BmsGaugeHistoryGraph or BmsTimelineStatistic or BmsHitScatterStatistic or BmsHitOffsetStatistic).ToArray();
        return charts.Length == 4 && charts.All(chart =>
        {
            var chartBounds = chart.ScreenSpaceDrawQuad.AABBFloat;
            return chartBounds.Top >= bounds.Top && chartBounds.Bottom <= bounds.Bottom + 1
                                                 && chartBounds.Left >= bounds.Left && chartBounds.Right <= bounds.Right + 1
                                                 && chartBounds.Height > 80;
        }) && grid.ChildrenOfType<OsuScrollContainer>().Single().ScrollableExtent < 1;
    }

    internal static bool OverviewFitsViewport(BmsResultsScreen screen)
    {
        var overview = screen.ChildrenOfType<BmsResultOverview>().SingleOrDefault();
        var grid = screen.ChildrenOfType<BmsResultStatisticsGrid>().SingleOrDefault();
        if (overview == null || grid == null || overview.DrawColourInfo.Colour.TopLeft.Alpha < 0.99f)
            return false;

        var bounds = overview.ScreenSpaceDrawQuad.AABBFloat;
        var screenBounds = screen.ScreenSpaceDrawQuad.AABBFloat;
        var chartBounds = grid.ScreenSpaceDrawQuad.AABBFloat;
        return bounds.Left >= screenBounds.Left && bounds.Right <= chartBounds.Left
                                                && chartBounds.Left - bounds.Right < 10
                                                && Math.Abs(bounds.Top - chartBounds.Top) < 1 && Math.Abs(bounds.Bottom - chartBounds.Bottom) < 1
                                                && bounds.Top >= screenBounds.Top && bounds.Bottom <= screenBounds.Bottom;
    }

    internal static bool ChartsUseExpectedRows(Drawable screen)
    {
        var grid = screen.ChildrenOfType<BmsResultStatisticsGrid>().Single(drawable => drawable.IsPresent
                                                                                       && drawable.FindClosestParent<BmsStatisticsPanel>()!.State.Value == Visibility.Visible);
        var gauge = grid.ChildrenOfType<BmsGaugeHistoryGraph>().Single().ScreenSpaceDrawQuad.AABBFloat;
        var timeline = grid.ChildrenOfType<BmsTimelineStatistic>().Single().ScreenSpaceDrawQuad.AABBFloat;
        var scatter = grid.ChildrenOfType<BmsHitScatterStatistic>().Single().ScreenSpaceDrawQuad.AABBFloat;
        var offset = grid.ChildrenOfType<BmsHitOffsetStatistic>().Single().ScreenSpaceDrawQuad.AABBFloat;

        return Math.Abs(gauge.Top - timeline.Top) < 1 && gauge.Right < timeline.Left
                                                      && scatter.Top > Math.Max(gauge.Bottom, timeline.Bottom) && offset.Top > scatter.Bottom
                                                      && Math.Abs(scatter.Left - gauge.Left) < 1 && Math.Abs(scatter.Right - timeline.Right) < 1
                                                      && Math.Abs(offset.Left - scatter.Left) < 1 && Math.Abs(offset.Right - scatter.Right) < 1;
    }

    [Test]
    public void TestFastSlowMatchesTimelineTimingEvents()
    {
        BmsResultFastSlow balance = null!;
        var hitEvents = new List<HitEvent>
        {
            new(-15, 1, HitResult.Perfect, new BmsNote(), null, null),
            new(-70, 1, HitResult.Good, new BmsNote(), null, null),
            new(35, 1, HitResult.Great, new BmsNote(), null, null),
            new(0, 1, HitResult.Perfect, new BmsNote(), null, null),
            new(-200, 1, HitResult.Miss, new BmsNote(), null, null),
            new(200, 1, HitResult.Miss, new BmsNote(), null, null),
            new(12, 1, HitResult.LargeBonus, new BmsNote(), null, null),
            new(-5, 1, HitResult.Perfect, new BmsLandmine(), null, null),
            new(12, 1, HitResult.Great, new HitObject(), null, null),
        };
        AddStep("load timing balance", () => Child = new Container
        {
            Size = new Vector2(320, 40),
            Child = balance = new BmsResultFastSlow(hitEvents),
        });
        AddUntilStep("timing balance loaded", () => balance.IsLoaded);
        AddAssert("only directional BMS hits are counted", () => (balance.FastCount, balance.SlowCount), () => Is.EqualTo((2, 1)));
        AddAssert("ratio uses fast and slow hits", () => balance.FastProportion, () => Is.EqualTo(2.0 / 3));
        AddAssert("counts match timeline categories", () =>
        {
            var timeline = BmsTimelineStatistic.CreateData(new ScoreInfo { HitEvents = hitEvents }, new BmsBeatmap()).FastSlow;
            return timeline.Categories.Single(category => category.Label == "fast").Buckets.Sum() == balance.FastCount
                && timeline.Categories.Single(category => category.Label == "slow").Buckets.Sum() == balance.SlowCount;
        });
    }

    [TestCase(0, 0)]
    [TestCase(7, 0)]
    [TestCase(0, 7)]
    public void TestFastSlowEmptyAndOneSidedScores(int fast, int slow)
    {
        BmsResultFastSlow balance = null!;
        AddStep("load timing balance", () => Child = new Container
        {
            Size = new Vector2(320, 40),
            Child = balance = new BmsResultFastSlow(Enumerable.Repeat(-10, fast).Concat(Enumerable.Repeat(10, slow))
                .Select(offset => new HitEvent(offset, 1, HitResult.Perfect, new BmsNote(), null, null)).ToArray()),
        });
        AddUntilStep("timing balance loaded", () => balance.IsLoaded);
        AddAssert("counts remain exact", () => (balance.FastCount, balance.SlowCount), () => Is.EqualTo((fast, slow)));
        AddAssert("ratio remains valid", () => balance.FastProportion, () => Is.EqualTo(fast > 0 ? 1 : 0));
        AddAssert("empty scores have no coloured fill", () => fast + slow > 0 || balance.ChildrenOfType<CircularContainer>().Single()
            .ChildrenOfType<Box>().Where(box => box.Alpha == 1).All(box => box.DrawWidth == 0));
    }

    [Test]
    public void TestSmallResultBarFillKeepsTrackCurvature()
    {
        BmsResultBar bar = null!;
        AddStep("load a small nonzero bar", () => Child = bar = new BmsResultBar(0.01, osuTK.Graphics.Color4.Cyan)
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(200, 16),
        });
        AddUntilStep("small fill retains a full round cap", () => bar.IsLoaded
            && bar.ChildrenOfType<CircularContainer>().Single(container => container.Name == "Bar fill shape").DrawWidth == 16);
        AddAssert("only the proportional part of the cap is revealed", () =>
            bar.ChildrenOfType<Container>().Single(container => container.Name == "Bar fill reveal").DrawWidth,
            () => Is.EqualTo(2));
        AddStep("resize the bar", () => bar.Size = new Vector2(400, 24));
        AddUntilStep("cap follows the new track height", () =>
            bar.ChildrenOfType<CircularContainer>().Single(container => container.Name == "Bar fill shape").DrawWidth == 24);
        AddAssert("reveal follows the new proportional width", () =>
            bar.ChildrenOfType<Container>().Single(container => container.Name == "Bar fill reveal").DrawWidth,
            () => Is.EqualTo(4));
    }

    [Test]
    public void TestBmsResultStatisticsUseExScoreInsteadOfPerformance()
    {
        TestBmsSoloResultsScreen screen = null!;

        AddStep("load results screen", () =>
        {
            OsuScreenStack stack;
            Child = stack = new OsuScreenStack { RelativeSizeAxes = Axes.Both };
            stack.Push(screen = new TestBmsSoloResultsScreen(createScore()));
        });

        AddUntilStep("results screen loaded", () => screen.IsLoaded);
        AddUntilStep("statistics shown automatically", () => this.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);
        AddAssert("performance statistic is absent", () => this.ChildrenOfType<PerformanceStatistic>().Count() == 0);
        AddUntilStep("new overview loaded", () => screen.ChildrenOfType<BmsResultOverview>().SingleOrDefault()?.IsLoaded == true);
        AddAssert("EXSCORE uses actual judgement values", () => screen.ChildrenOfType<BmsResultFittedText>()
            .Single(text => text.Name == "EXSCORE value").ChildrenOfType<SpriteText>().Single().Text.ToString(), () => Is.EqualTo("1542"));
        AddAssert("six judgement bars use score counts", () => screen.ChildrenOfType<BmsResultJudgementRow>().Select(row => row.Count),
            () => Is.EqualTo(new[] { 621, 300, 41, 12, 8, 3 }));
        AddAssert("judgement bar uses count proportion", () => screen.ChildrenOfType<BmsResultJudgementRow>()
                .Single(row => row.Result == HitResult.Perfect).ChildrenOfType<BmsResultBar>().Single().Proportion,
            () => Is.EqualTo(621.0 / 985).Within(0.0001));
        AddAssert("combo bar uses maximum achievable combo", () => screen.ChildrenOfType<BmsResultBar>()
                .Single(bar => bar.Name == "Result combo progress").Proportion,
            () => Is.EqualTo(653.0 / 985).Within(0.0001));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestOverviewWithoutReplay(bool empty)
    {
        TestBmsSoloResultsScreen screen = null!;
        AddStep("load result without replay events", () =>
        {
            var score = createScore();
            score.HitEvents.Clear();
            if (empty)
            {
                score.Statistics.Clear();
                score.MaximumStatistics.Clear();
                score.Accuracy = 0;
            }

            var stack = new OsuScreenStack { RelativeSizeAxes = Axes.Both };
            Child = stack;
            stack.Push(screen = new TestBmsSoloResultsScreen(score));
        });
        AddUntilStep("summary visible without replay", () => screen.ChildrenOfType<BmsResultJudgementRow>().Count() == 6);
        AddAssert("all progress bars remain valid", () => screen.ChildrenOfType<BmsResultBar>().All(bar => double.IsFinite(bar.Proportion)
                                                                                                           && bar.Proportion >= 0 && bar.Proportion <= 1));
        AddAssert("empty score bars stay empty", () => !empty || screen.ChildrenOfType<BmsResultBar>().All(bar => bar.Proportion == 0));
    }

    private ScoreInfo createScore()
    {
        var beatmap = Beatmap.Value.BeatmapInfo;
        var ruleset = new BmsRuleset().RulesetInfo;

        beatmap.Ruleset = ruleset;
        beatmap.DifficultyName = "[7K Hyper]";
        beatmap.StarRating = 7.42;
        beatmap.Hash = "bms-result-showcase";
        var metadata = new BeatmapMetadata(new RealmUser { Username = "mapper" })
        {
            Title = "BMS Result Showcase",
            TitleUnicode = "BMS Result Showcase",
            Artist = "visual-test artist",
            ArtistUnicode = "visual-test artist",
        };

        beatmap.Metadata = metadata;

        beatmap.BeatmapSet = null;

        var score = new ScoreInfo
        {
            User = new APIUser
            {
                Id = 2,
                Username = "bms-test",
            },
            BeatmapInfo = beatmap,
            BeatmapHash = beatmap.Hash,
            Ruleset = ruleset,
            Rank = ScoreRank.A,
            TotalScore = 813197,
            TotalScoreWithoutMods = 813197,
            Accuracy = 0.813197,
            MaxCombo = 653,
            Mods =
            [
                new BmsModAutoGauge(),
                new BmsModHardGauge(),
            ],
            HitEvents = createGaugeHitEvents(),
            Statistics = new Dictionary<HitResult, int>
            {
                [HitResult.Perfect] = 621,
                [HitResult.Great] = 300,
                [HitResult.Good] = 41,
                [HitResult.Ok] = 12,
                [HitResult.Meh] = 8,
                [HitResult.Miss] = 3,
            },
            MaximumStatistics =
            {
                [HitResult.Perfect] = 985,
            },
        };

        return score;
    }

    private List<HitEvent> createGaugeHitEvents()
    {
        // 1000 hit events with normally-distributed offsets (mean 0, std 50ms) — a natural bell
        // that fills the hit-offset histogram. Result severity tracks offset magnitude (Perfect
        // near the centre, Meh in the tails); Gauge History is unaffected because it ignores
        // TimeOffset. The RNG is seeded so the visual is reproducible across runs.
        var rng = new Random(42991);
        var offsets = new double[1000];

        for (var i = 0; i < 1000; i++)
            offsets[i] = nextGaussian(rng, 0, 50);

        offsets[0] = 200;

        return offsets.Select((offset, i) => new HitEvent(offset, 1, resultFor(offset), new BmsNote
        {
            Column = i % 2,
            StartTime = i * 100,
        }, null, null)).ToList();

        static double nextGaussian(Random rng, double mean, double std)
        {
            double u1;
            do u1 = rng.NextDouble();
            while (u1 == 0);

            var u2 = rng.NextDouble();
            return mean + std * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        static HitResult resultFor(double offset)
        {
            var magnitude = Math.Abs(offset);

            if (magnitude < 15) return HitResult.Perfect;
            if (magnitude < 35) return HitResult.Great;
            if (magnitude < 70) return HitResult.Good;
            if (magnitude > 150) return HitResult.Miss;
            if (magnitude < 115) return HitResult.Ok;

            return HitResult.Meh;
        }
    }

    private void assertText(string text) =>
        AddUntilStep($"{text} shown", () => this.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text));

    private void assertHitOffsetText(string text) =>
        AddUntilStep($"hit offset {text} shown", () => hitOffsetStatisticContainsText(text));

    private void assertNoHitOffsetText(string text) =>
        AddUntilStep($"hit offset {text} hidden", () => !hitOffsetStatisticContainsText(text));

    private bool hitOffsetStatisticContainsText(string text) =>
        this.ChildrenOfType<BmsHitOffsetStatistic>().Single().ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text);

    private void assertHitScatterText(string text) =>
        AddUntilStep($"hit scatter {text} shown", () => hitScatterStatisticContainsText(text));

    private void assertNoHitScatterText(string text) =>
        AddUntilStep($"hit scatter {text} hidden", () => !hitScatterStatisticContainsText(text));

    private bool hitScatterStatisticContainsText(string text) =>
        this.ChildrenOfType<BmsHitScatterStatistic>().Single().ChildrenOfType<SpriteText>().Any(t => t.Text.ToString() == text);

    private static Box plotBackgroundFor(Drawable statistic) =>
        statistic.ChildrenOfType<Box>()
            .Where(b => Math.Abs(b.Alpha - 0.18f) < 0.001f && b.DrawWidth > 100 && b.DrawHeight > 80)
            .OrderByDescending(b => b.DrawWidth * b.DrawHeight)
            .First();

    private static Box gaugePlotBackgroundFor(Drawable statistic) =>
        statistic.ChildrenOfType<Box>()
            .Where(b => Math.Abs(b.Alpha - 0.22f) < 0.001f && b.DrawWidth > 100 && b.DrawHeight > 80)
            .OrderByDescending(b => b.DrawWidth * b.DrawHeight)
            .First();

    private static Container gaugePlotFor(Drawable statistic) =>
        (Container)gaugePlotBackgroundFor(statistic).Parent!;

    private static BmsGaugeHistoryGraph.GaugePath gaugePathFor(Drawable statistic) =>
        statistic.ChildrenOfType<BmsGaugeHistoryGraph.GaugePath>().Single(p => p.Name?.Contains("gauge history") == true);

    private static Container failureMarkerFor(Drawable statistic) =>
        statistic.ChildrenOfType<Container>()
            .Single(c => Math.Abs(c.DrawWidth - BmsGaugeHistoryGraph.FAILURE_MARKER_SIZE) < 0.5f
                         && Math.Abs(c.DrawHeight - BmsGaugeHistoryGraph.FAILURE_MARKER_SIZE) < 0.5f
                         && c.ChildrenOfType<Box>().Count(b => Math.Abs(Math.Abs(b.Rotation) - 45) < 0.001f) == 4);

    private static bool gaugePathExtendsBelowBottomEdge(Drawable statistic)
    {
        var plot = gaugePlotBackgroundFor(statistic).ScreenSpaceDrawQuad.AABBFloat;
        var path = gaugePathFor(statistic);
        var pathBounds = path.ScreenSpaceDrawQuad.AABBFloat;

        return pathBounds.Bottom >= plot.Bottom + path.PathRadius - 0.5f;
    }

    private static bool gaugePathEndsAtFailurePoint(BmsGaugeHistoryGraph graph, ScoreInfo score, BmsBeatmap beatmap)
    {
        var failurePoint = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single().FailurePoint;
        if (failurePoint == null)
            return false;

        var path = gaugePathFor(graph);
        var pathEnd = path.ToScreenSpace(path.Vertices[^1]);
        var plot = gaugePlotBackgroundFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var expectedX = plot.Left + failurePoint.Value.Time * plot.Width;
        var expectedY = plot.Top + (1 - failurePoint.Value.Health) * plot.Height;

        return Math.Abs(pathEnd.X - expectedX) < 0.5f
               && Math.Abs(pathEnd.Y - expectedY) < 0.5f;
    }

    private static bool failureMarkerCentreAlignsWithFailurePoint(BmsGaugeHistoryGraph graph, ScoreInfo score, BmsBeatmap beatmap)
    {
        var failurePoint = BmsGaugeHistoryGraph.CreateSeries(score, beatmap).Single().FailurePoint;
        if (failurePoint == null)
            return false;

        var plot = gaugePlotBackgroundFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var marker = failureMarkerFor(graph).ScreenSpaceDrawQuad.AABBFloat;
        var markerCentreX = (marker.Left + marker.Right) / 2;
        var markerCentreY = (marker.Top + marker.Bottom) / 2;
        var expectedX = plot.Left + failurePoint.Value.Time * plot.Width;
        var expectedY = plot.Top + (1 - failurePoint.Value.Health) * plot.Height;

        return Math.Abs(markerCentreX - expectedX) < 0.5f
               && Math.Abs(markerCentreY - expectedY) < 0.5f;
    }

    private partial class TestBmsSoloResultsScreen : BmsResultsScreen
    {
        public TestBmsSoloResultsScreen(ScoreInfo score)
            : base(score)
        {
            AllowWatchingReplay = false;
        }
    }

    [Test]
    public void TestAutoGaugeHistory()
    {
        TestBmsSoloResultsScreen screen = null!;

        AddStep("load results screen", () =>
        {
            var score = createScore();

            OsuScreenStack stack;

            Child = stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.Both,
            };

            stack.Push(screen = new TestBmsSoloResultsScreen(score));
        });

        AddUntilStep("results screen loaded", () => screen.IsLoaded);

        AddUntilStep("statistics shown automatically", () => this.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);

        assertText("BMS Result Showcase");
        assertText("visual-test artist");
        assertText("[7K Hyper]");
        AddAssert("overview omits mapper", () => screen.ChildrenOfType<BmsResultOverview>().Single()
            .ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == "mapper"), () => Is.False);
        assertText("Gauge History");
        assertText("Hazard");
        assertText("ExHard");
        assertText("Hard");
        assertText("Normal");
        assertText("Easy");
        assertText("Assist Easy");
        assertText("Timeline");
        assertText("Hit Offset");
        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");
        assertHitOffsetText("-150");
        assertHitOffsetText("0");
        assertHitOffsetText("+150");
        assertText("Hit Scatter");
        assertText("E-POOR");
        assertHitScatterText("+150 ms");
    }

    [Test]
    public void TestGaugeFailureMarkerCentresAlignWithFailurePoints()
    {
        BmsGaugeHistoryGraph normalGraph = null!;
        BmsGaugeHistoryGraph hardGraph = null!;
        BmsBeatmap normalBeatmap = null!;
        BmsBeatmap hardBeatmap = null!;
        ScoreInfo normalScore = null!;
        ScoreInfo hardScore = null!;

        AddStep("load gauge failure graphs", () =>
        {
            normalScore = new ScoreInfo
            {
                HitEvents = [new HitEvent(0, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null)],
            };

            normalBeatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bme7K),
                Total = 200,
                HitObjects =
                {
                    new BmsNote { StartTime = 1000 },
                    new BmsNote { StartTime = 2000 },
                },
            };

            var hardEvents = Enumerable.Range(0, 20)
                .Select(i => new HitEvent(0, 1, HitResult.Meh, new BmsNote { StartTime = 1000 + i * 100 }, null, null))
                .ToList();

            hardScore = new ScoreInfo
            {
                Mods = [new BmsModHardGauge()],
                HitEvents = hardEvents,
            };

            hardBeatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bme7K),
                Total = 200,
            };

            foreach (var hitEvent in hardEvents)
                hardBeatmap.HitObjects.Add(new BmsNote { StartTime = hitEvent.HitObject.StartTime });

            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 24),
                Children =
                [
                    normalGraph = new BmsGaugeHistoryGraph(normalScore, normalBeatmap),
                    hardGraph = new BmsGaugeHistoryGraph(hardScore, hardBeatmap),
                ],
            };
        });

        AddUntilStep("gauge graphs loaded", () => normalGraph.IsLoaded && normalGraph.DrawHeight > 0 && hardGraph.IsLoaded && hardGraph.DrawHeight > 0);
        AddUntilStep("gauge plots allow overflow", () => !gaugePlotFor(normalGraph).Masking && !gaugePlotFor(hardGraph).Masking);
        AddUntilStep("hard line has room below bottom", () => gaugePathExtendsBelowBottomEdge(hardGraph));
        AddUntilStep("hard line stops at failure point", () => gaugePathEndsAtFailurePoint(hardGraph, hardScore, hardBeatmap));
        AddUntilStep("normal failure marker centre aligns", () => failureMarkerCentreAlignsWithFailurePoint(normalGraph, normalScore, normalBeatmap));
        AddUntilStep("hard failure marker centre aligns", () => failureMarkerCentreAlignsWithFailurePoint(hardGraph, hardScore, hardBeatmap));
    }

    [Test]
    public void TestGaugeFailureMarkerRepositionsWhenParentWidthChanges()
    {
        Container markerContainer = null!;
        BmsGaugeHistoryGraph.GaugeFailureMarker marker = null!;

        AddStep("load failure marker", () =>
        {
            var failurePoint = new BmsGaugeHistoryGraph.GaugePoint(0.5f, 0f);

            Child = markerContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                Height = 180,
                Child = marker = new BmsGaugeHistoryGraph.GaugeFailureMarker(
                    [new BmsGaugeHistoryGraph.GaugePoint(0, 0.5f), failurePoint],
                    failurePoint,
                    1.2f),
            };
        });

        AddUntilStep("marker loaded", () => marker.IsLoaded && marker.Position.X > 0);
        AddUntilStep("initial marker position matches parent width", () =>
            Math.Abs(marker.Position.X - 0.5f * markerContainer.DrawWidth) < 0.5f);

        AddStep("shrink parent", () => markerContainer.Width = 360);

        AddUntilStep("marker follows shrunken parent width", () =>
            Math.Abs(marker.Position.X - 0.5f * markerContainer.DrawWidth) < 0.5f);
    }

    [Test]
    public void TestGaugePathUpdatesWhenParentWidthChanges()
    {
        Container pathContainer = null!;
        BmsGaugeHistoryGraph.GaugePath path = null!;

        AddStep("load gauge path", () =>
        {
            Child = pathContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                Height = 180,
                Child = path = new BmsGaugeHistoryGraph.GaugePath(
                [
                    new BmsGaugeHistoryGraph.GaugePoint(0, 0.5f),
                    new BmsGaugeHistoryGraph.GaugePoint(1, 0.5f),
                ], 0)
                {
                    PathRadius = 1.2f,
                },
            };
        });

        AddUntilStep("path loaded", () => path.IsLoaded && path.Vertices.Count > 0);
        AddUntilStep("initial path matches parent width", () => Math.Abs(path.Vertices[^1].X - (pathContainer.DrawWidth + path.PathRadius)) < 0.5f);

        AddStep("shrink parent", () => pathContainer.Width = 360);

        AddUntilStep("path follows shrunken parent width", () => Math.Abs(path.Vertices[^1].X - (pathContainer.DrawWidth + path.PathRadius)) < 0.5f);
    }

    [Test]
    public void TestHitOffsetStatisticTogglesKeyChartsOnClick()
    {
        BmsHitOffsetStatistic hitOffsetStatistic = null!;

        AddStep("load hit offset statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitOffsetStatistic = new BmsHitOffsetStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit offset statistic loaded", () => hitOffsetStatistic.IsLoaded && hitOffsetStatistic.DrawHeight > 0);
        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");

        AddStep("expand from component body", () =>
        {
            InputManager.MoveMouseTo(hitOffsetStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertHitOffsetText("Scratch");
        assertHitOffsetText("Key 1");

        AddStep("collapse from component body", () =>
        {
            InputManager.MoveMouseTo(hitOffsetStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertNoHitOffsetText("Scratch");
        assertNoHitOffsetText("Key 1");
    }

    [Test]
    public void TestHitOffsetStatisticUsesManiaTimingDistributionStyle()
    {
        BmsHitOffsetStatistic hitOffsetStatistic = null!;

        AddStep("load hit offset statistic", () =>
        {
            var beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bms5K,
                TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
            };

            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitOffsetStatistic = new BmsHitOffsetStatistic(createGaugeHitEvents(), beatmap),
            };
        });

        AddUntilStep("hit offset statistic loaded", () => hitOffsetStatistic.IsLoaded);
        AddUntilStep("rounded timing bars shown", () => hitOffsetStatistic.ChildrenOfType<Circle>().Count() >= 101);
        AddUntilStep("old plot background removed", () => !hitOffsetStatistic.ChildrenOfType<Box>().Any(b => Math.Abs(b.Alpha - 0.18f) < 0.001f));
        AddUntilStep("standard deviation shown", () => hitOffsetStatistic.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString().StartsWith("SD ", StringComparison.Ordinal)));
        assertHitOffsetText("-150");
        assertHitOffsetText("0");
        assertHitOffsetText("+150");
    }

    [Test]
    public void TestHitScatterAxisAndDirectionLabelsAvoidPlotContent()
    {
        BmsHitScatterStatistic hitScatterStatistic = null!;

        AddStep("load hit scatter statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitScatterStatistic = new BmsHitScatterStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit scatter statistic loaded", () => hitScatterStatistic.IsLoaded && hitScatterStatistic.DrawHeight > 0);
        AddUntilStep("axis labels stay outside plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;

            return hitScatterStatistic.ChildrenOfType<SpriteText>()
                .Where(t => t.Text.ToString() is "-150 ms" or "-75 ms" or "0 ms" or "+75 ms" or "+150 ms")
                .All(t => t.ScreenSpaceDrawQuad.AABBFloat.Right < plot.Left);
        });

        AddUntilStep("scatter points stay inside plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;
            var points = hitScatterStatistic.ChildrenOfType<Circle>()
                .Where(c => Math.Abs(c.DrawWidth - 4.4f) < 0.01f || Math.Abs(c.DrawWidth - 5.2f) < 0.01f)
                .Select(c => c.ScreenSpaceDrawQuad.AABBFloat)
                .ToArray();

            return points.Length > 0
                   && points.All(point => point.Left >= plot.Left
                                          && point.Right <= plot.Right
                                          && point.Top >= plot.Top
                                          && point.Bottom <= plot.Bottom);
        });

        AddUntilStep("direction labels stay at plot edges", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;
            var slow = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "slow").ScreenSpaceDrawQuad.AABBFloat;
            var fast = hitScatterStatistic.ChildrenOfType<SpriteText>().Single(t => t.Text.ToString() == "fast").ScreenSpaceDrawQuad.AABBFloat;

            return fast.Top >= plot.Top
                   && fast.Bottom <= plot.Top + 28
                   && slow.Bottom <= plot.Bottom
                   && slow.Top >= plot.Bottom - 28;
        });

        AddUntilStep("axis marks touch plot", () =>
        {
            var plot = plotBackgroundFor(hitScatterStatistic).ScreenSpaceDrawQuad.AABBFloat;

            return hitScatterStatistic.ChildrenOfType<Box>()
                .Where(b => Math.Abs(b.Alpha - 0.45f) < 0.001f || Math.Abs(b.Alpha - 0.25f) < 0.001f)
                .All(b => Math.Abs(b.ScreenSpaceDrawQuad.AABBFloat.Right - plot.Left) < 0.5f);
        });
    }

    [Test]
    public void TestHitScatterStatisticTogglesKeyChartsOnClick()
    {
        BmsHitScatterStatistic hitScatterStatistic = null!;

        AddStep("load hit scatter statistic", () =>
        {
            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 720,
                AutoSizeAxes = Axes.Y,
                Child = hitScatterStatistic = new BmsHitScatterStatistic(createGaugeHitEvents(), new BmsBeatmap
                {
                    LayoutVariant = BmsLayoutVariant.Bms5K,
                    TotalColumns = BmsLayout.GetTotalColumns(BmsLayoutVariant.Bms5K),
                }),
            };
        });

        AddUntilStep("hit scatter statistic loaded", () => hitScatterStatistic.IsLoaded && hitScatterStatistic.DrawHeight > 0);
        assertNoHitScatterText("Scratch");
        assertNoHitScatterText("Key 1");

        AddStep("expand from component body", () =>
        {
            InputManager.MoveMouseTo(hitScatterStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertHitScatterText("Scratch");
        assertHitScatterText("Key 1");

        AddStep("collapse from component body", () =>
        {
            InputManager.MoveMouseTo(hitScatterStatistic);
            InputManager.Click(MouseButton.Left);
        });

        assertNoHitScatterText("Scratch");
        assertNoHitScatterText("Key 1");
    }

    [TestCase(0.813197)]
    [TestCase(0.95)]
    public void TestResultUsesOwnedRankComponents(double accuracy)
    {
        TestBmsSoloResultsScreen screen = null!;

        AddStep("load results screen", () =>
        {
            var stack = new OsuScreenStack
            {
                RelativeSizeAxes = Axes.Both,
            };

            Child = stack;
            var score = createScore();
            score.Accuracy = accuracy;
            stack.Push(screen = new TestBmsSoloResultsScreen(score));
        });

        AddUntilStep("results screen loaded", () => screen.IsLoaded);
        AddUntilStep("owned rank text loaded", () => screen.ChildrenOfType<BmsRankText>().Any());
        AddAssert("rank text shows BMS lettering", () => screen.ChildrenOfType<BmsRankText>().Single()
            .ChildrenOfType<GlowingSpriteText>().Single().Text.ToString(), () => Is.EqualTo("AA"));
        AddAssert("rank badges show all BMS thresholds", () => screen.ChildrenOfType<BmsRankBadge>()
            .Select(badge => badge.ChildrenOfType<BmsDrawableRank>().Single().ChildrenOfType<SpriteText>().Single().Text.ToString()),
            () => Is.EqualTo(new[] { "C", "B", "A", "AA", "AAA", "S" }));
        AddAssert("badge thresholds use EX score ratios", () => screen.ChildrenOfType<BmsRankBadge>().Select(badge => badge.Accuracy),
            () => Is.EqualTo(new[] { 0, 5.0 / 9, 6.0 / 9, 7.0 / 9, 8.0 / 9, 1 }));
        AddStep("finish rating animation", () => screen.ChildrenOfType<BmsAccuracyCircle>().Single().FinishTransforms(true));
        AddAssert("stored rank does not lower accuracy progress", () => screen.ChildrenOfType<BmsAccuracyCircle>().Single()
                .ChildrenOfType<CircularProgress>().Single(circle => circle.Name == "Accuracy circle").Progress,
            () => Is.EqualTo(accuracy - 0.001).Within(0.0001));
        AddAssert("native score panels absent", () => screen.ChildrenOfType<ScorePanel>(), () => Is.Empty);
    }
}
