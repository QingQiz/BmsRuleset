using System.Linq;
using NUnit.Framework;
using osu.Framework.Statistics;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestCourseStartQueriesInBackgroundAndCancelsOnChanges(bool changeCourse)
    {
        AddStep("configure playable courses", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
            var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));
            Assert.That(imported, Is.Not.Null);
            var beatmap = imported!.Value.Beatmaps.Single();
            BmsRulesetRuntime.CourseCatalog.Replace(
            [
                new BmsCourseDefinition("first", "Table", "First", [new BmsCourseStage("Stage", "1", BeatmapHash: beatmap.Hash)], []),
                new BmsCourseDefinition("second", "Table", "Second", [new BmsCourseStage("Stage", "1", BeatmapHash: beatmap.Hash)], []),
            ]);
        });
        AddStep("load song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("song select loaded", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("beatmap carousel ready", () => !carousel.IsFiltering);
        AddStep("show course mode", () => controller.ShowCourseMode());
        AddUntilStep("course selection ready", () => controller.SelectedCourse?.Id == "first" && !controller.CourseCarousel.IsFiltering);
        AddStep("start course and change selection before completion", () =>
        {
            var updateReads = GlobalStatistics.Get<int>("Realm", "Reads (Update)");
            var readsBefore = updateReads.Value;
            controller.StartCourse(songSelect);
            Assert.That(updateReads.Value, Is.EqualTo(readsBefore));
            Assert.That(Stack.CurrentScreen, Is.SameAs(songSelect));

            if (changeCourse)
                controller.CourseCarousel.Activate(controller.CourseCarousel.GetCarouselItems()!
                    .Single(item => item.Model is BmsGroupedCourse { Course.Id: "second" }));
            else
                songSelect.Mods.Value = [new BmsModMirror()];
        });
        AddWaitStep("allow cancelled stage lookup to finish", 20);
        AddAssert("cancelled course does not start", () => Stack.CurrentScreen, () => Is.SameAs(songSelect));
    }
}
