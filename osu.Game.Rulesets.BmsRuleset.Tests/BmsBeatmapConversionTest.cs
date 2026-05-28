using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

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
                new BmsHitObject { StartTime = 2000, Column = 1, IsLongNote = true, Duration = 500 },
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
    public void TestBeatmapConverterConvert()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 2 },
                new BmsHitObject { StartTime = 2000, Column = 5, IsLongNote = true, Duration = 800 },
            },
        };
        var converter = new BmsRuleset().CreateBeatmapConverter(beatmap);
        var converted = converter.Convert();

        Assert.That(converted.HitObjects.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestBmsRankStampedOnHitObject()
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

        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(1));
    }
}
