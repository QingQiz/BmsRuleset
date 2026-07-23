using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Conversion;

[TestFixture]
public class BmsBeatmapConversionTest
{
    [Test]
    public void TestBeatmapConverterCanConvertBmsHitObjects()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 0 },
                new BmsLongNote { StartTime = 2000, Column = 1, Duration = 500 },
            },
        };
        var converter = new BmsRuleset().CreateBeatmapConverter(beatmap);

        Assert.That(converter.CanConvert(), Is.True);
    }

    [Test]
    public void TestBeatmapConverterCanConvertEmptyBeatmap()
    {
        var converter = new BmsRuleset().CreateBeatmapConverter(new Beatmap());

        Assert.That(converter.CanConvert(), Is.False);
    }

    [Test]
    public void TestBeatmapConverterAllowsForeignHitObjectsForSongSelectStatistics()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new HitObject { StartTime = 1000 },
            },
        };
        var converter = new BmsRuleset().CreateBeatmapConverter(beatmap);

        Assert.That(converter.CanConvert(), Is.True);
    }

    [Test]
    public void TestBeatmapConverterConvert()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 2 },
                new BmsLongNote { StartTime = 2000, Column = 5, Duration = 800 },
            },
        };
        var converter = new BmsRuleset().CreateBeatmapConverter(beatmap);
        var converted = converter.Convert();

        Assert.That(converted.HitObjects.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestJudgementContextStampedOnHitObject()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 1,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        var converter = new BmsRuleset().CreateBeatmapConverter(beatmap);
        var converted = (BmsBeatmap)converter.Convert();

        Assert.That(converted.HitObjects[0].Beatmap.Rank, Is.EqualTo(1));
        Assert.That(converted.HitObjects[0].Beatmap.LayoutVariant, Is.EqualTo(BmsLayoutVariant.Bme7K));
    }

    [Test]
    public void TestFallbackTimingMapCoversHitObjectEndTime()
    {
        var longNote = new BmsLongNote
        {
            StartTime = 120000,
            Duration = 5000,
            Column = 1,
        };
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 8,
            HitObjects = { longNote },
        };

        var converted = (BmsBeatmap)new BmsRuleset().CreateBeatmapConverter(beatmap).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.ProjectTickToTime(timingMap.Measures[^1].StartTick), Is.GreaterThanOrEqualTo(longNote.EndTime));
        Assert.That(longNote.ScrollPositionAtEndTime, Is.GreaterThan(longNote.ScrollPositionAtStartTime));
    }
}
