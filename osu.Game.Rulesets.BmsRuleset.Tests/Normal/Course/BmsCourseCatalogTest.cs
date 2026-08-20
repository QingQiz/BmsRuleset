using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Course;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course;

[TestFixture]
public class BmsCourseCatalogTest
{
    [Test]
    public void TestCatalogSortsByTableThenCourse()
    {
        var catalog = new BmsCourseCatalog();

        catalog.Replace
        ([
            createCourse("b-2", "Table B", "Second"),
            createCourse("a-2", "Table A", "Second"),
            createCourse("a-1", "Table A", "First"),
        ]);

        Assert.That(catalog.Courses.Select(course => course.Id), Is.EqualTo(new[] { "a-1", "a-2", "b-2" }));
    }

    [TestCase("first", true)]
    [TestCase("table a", true)]
    [TestCase("class", true)]
    [TestCase("mirror", true)]
    [TestCase("stage song", true)]
    [TestCase("another", false)]
    public void TestCourseSearch(string search, bool expected)
    {
        var course = createCourse("a-1", "Table A", "First");
        Assert.That(course.Matches(search), Is.EqualTo(expected));
    }

    [Test]
    public void TestDifficultyTableCoursesAreConvertedWithChartMetadata()
    {
        var table = new global::osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable
        {
            Name = "Satellite",
            Symbol = "sl",
            SourcePath = "satellite/header.json",
            Entries =
            [
                new TableEntry
                {
                    Level = "7",
                    Md5Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                    Title = "First Song",
                    Artist = "First Artist",
                },
                new TableEntry
                {
                    Level = "8",
                    Md5Hash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                    Sha256Hash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc",
                    Title = "Second Song",
                    Artist = "Second Artist",
                },
            ],
            Courses =
            [
                new TableCourse
                {
                    Name = "Satellite sl7",
                    Hashes = ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"],
                    Constraints = ["grade_mirror", "gauge_lr2"],
                },
                new TableCourse
                {
                    Name = "Satellite EX",
                    Hashes = ["cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"],
                    Gauge = "ExClass",
                },
            ],
        };

        var courses = BmsCourseTableConverter.Convert([table]);

        Assert.That(courses, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(courses[0].TableName, Is.EqualTo("Satellite"));
            Assert.That(courses[0].Name, Is.EqualTo("Satellite sl7"));
            Assert.That(courses[0].Gauge, Is.EqualTo("Class"));
            Assert.That(courses[0].Constraints, Is.EqualTo(new[] { "grade_mirror", "gauge_lr2" }));
            Assert.That(courses[0].Stages[0].Title, Is.EqualTo("First Song"));
            Assert.That(courses[0].Stages[0].Artist, Is.EqualTo("First Artist"));
            Assert.That(courses[0].Stages[0].Difficulty, Is.EqualTo("sl7"));
            Assert.That(courses[0].Stages[0].BeatmapHash, Is.EqualTo("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
            Assert.That(courses[1].Gauge, Is.EqualTo("ExClass"));
            Assert.That(courses[1].Stages[0].Title, Is.EqualTo("Second Song"));
            Assert.That(courses[1].Stages[0].Artist, Is.EqualTo("Second Artist"));
        });
    }

    [Test]
    public void TestParsedCourseOrderOverridesAlphabeticalOrder()
    {
        var catalog = new BmsCourseCatalog();

        catalog.Replace
        ([
            createCourse("sl-9", "Satellite", "sl9") with { Order = 1 },
            createCourse("sl-10", "Satellite", "sl10") with { Order = 2 },
            createCourse("sl-8", "Satellite", "sl8") with { Order = 0 },
        ]);

        Assert.That(catalog.Courses.Select(course => course.Name), Is.EqualTo(new[] { "sl8", "sl9", "sl10" }));
    }

    [Test]
    public void TestRuntimeCatalogTracksDifficultyTableStore()
    {
        var previousStore = BmsRulesetRuntime.DifficultyTableStore;
        var store = new DifficultyTableStore(null, Path.Combine(TestContext.CurrentContext.WorkDirectory, "course-store"));
        var table = new global::osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable
        {
            Name = "Runtime Table",
            SourcePath = "runtime-table",
            Courses =
            [
                new TableCourse
                {
                    Name = "Runtime Course",
                    Hashes = ["aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"],
                },
            ],
        };

        try
        {
            BmsRulesetRuntime.DifficultyTableStore = store;
            store.AddTable(table);
            Assert.That(BmsRulesetRuntime.CourseCatalog.Courses.Select(course => course.Name), Is.EqualTo(new[] { "Runtime Course" }));

            store.RemoveTable(table);
            Assert.That(BmsRulesetRuntime.CourseCatalog.Courses, Is.Empty);
        }
        finally
        {
            BmsRulesetRuntime.DifficultyTableStore = previousStore;
        }
    }

    private static BmsCourseDefinition createCourse(string id, string table, string name) => new(
        id,
        table,
        name,
        [new BmsCourseStage("Stage Song", "★1")],
        "Class",
        ["Mirror"]);
}
