using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Statistics;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsCourseSelect : ScreenTestScene
{

    [TearDown]
    public void TearDown() => BmsRulesetRuntime.CourseCatalog.Clear();

    private static int nextTestId;

    private RealmRulesetStore rulesets = null!;
    private BeatmapManager beatmaps = null!;
    private ScoreManager scoreManager = null!;
    private RealmDetachedBeatmapStore beatmapStore = null!;
    private OsuConfigManager config = null!;
    private BmsSongSelect songSelect = null!;

    [Resolved]
    private Bindable<RulesetInfo> globalRuleset { get; set; } = null!;

    private BeatmapCarousel carousel => songSelect.ChildrenOfType<BeatmapCarousel>().Single();

    [Cached]
    private readonly OsuLogo logo;

    [Cached]
    private readonly VolumeOverlay volume;

    [Cached(typeof(INotificationOverlay))]
    private readonly INotificationOverlay notificationOverlay = new NotificationOverlay();

    public TestSceneBmsCourseSelect()
    {
        Children =
        [
            new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    new Toolbar
                    {
                        State = { Value = Visibility.Visible },
                    },
                    logo = new OsuLogo
                    {
                        Alpha = 0,
                    },
                    volume = new VolumeOverlay(),
                ],
            },
        ];

        Stack.Padding = new MarginPadding { Top = Toolbar.HEIGHT };
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
        dependencies.Cache(Realm);
        dependencies.Cache(beatmaps = new BeatmapManager(LocalStorage, Realm, null, Dependencies.Get<AudioManager>(), Resources, Dependencies.Get<GameHost>(), Beatmap.Default));
        dependencies.Cache(config = new OsuConfigManager(LocalStorage));
        dependencies.Cache(scoreManager = new ScoreManager(rulesets, () => beatmaps, LocalStorage, Realm, API, config));
        dependencies.CacheAs<BeatmapStore>(beatmapStore = new RealmDetachedBeatmapStore());

        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(beatmapStore);
    }

    public override void SetUpSteps()
    {
        base.SetUpSteps();

        AddStep("configure BMS courses", () =>
        {
            BmsRulesetRuntime.CourseCatalog.Replace(createCourses());
            Ruleset.Value = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            Beatmap.SetDefault();
            SelectedMods.SetDefault();
            config.SetValue(OsuSetting.SongSelectSortingMode, SortMode.Title);
            config.SetValue(OsuSetting.SongSelectGroupMode, GroupMode.None);
        });

        AddStep("delete all scores", () => scoreManager.Delete());
        AddStep("delete all beatmaps", () => beatmaps.Delete());
    }

    private BmsCourseSongSelectController controller => songSelect.ChildrenOfType<BmsCourseSongSelectController>().Single();

    private static double spacingBetween(CarouselItem top, CarouselItem bottom) =>
        bottom.CarouselYPosition - top.CarouselYPosition - top.DrawHeight;

    private ScoreInfo createScore(BeatmapInfo beatmap, ScoreRank rank, long totalScore, Dictionary<HitResult, int> statistics, Mod[] mods) => new()
    {
        User = API.LocalUser.Value,
        BeatmapInfo = beatmap,
        BeatmapHash = beatmap.Hash,
        Ruleset = beatmap.Ruleset,
        Rank = rank,
        Mods = mods,
        TotalScore = totalScore,
        Date = DateTimeOffset.Now,
        Accuracy = 1,
        MaxCombo = 1,
        Statistics = statistics,
    };

    private static BmsCourseAttemptData createCourseAttempt(ScoreInfo score, BeatmapInfo beatmap, Mod[] mods) => new()
    {
        Status = BmsCourseStatus.Passed,
        GaugeType = BmsGaugeType.Class,
        ModAcronyms = mods.Select(mod => mod.Acronym).ToArray(),
        Stages =
        [
            new BmsCourseStageAttemptData
            {
                BeatmapHash = beatmap.Hash,
                Status = BmsCourseStageStatus.Passed,
                ScoreId = score.ID,
                EndingHealth = 0.8,
            },
        ],
    };

    private static BeatmapSetInfo createBeatmapSet(RulesetInfo ruleset)
    {
        var id = nextTestId++;
        var hash = Guid.NewGuid().ToString("N");
        var metadata = new BeatmapMetadata
        {
            Artist = "Course Test Artist",
            Title = "Course Test Song",
            Author =
            {
                Username = "Course Test Author",
            },
        };
        var set = new BeatmapSetInfo
        {
            OnlineID = id + 1,
            Hash = Guid.NewGuid().ToString(),
            DateAdded = DateTimeOffset.UtcNow,
        };

        set.Beatmaps.Add(new BeatmapInfo(ruleset)
        {
            OnlineID = (id + 1) * 1000,
            BeatmapSet = set,
            DifficultyName = "Course Test Difficulty",
            StarRating = 1,
            Length = 30_000,
            BPM = 150,
            Hash = hash,
            MD5Hash = hash,
            Metadata = metadata,
            Difficulty = new BeatmapDifficulty
            {
                OverallDifficulty = 8,
            },
        });

        return set;
    }

    private static BmsCourseDefinition[] createCourses() =>
    [
        new(
            "satellite-7",
            "Satellite",
            "七段",
            [
                new BmsCourseStage("Blue Horizon", "sl7"),
                new BmsCourseStage("Quartz Signal", "sl8"),
                new BmsCourseStage("Afterimage", "sl8"),
                new BmsCourseStage("Terminal", "sl9"),
            ],
            [],
            TableMark: "sl"),
        new(
            "satellite-8",
            "Satellite",
            "八段",
            [
                new BmsCourseStage("Snowdrop", "sl9"),
                new BmsCourseStage("Orbit", "sl9"),
                new BmsCourseStage("Glass Engine", "sl10", false),
                new BmsCourseStage("Eventide", "sl10"),
            ],
            ["MIRROR"],
            TableMark: "sl"),
        new(
            "stella-1",
            "Stella",
            "Stella First Grade",
            [
                new BmsCourseStage("Light Years", "st0"),
                new BmsCourseStage("Convergence", "st0"),
                new BmsCourseStage("Resonance", "st1"),
                new BmsCourseStage("Asterism", "st1"),
            ],
            ["RANDOM"],
            TableMark: "st"),
    ];

    [Test]
    public void TestCourseModeReusesSongSelectLayout()
    {
        var originalFilterTopLeft = Vector2.Zero;
        var originalFilterTopRight = Vector2.Zero;
        var originalFilterWidth = 0f;
        var courseActivationCount = 0;

        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);

        AddUntilStep("course controller attached", () => songSelect.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault() != null);
        AddAssert("course carousel cannot receive input before course mode", () => !controller.CourseCarousel.IsPresent && controller.CourseCarousel.Alpha == 0);
        AddAssert("normal carousel host is not always present before course mode", () => controller.OriginalCarouselHostAlwaysPresent, () => Is.False);
        AddAssert("course title starts offscreen left", () => songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().X, () => Is.EqualTo(-150));
        AddAssert("course history starts offscreen left", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().X, () => Is.EqualTo(-150));
        AddAssert("course filter starts offscreen right", () => songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().X, () => Is.EqualTo(150));
        AddStep("capture song select filter bounds", () =>
        {
            var filter = songSelect.ChildrenOfType<FilterControl>().Single();
            filter.FinishTransforms(true);
            originalFilterTopLeft = filter.ScreenSpaceDrawQuad.TopLeft;
            originalFilterTopRight = filter.ScreenSpaceDrawQuad.TopRight;
            originalFilterWidth = filter.ScreenSpaceDrawQuad.Width;
        });
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                       && !controller.CourseCarousel.IsFiltering);
        AddStep("finish course mode transition", () =>
        {
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().FinishTransforms(true);
            songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().FinishTransforms(true);
            songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().FinishTransforms(true);
        });

        AddAssert("random disabled in course mode", () => !this.ChildrenOfType<FooterButtonRandom>().Single().Enabled.Value);
        AddAssert("course carousel shares the beatmap carousel outer host", () => controller.CourseCarousel.Parent == carousel.Parent?.Parent);
        AddAssert("course carousel matches song select width", () =>
            Math.Abs(controller.CourseCarousel.ScreenSpaceDrawQuad.Width - carousel.ScreenSpaceDrawQuad.Width), () => Is.LessThan(0.5f));
        AddAssert("course carousel matches song select position", () =>
            Math.Abs(controller.CourseCarousel.ScreenSpaceDrawQuad.AABBFloat.Left - carousel.ScreenSpaceDrawQuad.AABBFloat.Left), () => Is.LessThan(0.5f));
        AddAssert("course title uses title wedge host", () =>
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().Parent?.Parent
            == songSelect.ChildrenOfType<BeatmapTitleWedge>().Single().Parent?.Parent);
        AddAssert("course filter uses filter host", () =>
            songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().Parent
            == songSelect.ChildrenOfType<FilterControl>().Single().Parent);
        AddAssert("course filter matches song select width", () =>
            Math.Abs(songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ScreenSpaceDrawQuad.Width - originalFilterWidth), () => Is.LessThan(0.5f));
        AddAssert("course filter matches song select top-left", () =>
            (songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ScreenSpaceDrawQuad.TopLeft - originalFilterTopLeft).Length, () => Is.LessThan(0.5f));
        AddAssert("course filter matches song select top-right", () =>
            (songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ScreenSpaceDrawQuad.TopRight - originalFilterTopRight).Length, () => Is.LessThan(0.5f));
        AddUntilStep("four unavailable stage panels shown in carousel", () =>
            controller.CourseCarousel.ChildrenOfType<BmsUnavailableBeatmapPanel>().Count(), () => Is.EqualTo(4));
        AddAssert("unavailable stage panels reuse difficulty table panel", () =>
            controller.CourseCarousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
                .All(panel => panel.Item?.Model is BmsGroupedCourseStage { Beatmap: null }));
        AddAssert("stage panels removed from filter card", () =>
            songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ChildrenOfType<BmsUnavailableBeatmapPanel>(), () => Is.Empty);
        AddAssert("stage panels removed from history area", () =>
            songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().ChildrenOfType<BmsUnavailableBeatmapPanel>(), () => Is.Empty);
        AddAssert("stage panels removed from title wedge", () =>
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().ChildrenOfType<BmsUnavailableBeatmapPanel>(), () => Is.Empty);
        AddAssert("only selected course stages visible", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.IsVisible && item.Model is BmsGroupedCourseStage), () => Is.EqualTo(4));
        AddAssert("all course stages retained in carousel", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.Model is BmsGroupedCourseStage), () => Is.EqualTo(12));
        AddAssert("selected course is expanded", () => controller.CourseCarousel.GetCarouselItems()?
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == controller.SelectedCourse?.Id).IsExpanded == true);
        AddAssert("stage panel text is not italic", () =>
            controller.CourseCarousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
                .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>()).All(text => !text.Font.Italics));
        AddAssert("stage panels follow selected course card", () =>
        {
            var items = controller.CourseCarousel.GetCarouselItems()!;
            var selectedCourse = items.Single(item => item.Model is BmsGroupedCourse grouped
                                                      && grouped.Course.Id == controller.SelectedCourse?.Id);
            var firstStage = items.First(item => item.Model is BmsGroupedCourseStage stage
                                                 && stage.Course.Id == controller.SelectedCourse?.Id);
            return firstStage.CarouselYPosition > selectedCourse.CarouselYPosition;
        });
        AddAssert("selected course overlaps first stage", () =>
        {
            var items = controller.CourseCarousel.GetCarouselItems()!;
            var course = items.Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7");
            var firstStage = items.Single(item => item.Model is BmsGroupedCourseStage { Course.Id: "satellite-7", StageIndex: 0 });
            return spacingBetween(course, firstStage);
        }, () => Is.EqualTo(-BeatmapCarousel.SPACING).Within(0.01f));
        AddAssert("selected course stages overlap like song select cards", () =>
        {
            var stages = controller.CourseCarousel.GetCarouselItems()!
                .Where(item => item.Model is BmsGroupedCourseStage { Course.Id: "satellite-7" })
                .ToArray();
            return stages.Zip(stages.Skip(1)).All(pair => spacingBetween(pair.First, pair.Second) == -BeatmapCarousel.SPACING);
        });
        AddAssert("selected course stages have song select spacing before next course", () =>
        {
            var items = controller.CourseCarousel.GetCarouselItems()!;
            var lastStage = items.Single(item => item.Model is BmsGroupedCourseStage { Course.Id: "satellite-7", StageIndex: 3 });
            var nextCourse = items.Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-8");
            return spacingBetween(lastStage, nextCourse) == BeatmapCarousel.SPACING * 2;
        });
        AddAssert("table group displays mark", () => controller.CourseCarousel.GetCarouselItems()?
            .Any(item => item.Model is BmsCourseTableGroup { TableName: "Satellite", Mark: "sl" }) == true);
        AddAssert("table group renders mark", () => controller.CourseCarousel.ChildrenOfType<BmsCourseTablePanel>()
            .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>())
            .Any(text => text.Text.ToString() == "Satellite (sl)"));
        AddAssert("course cards include lamp state", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .All(panel => panel.ChildrenOfType<BmsLampDisplay>().Count() == 1));
        AddAssert("course cards include rank markers", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .All(panel => panel.ChildrenOfType<UpdateableRank>().Count() == 1));
        AddAssert("course cards use compact height", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .All(panel => panel.DrawHeight == PanelGroup.HEIGHT && panel.Item?.DrawHeight == PanelGroup.HEIGHT));
        AddAssert("course card text is vertically centred", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>().All(panel =>
        {
            var textBounds = panel.ChildrenOfType<OsuSpriteText>()
                .Select(text => text.ScreenSpaceDrawQuad.AABBFloat)
                .Aggregate(RectangleF.Union);
            var panelBounds = panel.ScreenSpaceDrawQuad.AABBFloat;
            return Math.Abs(textBounds.Centre.Y - panelBounds.Centre.Y) < 1;
        }));
        AddAssert("course card marks missing stages", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse { Course.Id: "satellite-8" })
            .ChildrenOfType<SpriteIcon>().Any(icon => icon.Icon.Equals(FontAwesome.Solid.ExclamationTriangle)));
        AddAssert("course cards omit stage count text", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>())
            .All(text => text.Text.ToString() != "4 stages"));
        AddAssert("history tabs removed", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<OsuSpriteText>()
            .All(text => text.Text.ToString() is not "Details" and not "History"));
        AddStep("record aborted course", () => BmsRulesetRuntime.CourseResults?.Record("satellite-8", BmsCourseStatus.Aborted, ScoreRank.F));
        AddAssert("aborted course has no lamp", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse { Course.Id: "satellite-8" })
            .ChildrenOfType<BmsLampDisplay>().Single().Lamp, () => Is.EqualTo(BmsLamp.NoPlay));
        AddAssert("aborted course has no rank", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse { Course.Id: "satellite-8" })
            .ChildrenOfType<UpdateableRank>().Single().Alpha, () => Is.Zero);
        AddAssert("first course selected", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("record course summary without stage history", () => BmsRulesetRuntime.CourseResults?.Record("satellite-7", BmsCourseStatus.Passed, ScoreRank.S));
        AddUntilStep("course lamp stays hidden without history", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7")
            .ChildrenOfType<BmsLampDisplay>().Single().Lamp, () => Is.EqualTo(BmsLamp.NoPlay));
        AddAssert("course rank stays hidden without history", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7")
            .ChildrenOfType<UpdateableRank>().Single().Alpha, () => Is.Zero);
        AddStep("press down to select next course", () => InputManager.Key(Key.Down));
        AddUntilStep("down skips expanded stages", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddAssert("down does not activate course", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("press down to wrap current table", () => InputManager.Key(Key.Down));
        AddUntilStep("down wraps to first course", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddAssert("second down does not activate course", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("press up to wrap current table", () => InputManager.Key(Key.Up));
        AddUntilStep("up wraps to last course", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddStep("press up to select first course", () => InputManager.Key(Key.Up));
        AddUntilStep("up skips expanded stages", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("shift right to next table", () =>
        {
            InputManager.PressKey(Key.ShiftLeft);
            InputManager.Key(Key.Right);
            InputManager.ReleaseKey(Key.ShiftLeft);
        });
        AddUntilStep("next table expanded", () => controller.CourseCarousel.ExpandedGroup?.TableName, () => Is.EqualTo("Stella"));
        AddAssert("next table header has keyboard focus", () => controller.CourseCarousel.KeyboardSelectedGroup?.TableName, () => Is.EqualTo("Stella"));
        AddAssert("group traversal does not activate course", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("shift left to previous table", () =>
        {
            InputManager.PressKey(Key.ShiftLeft);
            InputManager.Key(Key.Left);
            InputManager.ReleaseKey(Key.ShiftLeft);
        });
        AddUntilStep("previous table expanded", () => controller.CourseCarousel.ExpandedGroup?.TableName, () => Is.EqualTo("Satellite"));
        AddUntilStep("selected course regains keyboard focus", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("shift enter to collapse current table", () =>
        {
            InputManager.PressKey(Key.ShiftLeft);
            InputManager.Key(Key.Enter);
            InputManager.ReleaseKey(Key.ShiftLeft);
        });
        AddUntilStep("current table collapsed", () => controller.CourseCarousel.ExpandedGroup, () => Is.Null);
        AddAssert("shift enter does not activate course", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("shift enter to expand current table", () =>
        {
            InputManager.PressKey(Key.ShiftLeft);
            InputManager.Key(Key.Enter);
            InputManager.ReleaseKey(Key.ShiftLeft);
        });
        AddUntilStep("current table expanded", () => controller.CourseCarousel.ExpandedGroup?.TableName, () => Is.EqualTo("Satellite"));
        AddUntilStep("selected course focused after expanding table", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("move keyboard selection to next course", () => InputManager.Key(Key.Down));
        AddUntilStep("next course highlighted", () => controller.CourseCarousel.KeyboardSelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddAssert("highlighted course is not active", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("confirm highlighted course", () => InputManager.Key(Key.Enter));
        AddUntilStep("highlighted course activated", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddStep("track course activation", () => controller.StartRequested = () => courseActivationCount++);
        AddStep("enter activates selected course", () => InputManager.Key(Key.Enter));
        AddAssert("selected course activated once", () => courseActivationCount, () => Is.EqualTo(1));
        AddStep("control enter activates selected course", () =>
        {
            InputManager.PressKey(Key.ControlLeft);
            InputManager.Key(Key.Enter);
            InputManager.ReleaseKey(Key.ControlLeft);
        });
        AddAssert("control enter uses carousel activation", () => courseActivationCount, () => Is.EqualTo(2));
        AddStep("restore first course", () => InputManager.Key(Key.Left));
        AddUntilStep("first course restored", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("press right to select next course", () => InputManager.Key(Key.Right));
        AddUntilStep("next course selected by keyboard", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddAssert("history hidden without a result", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().State.Value, () => Is.EqualTo(Visibility.Hidden));
        AddStep("press left to select previous course", () => InputManager.Key(Key.Left));
        AddUntilStep("previous course selected by keyboard", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddAssert("history stays hidden without a score", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().State.Value, () => Is.EqualTo(Visibility.Hidden));
        AddStep("activate nested stage card", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .First(item => item.IsVisible && item.Model is BmsGroupedCourseStage)));
        AddAssert("nested stage cannot change selection", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("select second course", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-8")));
        AddUntilStep("second course selected", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddAssert("history hidden without a result", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().State.Value, () => Is.EqualTo(Visibility.Hidden));
        AddAssert("second course stages expanded", () => controller.CourseCarousel.GetCarouselItems()?
            .Where(item => item.IsVisible && item.Model is BmsGroupedCourseStage)
            .All(item => item.Model is BmsGroupedCourseStage stage && stage.Course.Id == "satellite-8") == true);
        AddUntilStep("selected course has song select spacing above", () =>
        {
            var items = controller.CourseCarousel.GetCarouselItems()!;
            var previousCourse = items.Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7");
            var selectedCourse = items.Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-8");
            return spacingBetween(previousCourse, selectedCourse);
        }, () => Is.EqualTo(BeatmapCarousel.SPACING * 2).Within(0.01f));
        AddStep("select course in second table", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "stella-1")));
        AddUntilStep("second table course selected", () => controller.SelectedCourse?.Id, () => Is.EqualTo("stella-1"));
        AddAssert("previous table courses collapsed", () => controller.CourseCarousel.GetCarouselItems()?
            .Where(item => item.Model is BmsGroupedCourse { Group.TableName: "Satellite" })
            .All(item => !item.IsVisible) == true);
        AddAssert("selected table courses expanded", () => controller.CourseCarousel.GetCarouselItems()?
            .Where(item => item.Model is BmsGroupedCourse { Group.TableName: "Stella" })
            .All(item => item.IsVisible) == true);
        AddAssert("course carousel starts below filter card", () =>
            controller.CourseCarousel.ScreenSpaceDrawQuad.AABBFloat.Top
            >= songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ScreenSpaceDrawQuad.AABBFloat.Bottom - 0.5f);
        AddAssert("course title finishes at song select position", () =>
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().X, () => Is.Zero);
        AddAssert("history stays hidden for course without result", () =>
            songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().State.Value, () => Is.EqualTo(Visibility.Hidden));
        AddAssert("two table groups displayed", () =>
            controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsCourseTableGroup), () => Is.EqualTo(2));
        AddAssert("three courses displayed", () =>
            controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsGroupedCourse), () => Is.EqualTo(3));
        AddAssert("three course models retained", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.Model is BmsGroupedCourse), () => Is.EqualTo(3));
        AddAssert("normal carousel is visually hidden by its host", () => controller.OriginalCarouselHostAlpha, () => Is.Zero);
        AddAssert("normal carousel is not present while course mode is visible", () => carousel.IsPresent, () => Is.False);
        AddAssert("normal carousel input is blocked while course mode is visible", () => controller.OriginalCarouselAcceptsInput, () => Is.False);
        AddAssert("normal carousel host is not present in course mode", () => controller.OriginalCarouselHostAlwaysPresent, () => Is.False);

        AddStep("search second table", () => controller.SearchTerm.Value = "stella");
        AddUntilStep("search filtered", () => !controller.CourseCarousel.IsFiltering
                                              && controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsGroupedCourse) == 1);
        AddAssert("matching table remains", () =>
            controller.CourseCarousel.GetCarouselItems()?.Any(item => item.Model is BmsCourseTableGroup { TableName: "Stella" }) == true);

        AddStep("simulate song select resuming after gameplay", () =>
        {
            songSelect.ChildrenOfType<BeatmapTitleWedge>().Single().Show();
            songSelect.ChildrenOfType<BmsBeatmapDetailsArea>().Single().Show();
            songSelect.ChildrenOfType<FilterControl>().Single().Show();
        });
        AddUntilStep("course mode keeps resumed controls hidden", () =>
            songSelect.ChildrenOfType<BeatmapTitleWedge>().Single().State.Value == Visibility.Visible
            && songSelect.ChildrenOfType<BeatmapTitleWedge>().Single().Alpha == 0
            && songSelect.ChildrenOfType<BmsBeatmapDetailsArea>().Single().State.Value == Visibility.Visible
            && songSelect.ChildrenOfType<BmsBeatmapDetailsArea>().Single().Alpha == 0
            && songSelect.ChildrenOfType<FilterControl>().Single().State.Value == Visibility.Visible
            && songSelect.ChildrenOfType<FilterControl>().Single().Alpha == 0);
    }

    [Test]
    public void TestModOverlayBackDoesNotExitCourseMode()
    {
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course mode visible", () => controller.IsCourseMode);
        AddStep("show mod overlay", () => songSelect.ModSelectOverlay.Show());
        AddUntilStep("mod overlay visible", () => songSelect.ModSelectOverlay.State.Value == Visibility.Visible);
        AddStep("press escape", () => InputManager.Key(Key.Escape));
        AddUntilStep("mod overlay hidden", () => songSelect.ModSelectOverlay.State.Value != Visibility.Visible);
        AddAssert("course mode remains visible", () => controller.IsCourseMode);
    }

    [Test]
    public void TestRulesetSwitchReplacesSongSelectImplementation()
    {
        TestParentScreen parentScreen = null!;
        RulesetInfo originalRuleset = null!;
        ToolbarRulesetSelector rulesetSelector = null!;

        AddStep("load parent screen", () => Stack.Push(parentScreen = new TestParentScreen()));
        AddUntilStep("wait for parent screen", () => Stack.CurrentScreen == parentScreen && parentScreen.IsLoaded);
        AddStep("load real song select", () =>
        {
            parentScreen.Push(songSelect = new BmsSoloSongSelect());
            BmsSongSelectEntryPatcher.TrackRulesetChanges(songSelect);
        });
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course mode visible", () => controller.IsCourseMode);
        AddStep("switch away from BMS", () =>
        {
            originalRuleset = globalRuleset.Value;
            rulesetSelector = this.ChildrenOfType<ToolbarRulesetSelector>().Single();
            rulesetSelector.Current.UnbindBindings();
            globalRuleset.Value = new AlternateTestRuleset().RulesetInfo;
        });
        AddUntilStep("upstream song select loaded", () => Stack.CurrentScreen is SoloSongSelect upstream && upstream.IsLoaded && upstream.IsCurrentScreen());
        AddAssert("alternate ruleset remains selected", () => globalRuleset.Value.ShortName, () => Is.EqualTo("other"));
        AddStep("switch back to BMS", () => globalRuleset.Value = originalRuleset);
        AddUntilStep("BMS song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect bms && bms.IsLoaded && bms.IsCurrentScreen());
        AddStep("restore toolbar binding", () =>
        {
            rulesetSelector.Current.BindTo(globalRuleset);
            songSelect = (BmsSoloSongSelect)Stack.CurrentScreen;
        });
    }

    [Test]
    public void TestCourseModeSwitchReplacesSongSelectImplementation()
    {
        TestParentScreen parentScreen = null!;
        BmsSoloSongSelect normalSongSelect = null!;
        BmsSoloSongSelect courseSongSelect = null!;
        BmsSoloSongSelect restoredNormalSongSelect = null!;

        AddStep("load parent screen", () => Stack.Push(parentScreen = new TestParentScreen()));
        AddUntilStep("wait for parent screen", () => Stack.CurrentScreen == parentScreen && parentScreen.IsLoaded);
        AddStep("load normal song select", () =>
        {
            parentScreen.Push(normalSongSelect = new BmsSoloSongSelect());
            BmsSongSelectEntryPatcher.TrackRulesetChanges(normalSongSelect);
            songSelect = normalSongSelect;
        });
        AddUntilStep("wait for normal song select", () => Stack.CurrentScreen == normalSongSelect && normalSongSelect.IsLoaded);
        AddStep("select user mod", () => normalSongSelect.Mods.Value = [new BmsModDoubleTime()]);
        AddStep("switch to course mode", () => controller.ToggleMode());
        AddUntilStep("course song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                         && current != normalSongSelect
                                                         && current.IsLoaded
                                                         && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == true);
        AddStep("capture course song select", () => songSelect = courseSongSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                       && !controller.CourseCarousel.IsFiltering);
        AddStep("select non-first course", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "stella-1")));
        AddUntilStep("non-first course selected", () => controller.SelectedCourse?.Id, () => Is.EqualTo("stella-1"));
        AddStep("return with escape", () => InputManager.Key(Key.Escape));
        AddUntilStep("fresh normal song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                                && current != courseSongSelect
                                                                && current.IsLoaded
                                                                && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == false);
        AddStep("capture restored normal song select", () => songSelect = restoredNormalSongSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddAssert("user mods restored", () => restoredNormalSongSelect.Mods.Value, () => Has.Exactly(1).TypeOf<BmsModDoubleTime>());
        AddStep("re-enter course mode", () => controller.ToggleMode());
        AddUntilStep("replacement course song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                                     && current != restoredNormalSongSelect
                                                                     && current.IsLoaded
                                                                     && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == true);
        AddStep("capture replacement course song select", () => songSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddUntilStep("course selection restored", () => controller.SelectedCourse?.Id, () => Is.EqualTo("stella-1"));
    }

    [Test]
    public void TestCourseStageCardsDisplayLocalLampAndRank()
    {
        BeatmapInfo beatmap = null!;
        ScoreInfo alternateScore = null!;
        BeatmapLeaderboardScore historyPanel = null!;

        AddStep("import course stage beatmap and scores", () =>
        {
            BmsRulesetRuntime.EnsureDifficultyTableStore(Dependencies.Get<GameHost>(), Realm);
            var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));

            Assert.That(imported, Is.Not.Null);
            beatmap = imported!.Value.Beatmaps.Single().Detach();

            var historicalScore = createScore(beatmap, ScoreRank.S, 900_000,
                new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = 1,
                    [HitResult.Ok] = 1,
                }, []);
            historicalScore.Accuracy = 0.9;
            alternateScore = createScore(beatmap, ScoreRank.A, 800_000,
                new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = 2,
                    [HitResult.Ok] = 1,
                }, [new BmsModMirror(), new BmsModHardGauge()]);
            Assert.That(scoreManager.Import(createScore(beatmap, ScoreRank.D, 100_000,
                new Dictionary<HitResult, int>
                {
                    [HitResult.Perfect] = 1,
                    [HitResult.Ok] = 1,
                }, [new BmsModHardGauge()])), Is.Not.Null);
            historicalScore = scoreManager.Import(historicalScore)!.Value.DeepClone();
            alternateScore = scoreManager.Import(alternateScore)!.Value.DeepClone();

            var course = new BmsCourseDefinition(
                "history-test",
                "History Test",
                "History Test Course",
                [new BmsCourseStage("History Test Song", "1", BeatmapHash: beatmap.Hash)],
                []);
            BmsRulesetRuntime.CourseCatalog.Replace([course]);
            BmsRulesetRuntime.CourseResults?.Record("history-test", BmsCourseStatus.Passed, historicalScore.Rank, historicalScore,
                createCourseAttempt(historicalScore, beatmap, [new BmsModClassGauge()]));
            BmsRulesetRuntime.CourseResults?.Record("history-test", BmsCourseStatus.Passed, alternateScore.Rank, alternateScore,
                createCourseAttempt(alternateScore, beatmap, [new BmsModMirror(), new BmsModClassGauge()]));
            config.SetValue(OsuSetting.BeatmapLeaderboardSortMode, LeaderboardSortMode.Score);
            config.SetValue(OsuSetting.BeatmapDetailModsFilter, false);
        });
        AddUntilStep("wait for course stage scores", () => Realm.Run(r =>
            r.All<ScoreInfo>().AsEnumerable().Count(score => score.BeatmapHash == beatmap.Hash && !score.DeletePending)), () => Is.EqualTo(3));
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                       && !controller.CourseCarousel.IsFiltering);
        AddAssert("stage beatmap resolved before panel materialisation", () => controller.CourseCarousel.GetCarouselItems()!
            .Select(item => item.Model)
            .OfType<BmsGroupedCourseStage>()
            .Single().Beatmap?.Hash, () => Is.EqualTo(beatmap.Hash));
        AddUntilStep("stage card realised", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().SingleOrDefault() != null);
        AddAssert("stage background retrieval is deferred", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single()
            .ChildrenOfType<PanelSetBackground>().Single().Beatmap, () => Is.Null);
        AddAssert("stage card hides BMS ruleset icon but keeps its slot", () =>
        {
            var icon = controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single()
                .ChildrenOfType<ConstrainedIconContainer>().First();
            return icon.Alpha == 0 && icon.IsPresent && icon.DrawWidth == 12;
        });
        AddAssert("stage card uses song select rating layout", () =>
        {
            var panel = controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single();
            return panel.ChildrenOfType<StarRatingDisplay>().Count() == 1
                   && panel.ChildrenOfType<BmsPanelBeatmapStandalone.SpreadDisplay>().Count() == 1;
        });
        AddUntilStep("current difficulty marker uses rating colour", () =>
        {
            var panel = controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single();
            var rating = panel.ChildrenOfType<StarRatingDisplay>().Single();
            var spread = panel.ChildrenOfType<BmsPanelBeatmapStandalone.SpreadDisplay>().Single();
            return ((Color4)spread.Current.Colour).Equals(rating.DisplayedDifficultyColour);
        });
        AddAssert("stage card uses song select metadata order", () =>
        {
            var texts = controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single()
                .ChildrenOfType<OsuSpriteText>()
                .Where(text => text.Alpha > 0)
                .Select(text => text.Text.ToString())
                .ToArray();
            return texts.Contains("Course Test Song")
                   && texts.Contains("Course Test Artist")
                   && texts.Contains("Course Test Difficulty")
                   && texts.All(text => !text.Contains("Course Test Author"))
                   && texts.All(text => text != "#1");
        });
        AddAssert("complete course keeps trophy icon", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>().Single()
            .ChildrenOfType<SpriteIcon>().Any(icon => icon.Icon.Equals(FontAwesome.Solid.Trophy)));
        AddUntilStep("stage card uses best local lamp", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single()
            .ChildrenOfType<BmsLampDisplay>().Single(display => display.Alpha > 0).Lamp, () => Is.EqualTo(BmsLamp.HardClear));
        AddUntilStep("stage card uses highest local rank", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single()
            .ChildrenOfType<UpdateableRank>().Single(rank => rank.Alpha > 0).Rank, () => Is.EqualTo(ScoreRank.S));
        AddUntilStep("course history keeps every aggregate score", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Count(), () => Is.EqualTo(2));
        AddUntilStep("course history uses BMS rank lettering", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().OrderByDescending(row => row.Score.TotalScore)
            .SelectMany(row => row.ChildrenOfType<OsuSpriteText>().Where(text => text.Font.Equals(osu.Game.Graphics.OsuFont.Numeric.With(size: 14))))
            .Select(text => text.Text.ToString()), () => Is.EqualTo(new[] { "AAA", "AAA" }));
        AddAssert("course history has song select controls without tabs", () =>
        {
            var history = songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single();
            var header = history.ChildrenOfType<BmsCourseHistoryHeader>().Single();
            return header.ChildrenOfType<ShearedDropdown<BeatmapLeaderboardScope>>().Single().Current.Value
                   == BeatmapLeaderboardScope.Local
                   && !header.FilterBySelectedMods.Value
                   && !history.ChildrenOfType<BeatmapDetailsArea.WedgeSelector<BeatmapDetailsArea.Header.Selection>>().Any();
        });
        AddAssert("course history sorts by score", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().OrderBy(score => score.Rank).First().Score.TotalScore, () => Is.EqualTo(900_000));
        AddStep("sort course history by accuracy", () => config.SetValue(OsuSetting.BeatmapLeaderboardSortMode, LeaderboardSortMode.Accuracy));
        AddUntilStep("course history applies sorting", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().OrderBy(score => score.Rank).First().Score.TotalScore, () => Is.EqualTo(800_000));
        AddStep("filter course history by selected mods", () => config.SetValue(OsuSetting.BeatmapDetailModsFilter, true));
        AddUntilStep("only no-mod history remains", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Count(), () => Is.EqualTo(1));
        AddAssert("no-mod history score is retained", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Single().Score.TotalScore, () => Is.EqualTo(900_000));
        AddStep("select mirror", () => songSelect.Mods.Value = [new BmsModMirror()]);
        AddUntilStep("selected mods history is replaced", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Single().Score.TotalScore, () => Is.EqualTo(800_000));
        AddAssert("selected mods filter ignores course gauge", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Single().Score.TotalScore, () => Is.EqualTo(800_000));
        AddAssert("course history preserves judgement statistics", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Single().Score.Statistics.GetValueOrDefault(HitResult.Perfect), () => Is.EqualTo(2));
        AddAssert("course history card remains sheared", () =>
        {
            var quad = songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
                .ChildrenOfType<BeatmapLeaderboardScore>().Single().ScreenSpaceDrawQuad;
            return Math.Abs(quad.TopLeft.X - quad.BottomLeft.X) > 1;
        });
        AddAssert("course history text is not sheared", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Single()
            .ChildrenOfType<OsuSpriteText>()
            .Where(text => text.IsPresent)
            .All(text => Math.Abs(text.ScreenSpaceDrawQuad.TopLeft.X - text.ScreenSpaceDrawQuad.BottomLeft.X) < 0.5f));
        AddStep("disable history mods filter", () => config.SetValue(OsuSetting.BeatmapDetailModsFilter, false));
        AddUntilStep("all history restored", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Count(), () => Is.EqualTo(2));
        AddStep("capture existing history panel", () => historyPanel = songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().First());
        AddStep("import unrelated score", () => Assert.That(scoreManager.Import(createScore(beatmap, ScoreRank.B, 200_000,
            new Dictionary<HitResult, int> { [HitResult.Perfect] = 1 }, [])), Is.Not.Null));
        AddUntilStep("unrelated score imported", () => Realm.Run(r =>
            r.All<ScoreInfo>().AsEnumerable().Count(score => score.BeatmapHash == beatmap.Hash && !score.DeletePending)), () => Is.EqualTo(4));
        AddWaitStep("allow realm notifications", 5);
        AddAssert("unrelated score does not rebuild course history", () => ReferenceEquals(historyPanel,
            songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single().ChildrenOfType<BeatmapLeaderboardScore>().First()));
        AddStep("open course history score", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().First(score => score.Score.TotalScore == 800_000).TriggerClick());
        AddUntilStep("course score details opened", () => Stack.CurrentScreen, Is.TypeOf<BmsCourseResultsScreen>);
        AddUntilStep("course summary is displayed", () => ((BmsCourseResultsScreen)Stack.CurrentScreen).ChildrenOfType<BmsCourseResultsLayout>().SingleOrDefault(), () => Is.Not.Null);
        AddAssert("course score details match selected history", () => ((BmsCourseResultsScreen)Stack.CurrentScreen)
                .ChildrenOfType<BmsResultOverview>().Single().Score.TotalScore,
            () => Is.EqualTo(800_000));
        AddStep("record course while song select is suspended", () => BmsRulesetRuntime.CourseResults?.Record(
            "history-test", BmsCourseStatus.Passed, alternateScore.Rank, alternateScore,
            createCourseAttempt(alternateScore, beatmap, [new BmsModMirror(), new BmsModClassGauge()])));
        AddWaitStep("allow suspended result notification", 5);
        AddAssert("suspended history is not rebuilt", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Count(), () => Is.EqualTo(2));
        AddStep("close course score details", () => Stack.CurrentScreen.Exit());
        AddUntilStep("song select resumed", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
        AddUntilStep("suspended result appears after resume", () => songSelect.ChildrenOfType<BmsCourseHistoryArea>().Single()
            .ChildrenOfType<BeatmapLeaderboardScore>().Count(), () => Is.EqualTo(3));
    }

    [Test]
    public void TestRemovingStricterModRestoresRequiredConstraintMod()
    {
        AddStep("configure a no-good course", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));
            Assert.That(imported, Is.Not.Null);
            var beatmap = imported!.Value.Beatmaps.First();

            BmsRulesetRuntime.CourseCatalog.Replace(
            [
                new BmsCourseDefinition(
                    "constraint-test",
                    "Constraint Table",
                    "No Good Course",
                    [new BmsCourseStage("Stage", "1", BeatmapHash: beatmap.Hash)],
                    ["no_good"]),
            ]);
        });
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                       && !controller.CourseCarousel.IsFiltering);
        AddUntilStep("required no-good applied", () => songSelect.Mods.Value.Any(mod => mod is BmsModNoGood));
        AddStep("select stricter no-great", () => songSelect.Mods.Value = [new BmsModNoGreat()]);
        AddUntilStep("no-great replaces no-good", () =>
            songSelect.Mods.Value.Any(mod => mod is BmsModNoGreat) && songSelect.Mods.Value.All(mod => mod is not BmsModNoGood));
        AddStep("remove no-great", () => songSelect.Mods.Value = []);
        AddUntilStep("no-good restored after stricter mod removal", () => songSelect.Mods.Value.Any(mod => mod is BmsModNoGood));
        AddStep("add mirror alongside constraint", () => songSelect.Mods.Value = [new BmsModNoGood(), new BmsModMirror()]);
        AddUntilStep("manual mirror kept with required no-good", () =>
            songSelect.Mods.Value.Any(mod => mod is BmsModMirror) && songSelect.Mods.Value.Any(mod => mod is BmsModNoGood));
        AddStep("remove everything", () => songSelect.Mods.Value = []);
        AddUntilStep("required no-good survives removal", () => songSelect.Mods.Value.Any(mod => mod is BmsModNoGood));
        AddStep("select no-great again", () => songSelect.Mods.Value = [new BmsModNoGreat()]);
        AddUntilStep("no-great replaces no-good again", () =>
            songSelect.Mods.Value.Any(mod => mod is BmsModNoGreat) && songSelect.Mods.Value.All(mod => mod is not BmsModNoGood));
        AddStep("clear mods", () => songSelect.Mods.Value = []);
        AddUntilStep("no-good restored after second removal", () => songSelect.Mods.Value.Any(mod => mod is BmsModNoGood));
    }

    [Test]
    public void TestSelectedCourseUpdatesAndRestoresPreviewBeatmap()
    {
        BeatmapInfo originalBeatmap = null!;
        BeatmapInfo[] previewBeatmaps = null!;
        TestParentScreen parentScreen = null!;
        BmsSoloSongSelect normalSongSelect = null!;
        BmsSoloSongSelect courseSongSelect = null!;

        AddStep("import original and course preview beatmaps", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            originalBeatmap = importBeatmap(createBeatmapSet(bmsRuleset));
            previewBeatmaps =
            [
                importBeatmap(createBeatmapSet(bmsRuleset)),
                importBeatmap(createBeatmapSet(bmsRuleset)),
                importBeatmap(createBeatmapSet(bmsRuleset)),
            ];

            BmsRulesetRuntime.CourseCatalog.Replace(
                previewBeatmaps.Select((beatmap, index) => new BmsCourseDefinition(
                    $"preview-test-{index}",
                    "Preview Table",
                    $"Preview Course {index}",
                    [new BmsCourseStage($"Preview {index}", "1", BeatmapHash: beatmap.Hash)],
                    [])));
        });
        AddStep("load parent screen", () => Stack.Push(parentScreen = new TestParentScreen()));
        AddUntilStep("wait for parent screen", () => Stack.CurrentScreen == parentScreen && parentScreen.IsLoaded);
        AddStep("load normal song select", () =>
        {
            parentScreen.Push(normalSongSelect = new BmsSoloSongSelect());
            BmsSongSelectEntryPatcher.TrackRulesetChanges(normalSongSelect);
            songSelect = normalSongSelect;
        });
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == normalSongSelect && normalSongSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("select original beatmap", () => songSelect.Beatmap.Value = beatmaps.GetWorkingBeatmap(originalBeatmap));
        AddUntilStep("original beatmap selected", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(originalBeatmap.Hash));
        AddStep("switch to course mode", () => controller.ToggleMode());
        AddUntilStep("course song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                         && current != normalSongSelect
                                                         && current.IsLoaded
                                                         && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == true);
        AddStep("capture course song select", () => songSelect = courseSongSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
        AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                       && !controller.CourseCarousel.IsFiltering);
        AddUntilStep("first course preview selected", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(previewBeatmaps[0].Hash));
        AddUntilStep("stage panel materialised", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().SingleOrDefault(), () => Is.Not.Null);
        AddAssert("stage panel is pooled", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Single().IsInPool);
        AddStep("rapidly select two courses", () =>
        {
            var courses = controller.CourseCarousel.GetCarouselItems()!
                .Where(item => item.Model is BmsGroupedCourse)
                .ToArray();
            controller.CourseCarousel.Activate(courses[1]);
            controller.CourseCarousel.Activate(courses[2]);
            Assert.That(songSelect.Beatmap.Value.BeatmapInfo.Hash, Is.EqualTo(previewBeatmaps[0].Hash));
        });
        AddUntilStep("only final course preview selected", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(previewBeatmaps[2].Hash));
        AddUntilStep("stage panel reset to final course", () => controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>()
            .SingleOrDefault()?.ResolvedBeatmap?.Hash, () => Is.EqualTo(previewBeatmaps[2].Hash));
        AddStep("return to normal song select", () => controller.ToggleMode());
        AddUntilStep("fresh normal song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                                && current != courseSongSelect
                                                                && current.IsLoaded
                                                                && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == false);
        AddUntilStep("original beatmap restored", () => ((BmsSoloSongSelect)Stack.CurrentScreen).Beatmap.Value.BeatmapInfo.Hash,
            () => Is.EqualTo(originalBeatmap.Hash));

        BeatmapInfo importBeatmap(BeatmapSetInfo set)
        {
            var imported = beatmaps.Import(set);
            Assert.That(imported, Is.Not.Null);
            return imported!.Value.Beatmaps.Single().Detach();
        }
    }

    [Test]
    public void TestSwitchingDistantCoursePreviewsDoesNotPopulateIntermediateWorkingBeatmaps()
    {
        const int beatmap_count = 120;

        BeatmapInfo[] importedBeatmaps = null!;
        ScheduledDelegate cacheTracker = null!;
        var baselineCacheCount = 0;
        var peakCacheCount = 0;

        AddStep("import distant course preview beatmaps", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            importedBeatmaps =
            [
                .. Enumerable.Range(0, beatmap_count).Select(index =>
                {
                    var set = createBeatmapSet(bmsRuleset);
                    set.Beatmaps.Single().Metadata.Title = $"Course Preview {index:D3}";
                    var imported = beatmaps.Import(set);
                    Assert.That(imported, Is.Not.Null);
                    return imported!.Value.Beatmaps.Single().Detach();
                }),
            ];

            BmsRulesetRuntime.CourseCatalog.Replace(
            [
                new BmsCourseDefinition(
                    "preview-first",
                    "Preview Table",
                    "First Preview",
                    [new BmsCourseStage("First", "1", BeatmapHash: importedBeatmaps[0].Hash)],
                    []),
                new BmsCourseDefinition(
                    "preview-last",
                    "Preview Table",
                    "Last Preview",
                    [new BmsCourseStage("Last", "1", BeatmapHash: importedBeatmaps[^1].Hash)],
                    []),
            ]);
        });
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("first course preview selected", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(importedBeatmaps[0].Hash));
        AddStep("start cache tracking", () =>
        {
            var cachedWorkingBeatmaps = GlobalStatistics.Get<int>("Beatmaps", $"Cached {nameof(WorkingBeatmap)}s");
            baselineCacheCount = peakCacheCount = cachedWorkingBeatmaps.Value;
            cacheTracker = Scheduler.AddDelayed(() => peakCacheCount = Math.Max(peakCacheCount, cachedWorkingBeatmaps.Value), 0, true);
        });
        AddStep("select distant course", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .Single(item => item.Model is BmsGroupedCourse { Course.Id: "preview-last" })));
        AddUntilStep("distant course preview selected", () => songSelect.Beatmap.Value.BeatmapInfo.Hash, () => Is.EqualTo(importedBeatmaps[^1].Hash));
        AddWaitStep("allow hidden carousel to settle", 120);
        AddStep("stop cache tracking", () => cacheTracker.Cancel());
        AddAssert("intermediate beatmaps were not cached", () => peakCacheCount - baselineCacheCount, () => Is.LessThanOrEqualTo(8));
    }

    private partial class TestParentScreen : OsuScreen
    {
    }

    public class AlternateTestRuleset : BmsRuleset
    {
        public override string Description => "Other";

        public override string ShortName => "other";
    }
}
