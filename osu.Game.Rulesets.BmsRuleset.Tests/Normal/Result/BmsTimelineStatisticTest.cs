using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Result;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Result;

[TestFixture]
public class BmsTimelineStatisticTest
{
    [Test]
    public void TestNotesExcludeLandmines()
    {
        var note = new BmsNote { StartTime = 1000, Column = 1 };
        var mine = new BmsLandmine { StartTime = 2000, Column = 2 };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { note, mine },
        };
        var score = new ScoreInfo { Passed = true };

        var data = BmsTimelineStatistic.CreateData(score, beatmap);

        Assert.Multiple(() =>
        {
            Assert.That(data.Notes.Categories.Select(category => category.Label), Does.Not.Contain("mine"));
            Assert.That(data.Notes.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestCourseDataKeepsNotesForUnplayedStages()
    {
        var playedBeatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { new BmsNote { StartTime = 1000, Column = 1 } },
        };
        var unplayedBeatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { new BmsNote { StartTime = 1000, Column = 2 } },
        };
        var playedScore = new ScoreInfo
        {
            Passed = true,
            HitEvents =
            [
                new HitEvent(0, 1, HitResult.Perfect, new BmsNote { StartTime = 1000, Column = 1 }, null, null),
            ],
        };

        var data = BmsTimelineStatistic.CreateCourseData(
        [
            (playedScore, (IBeatmap)playedBeatmap),
            ((ScoreInfo)null!, unplayedBeatmap),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(data.Notes.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(2));
            Assert.That(data.Judgements.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(1));
            Assert.That(data.FastSlow.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(0));
        });
    }
}
