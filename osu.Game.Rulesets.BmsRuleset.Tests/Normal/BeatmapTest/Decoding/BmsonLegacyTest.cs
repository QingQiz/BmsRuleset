using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonLegacyTest
{
    [TestCase("1.0.0")]
    [TestCase("1.2.3+player.1")]
    public void StableMajorOneVersionsUseTheStandardReader(string version)
    {
        var chart = Encoding.UTF8.GetString(BmsonParserTest.Chart()).Replace("1.0.0", version);
        Assert.That(BmsonParser.Parse(Encoding.UTF8.GetBytes(chart)).HitObjects, Is.Empty);
    }

    [TestCase("2.0.0")]
    [TestCase("1.0.0-rc.1")]
    [TestCase("1.01.0")]
    public void UnsupportedVersionsAreRejected(string version)
    {
        var chart = Encoding.UTF8.GetString(BmsonParserTest.Chart()).Replace("1.0.0", version);
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(Encoding.UTF8.GetBytes(chart)));
    }

    [TestCase("", null)]
    [TestCase(",\"total\":0", 0d)]
    [TestCase(",\"total\":320", 320d)]
    public void LegacyTotalRetainsMissingZeroAndAbsoluteValues(string total, double? expected)
    {
        var chart = Encoding.UTF8.GetBytes($$"""
            {"info":{"initBPM":120{{total}}},"soundChannel":[]}
            """);
        Assert.That(BmsonParser.Parse(chart).Total, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LegacyFieldsTimingLanesAndAbsoluteTotalAreNormalizedAtTheBoundary(bool explicitVersion)
    {
        var bytes = Encoding.UTF8.GetBytes("""
                                           {"info":{"title":"Legacy","artist":"A","initBPM":120,"judgeRank":100,"total":320},
                                            "soundChannel":[{"name":"stem.wav","notes":[{"x":18,"y":240,"l":240},{"x":11,"y":720,"c":true}]}],
                                            "bpmNotes":[{"y":240,"v":240}],"stopNotes":[{"y":240,"v":240}],
                                            "bga":{"bgaHeader":[{"ID":100000,"name":"a.png"}],"bgaNotes":[{"y":240,"ID":100000}]}}
                                           """);
        if (explicitVersion)
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("{\"info\"", "{\"version\":\"0.21\",\"info\""));
        var result = BmsonParser.Parse(bytes);
        Assert.That(result.TotalColumns, Is.EqualTo(12));
        Assert.That(result.HitObjects[0].Column, Is.EqualTo(11));
        Assert.That(result.HitObjects[0].StartTime, Is.EqualTo(500));
        Assert.That(result.HitObjects[0].Duration, Is.EqualTo(500));
        Assert.That(result.HitObjects[1].StartTime, Is.EqualTo(1250));
        Assert.That(result.Total, Is.EqualTo(320));
        Assert.That(result.Bga.Events.Single().Time, Is.EqualTo(500));
    }
}
