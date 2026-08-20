using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect;

[TestFixture]
public class BmsCourseGameplayTest
{
    [Test]
    public void TestCoursePlayerDisallowsPauseAndRestart()
    {
        var session = createSession();
        var player = new BmsCoursePlayer(session);

        Assert.Multiple(() =>
        {
            Assert.That(player.Configuration.AllowPause, Is.False);
            Assert.That(player.Configuration.AllowRestart, Is.False);
        });
    }

    private static BmsCourseSession createSession()
    {
        var stage = new BmsResolvedCourseStage(
            new BmsCourseStage("Stage", "Level", BeatmapHash: "hash"),
            new BeatmapInfo { Hash = "hash" });
        var course = new BmsCourseDefinition("course", "Table", "Course", [stage.Definition], "Class", []);
        return new BmsCourseSession(course, [stage], [], BmsGaugeType.Class);
    }
}
