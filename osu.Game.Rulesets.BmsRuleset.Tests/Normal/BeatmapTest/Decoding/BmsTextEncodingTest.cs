using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsTextEncodingTest
{
    [TestCase(65001, false)]
    [TestCase(65001, true)]
    [TestCase(1200, false)]
    [TestCase(1200, true)]
    [TestCase(1201, false)]
    [TestCase(1201, true)]
    [TestCase(12000, false)]
    [TestCase(12000, true)]
    [TestCase(12001, false)]
    [TestCase(12001, true)]
    [TestCase(932, false)]
    [TestCase(51949, false)]
    public void TestChartMetadataSamplesAndTimingSurviveDecoding(int codePage, bool withBom)
    {
        var title = codePage == 51949 ? "한국어 제목" : "日本語の曲";
        var artist = codePage == 51949 ? "작곡가" : "作者";
        var samplePath = codePage == 51949 ? "소리/피아노.wav" : "音声/ピアノ.wav";
        var chart = $"#TITLE {title}\r\n#ARTIST {artist}\n#BPM 150\r#WAV01 {samplePath}\n#00111:0101\n";
        var bytes = encode(chart, codePage, withBom);
        var decoded = (BmsDecodedBeatmap)BmsBeatmapDecoder.DecodeBytes(bytes);
        var summary = BmsChartParser.ParseImportSummary(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Metadata.Title, Is.EqualTo(title));
            Assert.That(decoded.Metadata.Artist, Is.EqualTo(artist));
            Assert.That(decoded.SampleDefinitions[BmsChartParser.Enc("01")], Is.EqualTo(samplePath));
            Assert.That(decoded.HitObjects.Select(h => h.StartTime), Is.EqualTo([1600d, 2400d]));
            Assert.That(summary.Metadata.RawTitle, Is.EqualTo(title));
            Assert.That(summary.Metadata.Artist, Is.EqualTo(artist));
            Assert.That(summary.TotalObjectCount, Is.EqualTo(2));
            Assert.That(summary.Length, Is.EqualTo(2400));
        });
    }

    [TestCase(65001)]
    [TestCase(1200)]
    [TestCase(1201)]
    [TestCase(12000)]
    [TestCase(12001)]
    public void TestBomDoesNotHideInitialBpm(int codePage)
    {
        var bytes = encode("#BPM 150\n#00111:01", codePage, true);
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes);
        var summary = BmsChartParser.ParseImportSummary(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.HitObjects.Single().StartTime, Is.EqualTo(1600));
            Assert.That(summary.Length, Is.EqualTo(1600));
        });
    }

    [TestCase(51949, "테스트")]
    [TestCase(932, "ﾊﾛｰﾜｰﾙﾄﾞ")]
    [TestCase(932, "髙﨑①")]
    public void TestOverlappingLegacyEncodingsPreserveReadableMetadata(int codePage, string title)
    {
        var bytes = encode($"#TITLE {title}\n#BPM 120\n#00111:01", codePage, false);
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes);
        var summary = BmsChartParser.ParseImportSummary(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Metadata.Title, Is.EqualTo(title));
            Assert.That(summary.Metadata.RawTitle, Is.EqualTo(title));
            Assert.That(decoded.HitObjects, Has.Count.EqualTo(1));
            Assert.That(summary.TotalObjectCount, Is.EqualTo(1));
        });
    }

    [TestCase(1200)]
    [TestCase(1201)]
    [TestCase(12000)]
    [TestCase(12001)]
    public void TestBomlessUnicodePreservesSupplementaryCharacters(int codePage)
    {
        const string title = "音楽 ⌀ 🎵";
        var bytes = encode($"* header\n \t#TITLE {title}\n#BPM 120\n#00111:01", codePage, false);

        Assert.Multiple(() =>
        {
            Assert.That(BmsBeatmapDecoder.DecodeBytes(bytes).Metadata.Title, Is.EqualTo(title));
            Assert.That(BmsChartParser.ParseImportSummary(bytes).Metadata.RawTitle, Is.EqualTo(title));
        });
    }

    [Test]
    public void TestMalformedLegacyTextStillImportsValidChartData()
    {
        byte[] bytes = [.. Encoding.ASCII.GetBytes("#TITLE Recovery\n#BPM 120\n#00111:01\n"), 0x81];
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes);
        var summary = BmsChartParser.ParseImportSummary(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Metadata.Title, Is.EqualTo("Recovery"));
            Assert.That(decoded.HitObjects, Has.Count.EqualTo(1));
            Assert.That(summary.TotalObjectCount, Is.EqualTo(1));
        });
    }

    private static byte[] encode(string text, int codePage, bool withBom)
    {
        var encoding = codePage is 932 or 51949
            ? CodePagesEncodingProvider.Instance.GetEncoding(codePage)!
            : Encoding.GetEncoding(codePage);
        return withBom ? [.. encoding.GetPreamble(), .. encoding.GetBytes(text)] : encoding.GetBytes(text);
    }
}
