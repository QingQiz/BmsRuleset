using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.Toolbar;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Scoring;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsCourseSelect : ScreenTestScene
{
    private RealmRulesetStore rulesets = null!;
    private BeatmapManager beatmaps = null!;
    private ScoreManager scoreManager = null!;
    private RealmDetachedBeatmapStore beatmapStore = null!;
    private OsuConfigManager config = null!;
    private osu.Game.Screens.Select.SongSelect songSelect = null!;

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

    [TearDown]
    public void TearDown() => BmsRulesetRuntime.CourseCatalog.Clear();

    public override void SetUpSteps()
    {
        base.SetUpSteps();

        AddStep("configure BMS courses", () =>
        {
            BmsCourseSongSelectPatcher.InstallOnce();
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

    [Test]
    public void TestCourseModeReusesSongSelectLayout()
    {
        var originalFilterTopLeft = Vector2.Zero;
        var originalFilterTopRight = Vector2.Zero;
        var originalFilterWidth = 0f;

        AddStep("load real song select", () => Stack.Push(songSelect = new SoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for filtering", () => !carousel.IsFiltering);

        AddUntilStep("course controller attached", () => songSelect.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault() != null);
        AddAssert("course title starts offscreen left", () => songSelect.ChildrenOfType<BmsCourseTitleWedge>().Single().X, () => Is.EqualTo(-150));
        AddAssert("course details start offscreen left", () => songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().X, () => Is.EqualTo(-150));
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
            songSelect.ChildrenOfType<BmsCourseTitleWedge>().Single().FinishTransforms(true);
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().FinishTransforms(true);
            songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().FinishTransforms(true);
        });

        AddAssert("random disabled in course mode", () => !this.ChildrenOfType<FooterButtonRandom>().Single().Enabled.Value);
        AddAssert("course carousel uses beatmap carousel host", () => controller.CourseCarousel.Parent == carousel.Parent);
        AddAssert("course carousel matches song select width", () =>
            Math.Abs(controller.CourseCarousel.ScreenSpaceDrawQuad.Width - carousel.ScreenSpaceDrawQuad.Width), () => Is.LessThan(0.5f));
        AddAssert("course carousel matches song select position", () =>
            Math.Abs(controller.CourseCarousel.ScreenSpaceDrawQuad.AABBFloat.Left - carousel.ScreenSpaceDrawQuad.AABBFloat.Left), () => Is.LessThan(0.5f));
        AddAssert("course title uses title wedge host", () =>
            songSelect.ChildrenOfType<BmsCourseTitleWedge>().Single().Parent?.Parent
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
        AddUntilStep("four nested stage panels shown in carousel", () =>
            controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>().Count(), () => Is.EqualTo(4));
        AddAssert("stage panels removed from filter card", () =>
            songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ChildrenOfType<BmsCourseStagePanel>(), () => Is.Empty);
        AddAssert("stage panels removed from details tab", () =>
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().ChildrenOfType<BmsCourseStagePanel>(), () => Is.Empty);
        AddAssert("stage panels removed from title wedge", () =>
            songSelect.ChildrenOfType<BmsCourseTitleWedge>().Single().ChildrenOfType<BmsCourseStagePanel>(), () => Is.Empty);
        AddAssert("only selected course stages visible", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.IsVisible && item.Model is BmsGroupedCourseStage), () => Is.EqualTo(4));
        AddAssert("all course stages retained in carousel", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.Model is BmsGroupedCourseStage), () => Is.EqualTo(12));
        AddAssert("selected course is expanded", () => controller.CourseCarousel.GetCarouselItems()?
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == controller.SelectedCourse?.Id).IsExpanded == true);
        AddAssert("stage panels include local ranks", () =>
            controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>()
                      .All(panel => panel.ChildrenOfType<PanelLocalRankDisplay>().Count() == 1));
        AddAssert("stage panel text is not italic", () =>
            controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>()
                      .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>()).All(text => !text.Font.Italics));
        AddAssert("stage panels follow selected course card", () =>
        {
            var selectedPanel = controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
                                          .Single(panel => panel.Item?.Model is BmsGroupedCourse grouped
                                                           && grouped.Course.Id == controller.SelectedCourse?.Id);
            return controller.CourseCarousel.ChildrenOfType<BmsCourseStagePanel>()
                             .Min(panel => panel.ScreenSpaceDrawQuad.AABBFloat.Top)
                   >= selectedPanel.ScreenSpaceDrawQuad.AABBFloat.Bottom - 0.5f;
        });
        AddAssert("table group displays mark", () => controller.CourseCarousel.GetCarouselItems()?
            .Any(item => item.Model is BmsCourseTableGroup { TableName: "Satellite", Mark: "sl" }) == true);
        AddAssert("table group renders mark", () => controller.CourseCarousel.ChildrenOfType<BmsCourseTablePanel>()
            .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>())
            .Any(text => text.Text.ToString() == "sl  Satellite"));
        AddAssert("course cards include lamp state", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .All(panel => panel.ChildrenOfType<BmsLampDisplay>().Count() == 1));
        AddAssert("course cards include rank markers", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .All(panel => panel.ChildrenOfType<UpdateableRank>().Count() == 1));
        AddAssert("course cards omit stage count text", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .SelectMany(panel => panel.ChildrenOfType<OsuSpriteText>())
            .All(text => text.Text.ToString() != "4 stages"));
        AddStep("record selected course clear", () => BmsRulesetRuntime.CourseResults?.Record("satellite-7", BmsCourseStatus.Passed, ScoreRank.S));
        AddUntilStep("selected course lamp updates", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7")
            .ChildrenOfType<BmsLampDisplay>().Single().Lamp, () => Is.EqualTo(BmsLamp.Clear));
        AddUntilStep("selected course rank updates", () => controller.CourseCarousel.ChildrenOfType<BmsCoursePanel>()
            .Single(panel => panel.Item?.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-7")
            .ChildrenOfType<UpdateableRank>().Single().Rank, () => Is.EqualTo(ScoreRank.S));
        AddStep("press right to select next course", () => InputManager.Key(Key.Right));
        AddUntilStep("next course selected by keyboard", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddStep("press left to select previous course", () => InputManager.Key(Key.Left));
        AddUntilStep("previous course selected by keyboard", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("activate nested stage card", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .First(item => item.IsVisible && item.Model is BmsGroupedCourseStage)));
        AddAssert("nested stage cannot change selection", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-7"));
        AddStep("select second course", () => controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
            .Single(item => item.Model is BmsGroupedCourse grouped && grouped.Course.Id == "satellite-8")));
        AddUntilStep("second course selected", () => controller.SelectedCourse?.Id, () => Is.EqualTo("satellite-8"));
        AddAssert("second course stages expanded", () => controller.CourseCarousel.GetCarouselItems()?
            .Where(item => item.IsVisible && item.Model is BmsGroupedCourseStage)
            .All(item => item.Model is BmsGroupedCourseStage stage && stage.Course.Id == "satellite-8") == true);
        AddAssert("course carousel starts below filter card", () =>
            controller.CourseCarousel.ScreenSpaceDrawQuad.AABBFloat.Top
            >= songSelect.ChildrenOfType<BmsCourseFilterControl>().Single().ScreenSpaceDrawQuad.AABBFloat.Bottom - 0.5f);
        AddAssert("course title finishes at song select position", () =>
            songSelect.ChildrenOfType<BmsCourseTitleWedge>().Single().X, () => Is.Zero);
        AddAssert("course details finish at song select position", () =>
            songSelect.ChildrenOfType<BmsCourseDetailsArea>().Single().X, () => Is.Zero);
        AddAssert("two table groups displayed", () =>
            controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsCourseTableGroup), () => Is.EqualTo(2));
        AddAssert("three courses displayed", () =>
            controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsGroupedCourse), () => Is.EqualTo(3));
        AddAssert("three course models retained", () => controller.CourseCarousel.GetCarouselItems()?
            .Count(item => item.Model is BmsGroupedCourse), () => Is.EqualTo(3));
        AddAssert("normal carousel hidden", () => carousel.Alpha, () => Is.Zero);

        AddStep("search second table", () => controller.SearchTerm.Value = "stella");
        AddUntilStep("search filtered", () => !controller.CourseCarousel.IsFiltering
                                                   && controller.CourseCarousel.GetCarouselItems()?.Count(item => item.Model is BmsGroupedCourse) == 1);
        AddAssert("matching table remains", () =>
            controller.CourseCarousel.GetCarouselItems()?.Any(item => item.Model is BmsCourseTableGroup { TableName: "Stella" }) == true);

        AddStep("return to song select", () => controller.HideCourseMode());
        AddAssert("normal carousel restored", () => carousel.Alpha, () => Is.GreaterThan(0));
        AddAssert("random restored in song select", () => this.ChildrenOfType<FooterButtonRandom>().Single().Enabled.Value);
        AddAssert("course mode hidden", () => !controller.IsCourseMode);
    }

    private BmsCourseSongSelectController controller => songSelect.ChildrenOfType<BmsCourseSongSelectController>().Single();

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
            "Class",
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
            "Class",
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
            "ExClass",
            ["RANDOM"],
            TableMark: "st"),
    ];
}
