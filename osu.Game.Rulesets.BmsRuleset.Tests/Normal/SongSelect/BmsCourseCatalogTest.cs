using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.SongSelect;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

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

    private static BmsCourseDefinition createCourse(string id, string table, string name) => new(
        id,
        table,
        name,
        [new BmsCourseStage("Stage Song", "★1")],
        "Class",
        ["Mirror"]);
}
