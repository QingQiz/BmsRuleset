using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsCourseResults : ScreenTestScene
{
    private RealmDetachedBeatmapStore beatmapStore = null!;

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        dependencies.Cache(Realm);
        dependencies.Cache(new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
        dependencies.CacheAs<BeatmapStore>(beatmapStore = new RealmDetachedBeatmapStore());

        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(beatmapStore);
    }

    private void clickCourseStage(string name)
    {
        var stage = this.ChildrenOfType<OsuClickableContainer>().Single(row => row.Name == name);
        InputManager.MoveMouseTo(stage);
        InputManager.Click(MouseButton.Left);
    }

    private static BmsCourseSession createAbortedSession(bool includeFailureEvent = false)
    {
        BeatmapInfo[] beatmaps =
        [
            createBeatmap("stage-1", "First Stage"),
            createBeatmap("stage-2", "Second Stage"),
            createBeatmap("stage-3", "Third Stage"),
            createBeatmap("stage-4", "Fourth Stage"),
        ];
        var definitions = beatmaps.Select((beatmap, index) => new BmsCourseStage(
            beatmap.Metadata.Title,
            $"sl{index + 7}",
            BeatmapHash: beatmap.Hash)).ToArray();
        var resolvedStages = definitions.Zip(beatmaps, (definition, beatmap) => new BmsResolvedCourseStage(definition, beatmap)).ToArray();
        var course = new BmsCourseDefinition("visual-course", "Visual Table", "Visual Course", definitions, []);
        var session = new BmsCourseSession(
            course,
            resolvedStages,
            [new BmsModMirror(), new BmsModClassGauge()],
            BmsGaugeType.Class);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(beatmaps[0], true, 765432, 0.91), [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.72, false)]);
        session.RequestAdvance();
        session.Advance();
        session.BeginCurrentStage();
        var abortedScore = createScore(beatmaps[1], false, 321000, 0.63);

        if (includeFailureEvent)
            addFailureHitEvent(abortedScore);

        session.AbortCurrentStage(abortedScore, [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.28, false)]);

        return session;
    }

    private static BmsCourseSession createFailedSession()
    {
        var beatmap = createBeatmap("failed-stage", "Failed Stage");
        var definition = new BmsCourseStage("Failed Stage", "sl7", BeatmapHash: beatmap.Hash);
        var session = new BmsCourseSession(
            new BmsCourseDefinition("failed-course", "Visual Table", "Failed Course", [definition], []),
            [new BmsResolvedCourseStage(definition, beatmap)],
            [new BmsModClassGauge()],
            BmsGaugeType.Class);
        var score = createScore(beatmap, false, 123000, 0.25);
        addFailureHitEvent(score);

        session.BeginCurrentStage();
        session.FailCurrentStage(score, [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0, true)]);
        return session;
    }

    private static void addFailureHitEvent(ScoreInfo score)
    {
        var mine = new BmsLandmine
        {
            StartTime = 250,
            LandmineDamagePercent = 647.5,
        };
        score.HitEvents =
        [
            new HitEvent(0, 1, HitResult.Meh, mine, null, null),
            new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null),
        ];
    }

    private static bool hasFailureShade(Drawable root) => root.ChildrenOfType<BmsTimelineStatistic>()
        .SelectMany(timeline => timeline.ChildrenOfType<Box>())
        .Any(box => box.Alpha == 0.6f && ((Color4)box.Colour).Equals(new Color4(70, 70, 70, 255)));

    private static BeatmapInfo createBeatmap(string hash, string title) => new()
    {
        Hash = hash,
        DifficultyName = "[7K Hyper]",
        Ruleset = new BmsRuleset().RulesetInfo,
        Metadata = new BeatmapMetadata(new RealmUser { Username = "course mapper" })
        {
            Title = title,
            Artist = "course artist",
        },
    };

    private static ScoreInfo createScore(BeatmapInfo beatmap, bool passed, long totalScore, double accuracy) => new()
    {
        User = new APIUser
        {
            Id = 2,
            Username = "course-player",
        },
        BeatmapInfo = beatmap,
        BeatmapHash = beatmap.Hash,
        Ruleset = beatmap.Ruleset,
        Passed = passed,
        Rank = passed ? ScoreRank.A : ScoreRank.F,
        TotalScore = totalScore,
        TotalScoreWithoutMods = totalScore,
        Accuracy = accuracy,
        MaxCombo = 375,
        Date = new DateTimeOffset(2026, 8, 14, 0, 54, 0, TimeSpan.Zero),
        Mods = [new BmsModMirror(), new BmsModClassGauge()],
        Statistics = new Dictionary<HitResult, int>
        {
            [HitResult.Perfect] = 300,
            [HitResult.Great] = 100,
            [HitResult.Good] = 20,
            [HitResult.Ok] = 5,
            [HitResult.Meh] = 2,
            [HitResult.Miss] = passed ? 0 : 1,
        },
        MaximumStatistics =
        {
            [HitResult.Perfect] = 428,
        },
    };

    private static BmsCourseResultButton button(Drawable root, string text) =>
        root.ChildrenOfType<BmsCourseResultButton>().SingleOrDefault(candidate =>
            candidate.ChildrenOfType<OsuSpriteText>().Any(label => label.Text.ToString() == text));

    private void assertText(string text) =>
        AddUntilStep($"{text} shown", () => this.ChildrenOfType<OsuSpriteText>()
            .Any(spriteText => spriteText.Text.ToString() == text));

    private static bool overviewMatches(Drawable screen, double accuracy, int combo, int perfect, int fast, int slow)
    {
        var overview = screen.ChildrenOfType<BmsResultOverview>().SingleOrDefault();
        var timing = overview?.ChildrenOfType<BmsResultFastSlow>().SingleOrDefault();
        return overview != null && Math.Abs(overview.Score.Accuracy - accuracy) < 0.00001
                                && overview.Score.MaxCombo == combo
                                && overview.ChildrenOfType<BmsResultJudgementRow>().SingleOrDefault(row => row.Result == HitResult.Perfect)?.Count == perfect
                                && overview.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == $"{accuracy * 100:F2}%")
                                && overview.ChildrenOfType<GridContainer>().SingleOrDefault(grid => grid.Name == "Result combo")?
                                    .ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == combo.ToString()) == true
                                && timing?.FastCount == fast && timing.SlowCount == slow;
    }

    private static bool overviewFits(BmsCourseResultsScreen screen)
    {
        var overview = screen.ChildrenOfType<BmsResultOverview>().SingleOrDefault();
        if (overview == null || overview.DrawWidth <= 0)
            return false;

        var bounds = overview.ScreenSpaceDrawQuad.AABBFloat;
        var viewport = screen.ScreenSpaceDrawQuad.AABBFloat;
        var footer = BmsResultsScreenPatcher.GetBottomPanel(screen).ScreenSpaceDrawQuad.AABBFloat;
        var cards = overview.ChildrenOfType<BmsCourseStageCard>().ToArray();
        return bounds.Left >= viewport.Left && bounds.Bottom <= footer.Top && bounds.Top >= viewport.Top
               && cards.Length == 4 && cards.All(card =>
               {
                   var cardBounds = card.ScreenSpaceDrawQuad.AABBFloat;
                   return cardBounds.Height > 0 && cardBounds.Left >= bounds.Left && cardBounds.Right <= bounds.Right
                                               && cardBounds.Top >= bounds.Top && cardBounds.Bottom <= bounds.Bottom;
               })
               && cards.Zip(cards.Skip(1)).All(pair => pair.First.ScreenSpaceDrawQuad.AABBFloat.Bottom < pair.Second.ScreenSpaceDrawQuad.AABBFloat.Top)
               && overview.ChildrenOfType<BmsResultFittedText>().All(cell =>
               {
                   var text = cell.ChildrenOfType<OsuSpriteText>().Single().ScreenSpaceDrawQuad.AABBFloat;
                   return text.Left >= bounds.Left - 1 && text.Right <= bounds.Right + 1
                                                      && text.Top >= bounds.Top && text.Bottom <= bounds.Bottom;
               });
    }

    private void assertNativeButtonHover(Func<BmsCourseResultButton> getButton, string name)
    {
        var restingWidth = 0f;
        var backgroundColour = Color4.White;

        AddStep($"move away from {name}", () => InputManager.MoveMouseTo(new Vector2(-100)));
        AddUntilStep($"{name} icon retracts to native width", () =>
            Math.Abs(getButton().IconLayer.DrawWidth - TwoLayerButton.SIZE_RETRACTED.X * 0.4f) < 0.05f);
        AddStep($"record {name} appearance", () =>
        {
            restingWidth = getButton().DrawWidth;
            backgroundColour = getButton().TextLayer.Colour;
        });
        AddAssert($"{name} label fits text segment", () =>
            getButton().ChildrenOfType<OsuSpriteText>().Single().DrawWidth + 24 <= getButton().TextLayer.DrawWidth);
        AddStep($"hover {name} text", () => InputManager.MoveMouseTo(getButton().TextLayer));
        AddUntilStep($"{name} icon reaches hover colour", () => getButton().IconLayer.Colour == getButton().HoverColour);
        AddUntilStep($"{name} icon expands to native width", () =>
            Math.Abs(getButton().IconLayer.DrawWidth - TwoLayerButton.SIZE_EXTENDED.X * 0.4f) < 0.05f);
        AddAssert($"{name} expands by native distance", () => getButton().DrawWidth - restingWidth,
            () => Is.EqualTo(TwoLayerButton.SIZE_EXTENDED.X - TwoLayerButton.SIZE_RETRACTED.X).Within(0.2f));
        AddAssert($"{name} text keeps native background", () => getButton().TextLayer.Colour == backgroundColour);
        AddStep($"hover {name} icon", () => InputManager.MoveMouseTo(getButton().IconLayer));
        AddAssert($"{name} stays hovered across segments", () => getButton().IsHovered);
        AddStep($"leave {name}", () => InputManager.MoveMouseTo(new Vector2(-100)));
        AddUntilStep($"{name} restores native appearance", () =>
            Math.Abs(getButton().DrawWidth - restingWidth) < 0.05f && getButton().IconLayer.Colour == backgroundColour);
    }

    [Test]
    public void TestAbortedStageDoesNotShadeCourseTimelineAsFailed()
    {
        var session = createAbortedSession(true);
        BmsCourseResultsScreen screen = null!;

        AddStep("show aborted course summary", () => Stack.Push(screen = new BmsCourseResultsScreen(session)));
        AddUntilStep("course timeline loaded", () => screen.ChildrenOfType<BmsTimelineStatistic>().SingleOrDefault(), () => Is.Not.Null);
        AddAssert("course timeline has no failure shade", () => hasFailureShade(screen), () => Is.False);
        AddStep("open aborted stage", () => clickCourseStage("Course stage 2 result"));
        AddUntilStep("aborted stage statistics shown", () => screen.SelectedStageIndex == 1
                                                             && screen.ChildrenOfType<StatisticsPanel>()
                                                                 .Single(panel => panel is not BmsCourseAggregateStatistics)
                                                                 .State.Value == Visibility.Visible);
        AddUntilStep("aborted stage timeline loaded", () => screen.ChildrenOfType<BmsTimelineStatistic>().Count(), () => Is.EqualTo(2));
        AddAssert("aborted stage timeline has no failure shade", () => hasFailureShade(screen), () => Is.False);
    }

    [TestCase(1280, 720)]
    [TestCase(1024, 600)]
    [TestCase(1600, 900)]
    public void TestCourseSummaryShowsPlayedAndEmptyStages(int width, int height)
    {
        var session = createAbortedSession();
        session.Stages[3].Stage.Beatmap.Metadata.Title = "Fourth Stage With A Long Song Title That Must Stay Inside The Card";
        session.Stages[3].Stage.Beatmap.Metadata.Artist = "A Long Artist Name For The Compact Course Card";
        session.Stages[3].Stage.Beatmap.DifficultyName = "[7K Hyper - A Long Difficulty Name]";
        var firstScore = session.Stages[0].Score!;
        var secondScore = session.Stages[1].Score!;
        firstScore.HitEvents =
        [
            new HitEvent(-8, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null),
            new HitEvent(12, 1, HitResult.Great, new BmsNote { StartTime = 2000 }, null, null),
        ];
        secondScore.HitEvents = [new HitEvent(24, 1, HitResult.Good, new BmsNote { StartTime = 1000 }, null, null)];
        secondScore.MaxCombo = 150;
        BmsCourseResultsScreen screen = null!;

        AddStep("show course summary", () =>
        {
            Stack.RelativeSizeAxes = Axes.None;
            Stack.Size = new Vector2(width, height);
            Stack.Scale = new Vector2(Math.Min(Content.DrawWidth / width, Content.DrawHeight / height));
            Stack.Push(screen = new BmsCourseResultsScreen(session));
        });
        AddUntilStep("course summary loaded", () => screen.IsLoaded && Stack.CurrentScreen == screen);

        assertText("Gauge History");
        assertText("Timeline");
        assertText("Hit Scatter");
        assertText("Hit Offset");
        assertText("ACCURACY");
        assertText("EXSCORE");
        assertText("77.00%");
        AddAssert("four course stage cards shown", () => screen.ChildrenOfType<BmsCourseStageCard>().Count(), () => Is.EqualTo(4));
        assertText("Visual Course");
        assertText("Visual Table");
        AddAssert("course heading uses large name above small table", () =>
        {
            var heading = screen.ChildrenOfType<GridContainer>().Single(grid => grid.Name == "Course heading");
            var name = heading.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == "Visual Course");
            var table = heading.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.ToString() == "Visual Table");
            var firstCard = screen.ChildrenOfType<BmsCourseStageCard>().First();
            return name.ScreenSpaceDrawQuad.AABBFloat.Bottom <= table.ScreenSpaceDrawQuad.AABBFloat.Top
                   && table.ScreenSpaceDrawQuad.AABBFloat.Bottom < firstCard.ScreenSpaceDrawQuad.AABBFloat.Top
                   && name.ScreenSpaceDrawQuad.AABBFloat.Height > table.ScreenSpaceDrawQuad.AABBFloat.Height;
        });
        AddAssert("course status omitted", () => screen.ChildrenOfType<OsuSpriteText>()
            .Any(text => text.Text.ToString() == "Course abandoned"), () => Is.False);
        AddAssert("cards omit stage status colours", () => screen.ChildrenOfType<Box>()
            .Any(box => box.Name == "Stage status indicator"), () => Is.False);
        AddAssert("cards use solo result banners", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .All(card => card.ChildrenOfType<Container>().Any(container => container.Name == "Result beatmap banner")));
        AddAssert("cards show three metadata lines", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .All(card => card.ChildrenOfType<BmsResultFittedText>().Count() == 3));
        AddUntilStep("all card text fits its own row", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .SelectMany(card => card.ChildrenOfType<BmsResultFittedText>()).All(cell =>
            {
                var bounds = cell.ScreenSpaceDrawQuad.AABBFloat;
                var text = cell.ChildrenOfType<OsuSpriteText>().Single().ScreenSpaceDrawQuad.AABBFloat;
                return text.Left >= bounds.Left - 0.5f && text.Right <= bounds.Right + 0.5f
                                                     && text.Top >= bounds.Top - 0.5f && text.Bottom <= bounds.Bottom + 0.5f;
            }));
        AddAssert("cards omit mapper and inline score statistics", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .All(card => !card.ChildrenOfType<StatisticDisplay>().Any()
                         && !card.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == "course mapper")));
        AddAssert("course overview replaces rank circle", () => screen.ChildrenOfType<BmsResultOverview>().Single()
            .ChildrenOfType<AccuracyCircle>(), () => Is.Empty);
        AddAssert("course cards retain translucent outer section", () =>
        {
            var header = screen.ChildrenOfType<BmsCourseOverviewHeader>().Single();
            return header.Parent!.Parent!.ChildrenOfType<Box>().Any(box => box.Alpha == 0.6f && box.Parent!.Masking);
        });
        AddUntilStep("aggregate left statistics match", () => overviewMatches(screen, 0.77, 375, 600, 1, 2));
        AddAssert("course overview omits pp", () => screen.ChildrenOfType<BmsResultOverview>().Single()
            .ChildrenOfType<PerformanceStatistic>(), () => Is.Empty);
        AddUntilStep("all cards and left statistics fit viewport", () => overviewFits(screen));
        AddAssert("unplayed stage disabled", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .Single(card => card.Name == "Course stage 3 result").Enabled.Value, () => Is.False);
        AddStep("click unplayed stage", () => clickCourseStage("Course stage 3 result"));
        AddAssert("unplayed stage leaves summary selected", () => screen.SelectedStageIndex, () => Is.Null);
        AddAssert("aggregate statistics have popover container", () => screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single()
                .FindClosestParent<PopoverContainer>(),
            () => Is.Not.Null);
        AddAssert("return button shown", () => button(screen, "Return to course select"), () => Is.Not.Null);
        assertNativeButtonHover(() => button(screen, "Return to course select")!, "return button");
        AddUntilStep("aggregate charts fit viewport", () => TestSceneBmsResultScreenStatistics.ChartsFitViewport(screen));
        AddAssert("only gauge and timeline share a row", () => TestSceneBmsResultScreenStatistics.ChartsUseExpectedRows(screen));
        AddAssert("left and right columns do not overlap", () =>
        {
            var left = screen.ChildrenOfType<BmsResultOverview>().Single().ScreenSpaceDrawQuad.AABBFloat;
            var right = screen.ChildrenOfType<BmsResultStatisticsGrid>().Single().ScreenSpaceDrawQuad.AABBFloat;
            return left.Right <= right.Left && Math.Abs(left.Top - right.Top) < 1;
        });
        AddAssert("aggregate charts stay above footer", () => screen.ChildrenOfType<BmsResultStatisticsGrid>().Single()
            .ScreenSpaceDrawQuad.AABBFloat.Bottom <= BmsResultsScreenPatcher.GetBottomPanel(screen).ScreenSpaceDrawQuad.AABBFloat.Top);

        AddStep("open first stage score", () => clickCourseStage("Course stage 1 result"));
        AddUntilStep("first stage selected", () => screen.SelectedStageIndex == 0);
        AddUntilStep("first stage left statistics match", () => overviewMatches(screen, 0.91, 375, 300, 1, 1));
        AddAssert("first stage remains highlighted", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .Single(card => card.IsSelected).Name, () => Is.EqualTo("Course stage 1 result"));
        AddAssert("screen stack unchanged", () => Stack.CurrentScreen, () => Is.SameAs(screen));
        AddAssert("first stage score selected", () => screen.SelectedScore.Value, () => Is.SameAs(session.Stages[0].Score));
        AddUntilStep("native statistics shown", () => screen.ChildrenOfType<StatisticsPanel>()
            .Single(panel => panel is not BmsCourseAggregateStatistics).State.Value == Visibility.Visible);
        AddUntilStep("first stage chart shows its own hits", () => screen.ChildrenOfType<BmsStatisticsPanel>()
            .Single(panel => panel is not BmsCourseAggregateStatistics).ChildrenOfType<BmsHitScatterStatistic>()
            .SelectMany(chart => chart.ChildrenOfType<OsuSpriteText>()).Any(text => text.Text.ToString() == "2 hits"));
        AddUntilStep("native score card hidden", () => screen.ChildrenOfType<ScorePanel>().All(panel => !panel.IsPresent));
        AddAssert("score replay disabled", () => screen.AllowWatchingReplay, () => Is.False);
        AddAssert("score retry disabled", () => screen.AllowRetry, () => Is.False);

        AddStep("toggle first stage back to course summary", () => clickCourseStage("Course stage 1 result"));
        AddUntilStep("course summary restored from first stage", () => screen.SelectedStageIndex == null
                                                                       && screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single().State.Value == Visibility.Visible);
        AddUntilStep("aggregate left statistics restored", () => overviewMatches(screen, 0.77, 375, 600, 1, 2));

        AddStep("open second stage directly", () => clickCourseStage("Course stage 2 result"));
        AddUntilStep("second stage selected directly", () => screen.SelectedStageIndex == 1
                                                             && ReferenceEquals(screen.SelectedScore.Value, session.Stages[1].Score)
                                                             && screen.ChildrenOfType<StatisticsPanel>()
                                                                 .Single(panel => panel is not BmsCourseAggregateStatistics)
                                                                 .State.Value == Visibility.Visible);

        AddUntilStep("second stage left statistics match", () => overviewMatches(screen, 0.63, 150, 300, 0, 1));
        AddUntilStep("second stage charts fit viewport", () => TestSceneBmsResultScreenStatistics.ChartsFitViewport(screen));
        AddUntilStep("second stage chart shows its own hits", () => screen.ChildrenOfType<BmsStatisticsPanel>()
            .Single(panel => panel is not BmsCourseAggregateStatistics).ChildrenOfType<BmsHitScatterStatistic>()
            .SelectMany(chart => chart.ChildrenOfType<OsuSpriteText>()).Any(text => text.Text.ToString() == "1 hits"));
        AddAssert("stage selection preserves all song cards", () => overviewFits(screen));

        AddStep("switch from second stage to first stage", () => clickCourseStage("Course stage 1 result"));
        AddUntilStep("first stage selected directly", () => screen.SelectedStageIndex == 0
                                                            && ReferenceEquals(screen.SelectedScore.Value, session.Stages[0].Score)
                                                            && screen.ChildrenOfType<StatisticsPanel>()
                                                                .Single(panel => panel is not BmsCourseAggregateStatistics)
                                                                .State.Value == Visibility.Visible);
        AddUntilStep("first stage left statistics restored", () => overviewMatches(screen, 0.91, 375, 300, 1, 1));
        AddStep("rapidly switch stages", () =>
        {
            clickCourseStage("Course stage 2 result");
            clickCourseStage("Course stage 1 result");
            clickCourseStage("Course stage 2 result");
        });
        AddUntilStep("both columns finish on latest stage", () => screen.SelectedStageIndex == 1
            && ReferenceEquals(screen.SelectedScore.Value, secondScore) && overviewMatches(screen, 0.63, 150, 300, 0, 1));
        AddStep("click selected stage to restore summary", () => clickCourseStage("Course stage 2 result"));
        AddUntilStep("selected stage restores both columns", () => screen.SelectedStageIndex == null
            && overviewMatches(screen, 0.77, 375, 600, 1, 2)
            && screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single().State.Value == Visibility.Visible);
        AddStep("select stage before back", () => clickCourseStage("Course stage 1 result"));
        AddUntilStep("stage selected before back", () => ReferenceEquals(screen.SelectedScore.Value, firstScore));
        AddStep("return to aggregate summary", () => screen.OnBackButton());
        AddUntilStep("aggregate charts visible", () => screen.SelectedStageIndex == null
            && TestSceneBmsResultScreenStatistics.ChartsFitViewport(screen));
        AddStep("expand aggregate key charts", () =>
        {
            InputManager.MoveMouseTo(screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single().ChildrenOfType<BmsHitOffsetStatistic>().Single());
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("expanded aggregate charts scroll", () => screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single()
            .ChildrenOfType<OsuScrollContainer>().Single().ScrollableExtent > 0);
        AddStep("select stage before exiting", () => clickCourseStage("Course stage 2 result"));
        AddUntilStep("stage statistics loaded before exiting", () => ReferenceEquals(screen.SelectedScore.Value, secondScore)
            && screen.ChildrenOfType<BmsStatisticsPanel>().Single(panel => panel is not BmsCourseAggregateStatistics)
                .ChildrenOfType<BmsHitScatterStatistic>().Any());
        AddStep("return to select through pointer", () =>
        {
            InputManager.MoveMouseTo(button(screen, "Return to course select")!);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("course results exited", () => Stack.CurrentScreen != screen);
        AddStep("dispose selected results twice", () =>
        {
            screen.Dispose();
            screen.Dispose();
        });
    }

    [Test]
    public void TestFailedStageDoesNotShadeCourseTimeline()
    {
        var session = createFailedSession();
        BmsCourseResultsScreen screen = null!;

        AddStep("show failed course summary", () => Stack.Push(screen = new BmsCourseResultsScreen(session)));
        AddUntilStep("course timeline loaded", () => screen.ChildrenOfType<BmsTimelineStatistic>().SingleOrDefault(), () => Is.Not.Null);
        AddAssert("course timeline has no failure shade", () => hasFailureShade(screen), () => Is.False);
    }

    [TestCase(1280, 720)]
    [TestCase(1024, 600)]
    public void TestAllStageSelectionOutlines(int width, int height)
    {
        var beatmaps = Enumerable.Range(1, 4).Select(index => createBeatmap($"outline-{index}", $"Stage {index}")).ToArray();
        var stages = beatmaps.Select(beatmap => new BmsCourseStage(beatmap.Metadata.Title, "sl7", BeatmapHash: beatmap.Hash)).ToArray();
        var session = new BmsCourseSession(new BmsCourseDefinition("outline-course", "Visual Table", "Visual Course", stages, []),
            stages.Zip(beatmaps, (stage, beatmap) => new BmsResolvedCourseStage(stage, beatmap)).ToArray(), [], BmsGaugeType.Class);
        for (var index = 0; index < stages.Length; index++)
        {
            session.BeginCurrentStage();
            session.CompleteCurrentStage(createScore(beatmaps[index], true, 123456, 0.9), [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.8, false)]);
            if (index < stages.Length - 1)
            {
                session.RequestAdvance();
                session.Advance();
            }
        }

        BmsCourseResultsScreen screen = null!;
        AddStep("show completed course", () =>
        {
            Stack.RelativeSizeAxes = Axes.None;
            Stack.Size = new Vector2(width, height);
            Stack.Scale = new Vector2(Math.Min(Content.DrawWidth / width, Content.DrawHeight / height));
            Stack.Push(screen = new BmsCourseResultsScreen(session, recordResult: false));
        });
        AddUntilStep("four cards loaded", () => screen.ChildrenOfType<BmsCourseStageCard>().Count(), () => Is.EqualTo(4));
        AddStep("make all song backgrounds opaque", () =>
        {
            foreach (var card in screen.ChildrenOfType<BmsCourseStageCard>())
                card.ChildrenOfType<Container>().Single(container => container.Name == "Result beatmap banner")
                    .Add(new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(40, 40, 40, 255), Depth = 1 });
        });
        for (var index = 0; index < stages.Length; index++)
        {
            var stageIndex = index;
            AddStep($"select stage {index + 1}", () => clickCourseStage($"Course stage {stageIndex + 1} result"));
            AddUntilStep($"stage {index + 1} statistics selected", () => screen.SelectedStageIndex == stageIndex
                && ReferenceEquals(screen.SelectedScore.Value, session.Stages[stageIndex].Score));
            AddStep($"move away from stage {index + 1}", () => InputManager.MoveMouseTo(screen.ScreenSpaceDrawQuad.Centre));
            AddWaitStep($"stage {index + 1} hover fades", 2);
            AddAssert($"stage {index + 1} stays selected", () => screen.ChildrenOfType<BmsCourseStageCard>()
                .All(card => card.ChildrenOfType<Container>().Single(container => container.Name == "Stage selection border").IsPresent == card.IsSelected));
        }
        AddStep("clear stage 4 selection", () => clickCourseStage("Course stage 4 result"));
        AddUntilStep("aggregate selection restored", () => screen.SelectedStageIndex == null);
        AddAssert("no card has a selected outline", () => screen.ChildrenOfType<BmsCourseStageCard>()
            .All(card => !card.ChildrenOfType<Container>().Single(container => container.Name == "Stage selection border").IsPresent));
    }

    [Test]
    public void TestIntermediateStageResultIsRestricted()
    {
        var nextRequested = false;
        var abandonRequested = false;
        var sourceScore = createScore(createBeatmap("stage-1", "First Stage"), true, 765432, 0.91);
        sourceScore.HitEvents = [new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 1000 }, null, null)];
        BmsCourseStageResultsScreen screen = null!;

        AddStep("show intermediate stage result", () => Stack.Push(screen = new BmsCourseStageResultsScreen(
            sourceScore,
            () => nextRequested = true,
            () => abandonRequested = true)));
        AddUntilStep("result screen loaded", () => screen.IsLoaded && Stack.CurrentScreen == screen);

        AddUntilStep("stage statistics expanded", () => screen.ChildrenOfType<StatisticsPanel>().Single().State.Value == Visibility.Visible);

        AddAssert("result uses score copy", () => screen.Score, () => Is.Not.SameAs(sourceScore));
        AddAssert("watch replay disabled", () => screen.AllowWatchingReplay, () => Is.False);
        AddAssert("retry disabled", () => screen.AllowRetry, () => Is.False);
        AddAssert("no retry action", () => screen.ChildrenOfType<RetryButton>(), () => Is.Empty);
        AddUntilStep("countdown shown", () => screen.ChildrenOfType<OsuSpriteText>()
            .Any(text => text.Text.ToString().StartsWith("Next stage in ", StringComparison.Ordinal)));
        AddAssert("abandon button shown", () => button(screen, "Abandon course"), () => Is.Not.Null);
        AddAssert("next stage button shown", () => button(screen, "Start next stage"), () => Is.Not.Null);
        AddAssert("course controls use native footer height", () => screen.ChildrenOfType<Container>()
                .Single(container => container.Name == "Course stage result controls").DrawHeight,
            () => Is.EqualTo(TwoLayerButton.SIZE_EXTENDED.Y));
        AddAssert("course controls stay inside result screen", () =>
        {
            var controls = screen.ChildrenOfType<Container>()
                .Single(container => container.Name == "Course stage result controls");
            var bounds = controls.ScreenSpaceDrawQuad.AABBFloat;
            var screenBounds = screen.ScreenSpaceDrawQuad.AABBFloat;
            return bounds.Top >= screenBounds.Top && bounds.Bottom <= screenBounds.Bottom;
        });
        AddAssert("course buttons have visible labels", () =>
            button(screen, "Abandon course")!.ChildrenOfType<OsuSpriteText>().Any(text => text.IsPresent && text.Text.ToString() == "Abandon course")
            && button(screen, "Start next stage")!.ChildrenOfType<OsuSpriteText>().Any(text => text.IsPresent && text.Text.ToString() == "Start next stage"));
        AddAssert("course buttons fill footer height", () =>
                button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat.Top
                - screen.ChildrenOfType<Container>().Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat.Top,
            () => Is.EqualTo(0).Within(0.5f));
        AddAssert("course buttons reach footer bottom", () =>
                screen.ChildrenOfType<Container>().Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat.Bottom
                - button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat.Bottom,
            () => Is.EqualTo(0).Within(0.5f));
        AddAssert("course buttons align to opposite footer edges", () =>
        {
            var footerBounds = screen.ChildrenOfType<Container>()
                .Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat;
            var abandonBounds = button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat;
            var nextBounds = button(screen, "Start next stage")!.ScreenSpaceDrawQuad.AABBFloat;
            return Math.Abs(abandonBounds.Left - footerBounds.Left) < 6
                   && Math.Abs(footerBounds.Right - nextBounds.Right) < 6;
        });
        AddAssert("abandon and next buttons use distinct status colours", () =>
        {
            var abandon = (Color4)button(screen, "Abandon course")!.TextLayer.Colour;
            var next = (Color4)button(screen, "Start next stage")!.TextLayer.Colour;
            return abandon != next && abandon.R > abandon.G && next.G > next.R;
        });
        AddUntilStep("stage charts fit viewport", () => TestSceneBmsResultScreenStatistics.ChartsFitViewport(screen));
        assertNativeButtonHover(() => button(screen, "Abandon course")!, "abandon button");
        assertNativeButtonHover(() => button(screen, "Start next stage")!, "next stage button");
        AddAssert("only gauge and timeline share a row", () => TestSceneBmsResultScreenStatistics.ChartsUseExpectedRows(screen));
        AddStep("expand stage key charts", () =>
        {
            InputManager.MoveMouseTo(screen.ChildrenOfType<BmsHitOffsetStatistic>().Single());
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("expanded stage charts scroll", () => screen.ChildrenOfType<BmsResultStatisticsGrid>().Single()
            .ChildrenOfType<OsuScrollContainer>().Single().ScrollableExtent > 0);

        AddStep("request abandon through pointer", () =>
        {
            InputManager.MoveMouseTo(button(screen, "Abandon course")!);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("confirmation shown", () => DialogOverlay.CurrentDialog is BmsCourseAbandonDialog);
        AddAssert("course not abandoned before confirmation", () => abandonRequested, () => Is.False);
        AddStep("cancel abandon", () => DialogOverlay.CurrentDialog!.Buttons.Last().TriggerClick());
        AddUntilStep("confirmation hidden", () => DialogOverlay.CurrentDialog?.State.Value != Visibility.Visible);

        AddStep("start next stage through pointer", () =>
        {
            InputManager.MoveMouseTo(button(screen, "Start next stage")!);
            InputManager.Click(MouseButton.Left);
        });
        AddUntilStep("next stage requested", () => nextRequested);
        AddAssert("abandon was not requested", () => abandonRequested, () => Is.False);
        AddUntilStep("result screen exited", () => Stack.CurrentScreen != screen);
    }

    [Test]
    public void TestIntermediateStageEnterAdvancesInsteadOfTogglingStatistics()
    {
        var nextRequested = false;
        var sourceScore = createScore(createBeatmap("stage-enter", "Enter Stage"), true, 765432, 0.91);
        BmsCourseStageResultsScreen screen = null!;

        AddStep("show intermediate stage result", () => Stack.Push(screen = new BmsCourseStageResultsScreen(
            sourceScore,
            () => nextRequested = true,
            () => { })));
        AddUntilStep("result screen loaded", () => screen.IsLoaded && Stack.CurrentScreen == screen);
        AddUntilStep("stage statistics expanded", () => screen.ChildrenOfType<StatisticsPanel>().Single().State.Value == Visibility.Visible);
        AddUntilStep("stage result overview visible", () => screen.ChildrenOfType<BmsResultOverview>()
            .Any(panel => panel.DrawColourInfo.Colour.TopLeft.Alpha > 0));
        AddAssert("stage overview stays on the left", () =>
        {
            var scorePanel = screen.ChildrenOfType<BmsResultOverview>().Single();
            return scorePanel.ScreenSpaceDrawQuad.Centre.X < screen.ScreenSpaceDrawQuad.Centre.X;
        });
        AddStep("press enter", () => InputManager.Key(Key.Enter));
        AddUntilStep("next stage requested", () => nextRequested);
        AddAssert("statistics remain expanded", () => screen.ChildrenOfType<StatisticsPanel>().Single().State.Value,
            () => Is.EqualTo(Visibility.Visible));
    }
}
