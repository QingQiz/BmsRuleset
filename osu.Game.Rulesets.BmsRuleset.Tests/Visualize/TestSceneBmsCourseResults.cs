using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
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
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Tests.Visual;

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

    [Test]
    public void TestIntermediateStageResultIsRestricted()
    {
        var nextRequested = false;
        var abandonRequested = false;
        var sourceScore = createScore(createBeatmap("stage-1", "First Stage"), true, 765432, 0.91);
        BmsCourseStageResultsScreen screen = null!;

        AddStep("show intermediate stage result", () => Stack.Push(screen = new BmsCourseStageResultsScreen(
            sourceScore,
            () => nextRequested = true,
            () => abandonRequested = true)));
        AddUntilStep("result screen loaded", () => screen.IsLoaded && Stack.CurrentScreen == screen);

        AddAssert("result uses score copy", () => screen.Score, () => Is.Not.SameAs(sourceScore));
        AddAssert("watch replay disabled", () => screen.AllowWatchingReplay, () => Is.False);
        AddAssert("retry disabled", () => screen.AllowRetry, () => Is.False);
        AddAssert("no replay action", () => screen.ChildrenOfType<ReplayDownloadButton>(), () => Is.Empty);
        AddAssert("no retry action", () => screen.ChildrenOfType<RetryButton>(), () => Is.Empty);
        AddUntilStep("countdown shown", () => screen.ChildrenOfType<OsuSpriteText>()
                                                        .Any(text => text.Text.ToString().StartsWith("Next stage in ")));
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
        AddAssert("course buttons have top footer inset", () =>
            button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat.Top
            - screen.ChildrenOfType<Container>().Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat.Top,
            () => Is.GreaterThanOrEqualTo(4.5f));
        AddAssert("course buttons have bottom footer inset", () =>
            screen.ChildrenOfType<Container>().Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat.Bottom
            - button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat.Bottom,
            () => Is.GreaterThanOrEqualTo(4.5f));
        AddAssert("course buttons align to opposite footer edges", () =>
        {
            var footerBounds = screen.ChildrenOfType<Container>()
                                     .Single(container => container.Name == "Course stage result controls").ScreenSpaceDrawQuad.AABBFloat;
            var abandonBounds = button(screen, "Abandon course")!.ScreenSpaceDrawQuad.AABBFloat;
            var nextBounds = button(screen, "Start next stage")!.ScreenSpaceDrawQuad.AABBFloat;
            return System.Math.Abs(abandonBounds.Left - footerBounds.Left - 5) < 0.5f
                   && System.Math.Abs(footerBounds.Right - nextBounds.Right - 5) < 0.5f;
        });
        AddAssert("abandon and next buttons use distinct status colours", () =>
        {
            var abandon = button(screen, "Abandon course")!.BackgroundColour;
            var next = button(screen, "Start next stage")!.BackgroundColour;
            return abandon != next && abandon.R > abandon.G && next.G > next.R;
        });

        AddStep("request abandon", () => button(screen, "Abandon course")!.TriggerClick());
        AddUntilStep("confirmation shown", () => DialogOverlay.CurrentDialog is BmsCourseAbandonDialog);
        AddAssert("course not abandoned before confirmation", () => abandonRequested, () => Is.False);
        AddStep("cancel abandon", () => DialogOverlay.CurrentDialog!.Buttons.Last().TriggerClick());
        AddUntilStep("confirmation hidden", () => DialogOverlay.CurrentDialog?.State.Value != Visibility.Visible);

        AddStep("start next stage", () => button(screen, "Start next stage")!.TriggerClick());
        AddUntilStep("next stage requested", () => nextRequested);
        AddAssert("abandon was not requested", () => abandonRequested, () => Is.False);
        AddUntilStep("result screen exited", () => Stack.CurrentScreen != screen);
    }

    [Test]
    public void TestCourseSummaryShowsPlayedAndEmptyStages()
    {
        var session = createAbortedSession();
        BmsCourseResultsScreen screen = null!;

        AddStep("show course summary", () => Stack.Push(screen = new BmsCourseResultsScreen(session)));
        AddUntilStep("course summary loaded", () => screen.IsLoaded && Stack.CurrentScreen == screen);

        assertText("Visual Course");
        assertText("Course abandoned");
        assertText("Gauge History");
        assertText("Timeline");
        assertText("Hit Scatter");
        assertText("Hit Offset");
        assertText("course artist");
        assertText("ACC");
        assertText("EXSCORE");
        assertText("91.00%");
        assertText("63.00%");
        AddAssert("four course stage cards shown", () => this.ChildrenOfType<BmsCourseStageCard>().Count(), () => Is.EqualTo(4));
        AddAssert("stage cards clip rounded corners", () => this.ChildrenOfType<BmsCourseStageCard>()
                                                                  .All(card => card.Masking && card.CornerRadius == 7 && card.CornerExponent == 2.5f));
        AddAssert("four details panels shown", () => this.ChildrenOfType<Container>()
                                                           .Count(container => container.Name == "Stage details panel"), () => Is.EqualTo(4));
        AddAssert("details panels clip rounded corners", () => this.ChildrenOfType<Container>()
                                                                     .Where(container => container.Name == "Stage details panel")
                                                                     .All(panel => panel.Masking && panel.CornerRadius == 7 && panel.CornerExponent == 2.5f
                                                                                   && panel.Width == 270 && panel.X == -8));
        AddAssert("four rounded square covers shown", () => this.ChildrenOfType<Container>()
                                                               .Where(container => container.Name == "Stage cover")
                                                               .Count(container => container.Masking && container.Width == 72 && container.Height == 1
                                                                                   && container.CornerRadius == 7), () => Is.EqualTo(4));
        AddAssert("fallback backgrounds cover all card images", () => this.ChildrenOfType<Sprite>()
                                                                         .Count(sprite => sprite.Name == "Course beatmap fallback background"),
            () => Is.EqualTo(8));
        AddAssert("fallback background textures are loaded", () => this.ChildrenOfType<Sprite>()
                                                                         .Where(sprite => sprite.Name == "Course beatmap fallback background")
                                                                         .All(sprite => sprite.Texture != null));
        AddAssert("all cards show the song artist", () => this.ChildrenOfType<OsuSpriteText>()
                                                               .Count(text => text.Text.ToString() == "course artist"),
            () => Is.EqualTo(4));
        AddAssert("song metadata is separated from the cover", () => this.ChildrenOfType<BmsCourseStageCard>().All(card =>
        {
            var cover = card.ChildrenOfType<Container>().Single(container => container.Name == "Stage cover");
            var metadata = card.ChildrenOfType<Container>().Single(container => container.Name == "Stage metadata");
            var title = metadata.ChildrenOfType<TruncatingSpriteText>().First();
            return title.ScreenSpaceDrawQuad.AABBFloat.Left - cover.ScreenSpaceDrawQuad.AABBFloat.Right >= 7.5f;
        }));
        AddAssert("score details are separated from the cover", () => this.ChildrenOfType<BmsCourseStageCard>()
                                                                         .Where(card => card.Enabled.Value)
                                                                         .All(card =>
                                                                         {
                                                                             var statistics = card.ChildrenOfType<Container>()
                                                                                                  .Single(container => container.Name == "Stage score statistics");
                                                                             return statistics.Padding.Left == 18;
                                                                         }));
        AddAssert("stage cards omit empty result overlays", () => this.ChildrenOfType<OsuSpriteText>()
                                                                        .Any(text => text.Text.ToString() == "Not played"),
            () => Is.False);
        AddAssert("unplayed stage remains opaque", () => this.ChildrenOfType<BmsCourseStageCard>()
                                                               .Single(card => card.Name == "Course stage 3 result").Alpha,
            () => Is.EqualTo(1));
        AddAssert("unplayed stage uses gray status colour", () =>
        {
            var colour = BmsCourseResultPresentation.StageStatusColour(BmsCourseStageStatus.NotPlayed);
            return colour.R < 0.9f
                   && System.MathF.Abs(colour.R - colour.G) < 0.001f
                   && System.MathF.Abs(colour.G - colour.B) < 0.001f;
        });
        AddAssert("played stages use requested status colours", () =>
        {
            var passed = BmsCourseResultPresentation.StageStatusColour(BmsCourseStageStatus.Passed);
            var failed = BmsCourseResultPresentation.StageStatusColour(BmsCourseStageStatus.Failed);
            var aborted = BmsCourseResultPresentation.StageStatusColour(BmsCourseStageStatus.Aborted);
            return passed.R > 0.99f && passed.G > 0.99f && passed.B > 0.99f
                   && failed.R > 0.99f && failed.G < 0.01f && failed.B < 0.01f
                   && aborted.R > 0.9f && aborted.G > 0.4f && aborted.G < 0.6f
                   && System.MathF.Abs(aborted.G - aborted.B) < 0.001f;
        });
        AddAssert("course title is separated from cards", () => this.ChildrenOfType<BmsCourseStageCardList>().Single().Margin.Top,
            () => Is.EqualTo(6));
        AddAssert("status colour fills each card background", () => this.ChildrenOfType<Box>()
                                                                       .Count(box => box.Name == "Stage status background"
                                                                                     && box.RelativeSizeAxes == Axes.Both),
            () => Is.EqualTo(4));
        AddAssert("ruleset and mods are centred", () => this.ChildrenOfType<FillFlowContainer>()
                                                               .Single(flow => flow.Name == "Course ruleset and mods"),
            () => Has.Property(nameof(Anchor)).EqualTo(Anchor.TopCentre)
                     .And.Property(nameof(Origin)).EqualTo(Anchor.TopCentre));
        AddAssert("mods are separated from surrounding content", () =>
        {
            var margin = this.ChildrenOfType<FillFlowContainer>()
                             .Single(flow => flow.Name == "Course ruleset and mods").Margin;
            return margin.Top == 6 && margin.Bottom == 6;
        });
        AddAssert("ruleset icon shown", () => this.ChildrenOfType<DifficultyIcon>().Count(), () => Is.EqualTo(1));
        AddAssert("course summary omits pp", () => this.ChildrenOfType<BmsCourseScoreMiddleContent>()
                                                      .Single().ChildrenOfType<PerformanceStatistic>(), () => Is.Empty);
        AddAssert("course played time shown", () => this.ChildrenOfType<PlayedOnText>()
                                                         .Count(text => text.Name == "Course played time"), () => Is.EqualTo(1));
        AddAssert("aggregate statistics use native panel", () => this.ChildrenOfType<BmsCourseAggregateStatistics>().Count(), () => Is.EqualTo(1));
        AddAssert("aggregate statistics have popover container", () => this.ChildrenOfType<BmsCourseAggregateStatistics>().Single()
                                                                           .FindClosestParent<PopoverContainer>(),
            () => Is.Not.Null);
        AddAssert("return button shown", () => button(this, "Return to course select"), () => Is.Not.Null);
        AddAssert("unplayed stage disabled", () => this.ChildrenOfType<OsuClickableContainer>()
                                                         .Single(row => row.Name == "Course stage 3 result").Enabled.Value, () => Is.False);

        AddStep("open first stage score", () => this.ChildrenOfType<OsuClickableContainer>()
                                                      .Single(row => row.Name == "Course stage 1 result")
                                                      .TriggerClick());
        AddUntilStep("first stage selected", () => screen.SelectedStageIndex == 0);
        AddAssert("screen stack unchanged", () => Stack.CurrentScreen, () => Is.SameAs(screen));
        AddAssert("first stage score selected", () => screen.SelectedScore.Value, () => Is.SameAs(session.Stages[0].Score));
        AddUntilStep("native statistics shown", () => screen.ChildrenOfType<StatisticsPanel>()
                                                              .Single(panel => panel is not BmsCourseAggregateStatistics).State.Value == Visibility.Visible);
        AddUntilStep("native score card hidden", () => screen.ChildrenOfType<ScorePanel>().All(panel => !panel.IsPresent));
        AddAssert("score replay disabled", () => screen.AllowWatchingReplay, () => Is.False);
        AddAssert("score retry disabled", () => screen.AllowRetry, () => Is.False);

        AddStep("toggle first stage back to course summary", () => this.ChildrenOfType<OsuClickableContainer>()
                                                                          .Single(row => row.Name == "Course stage 1 result")
                                                                          .TriggerClick());
        AddUntilStep("course summary restored from first stage", () => screen.SelectedStageIndex == null
                                                                       && screen.ChildrenOfType<BmsCourseAggregateStatistics>().Single().State.Value == Visibility.Visible);

        AddStep("open second stage directly", () => this.ChildrenOfType<OsuClickableContainer>()
                                                         .Single(row => row.Name == "Course stage 2 result")
                                                         .TriggerClick());
        AddUntilStep("second stage selected directly", () => screen.SelectedStageIndex == 1
                                                               && screen.SelectedScore.Value == session.Stages[1].Score
                                                               && screen.ChildrenOfType<StatisticsPanel>()
                                                                        .Single(panel => panel is not BmsCourseAggregateStatistics)
                                                                        .State.Value == Visibility.Visible);

        AddStep("switch from second stage to first stage", () => this.ChildrenOfType<OsuClickableContainer>()
                                                                        .Single(row => row.Name == "Course stage 1 result")
                                                                        .TriggerClick());
        AddUntilStep("first stage selected directly", () => screen.SelectedStageIndex == 0
                                                              && screen.SelectedScore.Value == session.Stages[0].Score
                                                              && screen.ChildrenOfType<StatisticsPanel>()
                                                                       .Single(panel => panel is not BmsCourseAggregateStatistics)
                                                                       .State.Value == Visibility.Visible);

    }

    private static BmsCourseSession createAbortedSession()
    {
        BeatmapInfo[] beatmaps =
        [
            createBeatmap("stage-1", "First Stage"),
            createBeatmap("stage-2", "Second Stage"),
            createBeatmap("stage-3", "Third Stage"),
            createBeatmap("stage-4", "Fourth Stage"),
        ];
        var definitions = beatmaps.Select((beatmap, index) => new BmsCourseStage(
            beatmap.Metadata?.Title ?? $"Stage {index + 1}",
            $"sl{index + 7}",
            BeatmapHash: beatmap.Hash)).ToArray();
        var resolvedStages = definitions.Zip(beatmaps, (definition, beatmap) => new BmsResolvedCourseStage(definition, beatmap)).ToArray();
        var course = new BmsCourseDefinition("visual-course", "Visual Table", "Visual Course", definitions, "Class", []);
        var session = new BmsCourseSession(
            course,
            resolvedStages,
            [new BmsModMirror(), new BmsModClassGauge()],
            BmsGaugeType.Class);

        session.BeginCurrentStage();
        session.CompleteCurrentStage(createScore(beatmaps[0], true, 765432, 0.91), 0.72);
        session.RequestAdvance();
        session.Advance();
        session.BeginCurrentStage();
        session.AbortCurrentStage(createScore(beatmaps[1], false, 321000, 0.63), 0.28);

        return session;
    }

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
        Date = new System.DateTimeOffset(2026, 8, 14, 0, 54, 0, System.TimeSpan.Zero),
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

    private static RoundedButton button(Drawable root, string text) =>
        root.ChildrenOfType<RoundedButton>().SingleOrDefault(candidate => candidate.Text.ToString() == text);

    private void assertText(string text) =>
        AddUntilStep($"{text} shown", () => this.ChildrenOfType<OsuSpriteText>()
                                               .Any(spriteText => spriteText.Text.ToString() == text));
}
