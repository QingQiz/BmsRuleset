using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsImportSummaryParserTest
{
    [TestCase(1, "chart.bms")]
    [TestCase(2, "chart.bms")]
    [TestCase(1, "chart.pms")]
    [TestCase(2, "chart.pms")]
    public void TestImportMatchesFullParserWithTimingAndLongNotes(int lnType, string path)
    {
        string[] lines =
        [
            "#TITLE Summary", "#ARTIST Artist", "#SUBTITLE [Hyper]", "#PLAYLEVEL 12.5", "#TOTAL 180",
            "#DEFEXRANK 140", "#RANK 1", "#EXRANK 110", "#LNMODE 3", $"#LNTYPE {lnType}",
            "#BASE 62", "#BPM 120", "#BPMaa 180", "#BPMAB -240", "#STOPaa 48", "#STOPAB 96", "#LNOBJ zz",
            "#00002:0.5", "#00102:1.5", "#00108:aaAB", "#00109:aa00", "#00109:AB00", "#00203:00780000",
            "#00311:02000000", "#00011:01zz", "#00151:01020000", "#00252:00030400", "#00321:00050006",
            "#002D6:0000000A", "#003E1:000A", "#00449:01", "#00422:01",
            "#WAV01 ignored.wav", "#BMP01 ignored.png", "#EXRANKaa 300", "#001A0:aa", "#00101:00000100",
            "#SCROLLaa -2", "#SPEEDaa 3", "#001SC:aa", "#001SP:aa", "#00104:01", "#TEXTaa ignored", "#00199:aa",
        ];
        assertMatchesFull(lines, path);
    }

    [TestCase("\n", 65001)]
    [TestCase("\r\n", 65001)]
    [TestCase("\r", 65001)]
    [TestCase("\n", 932)]
    [TestCase("\r\n", 932)]
    public void TestByteInputPreservesEncodingWhitespaceAndLineBoundaries(string newline, int codePage)
    {
        string[] lines =
        [
            "* header", "", " \t#title 日本語", "#artist 作者", "#subtitle [難易度]", "#bpm 120", "",
            "#WAV01 ignored.wav", "#00111:0100 \t", "#00151:00010001", "#00216:02", "#00249:01",
        ];
        var encoding = codePage == 65001 ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(codePage)!;
        var expected = BmsChartParser.ParseImportSummary(lines);
        var actual = BmsChartParser.ParseImportSummary(encoding.GetBytes(string.Join(newline, lines)));
        assertEqual(expected, actual);
        Assert.That(actual.Metadata.RawTitle, Is.EqualTo("日本語"));
        Assert.That(actual.Metadata.Artist, Is.EqualTo("作者"));
    }

    [Test]
    public void TestByteInputHandlesMixedNewlinesAndEmptyChart()
    {
        var actual = BmsChartParser.ParseImportSummary(Encoding.UTF8.GetBytes("\r\n#BPM 120\r#00111:01\n\n#00216:02\r\n"));
        var expected = BmsChartParser.ParseImportSummary(new[] { "#BPM 120", "#00111:01", "#00216:02" });
        assertEqual(expected, actual);
        Assert.That(BmsChartParser.ParseImportSummary(Array.Empty<byte>()).TotalObjectCount, Is.Zero);
    }

    [Test]
    public void TestIgnoredDefinitionsAndPayloadsAreNeverDecoded()
    {
        string[] essential = ["#TITLE Kept", "#BPM 120", "#00111:01", "#00216:02", "#00349:01"];
        string[] ignored =
        [
            "#GENRE unused", "#GENLE unused", "#SUBARTIST unused", "#MAKER unused", "#COMMENT unused",
            "%URL unused", "%EMAIL unused", "#URL unused", "#EMAIL unused", "#PREVIEW unused",
            "#STAGEFILE unused", "#BACKBMP unused", "#BANNER unused", "#MIDIFILE unused", "#VOLWAV 1",
            "#WAV漢字 unused", "#BMP漢字 unused", "#BGA漢字 01 0 0 32 32 0 0", "#EXRANK漢字 200",
            "#TEXT漢字 unused", "#SONG漢字 unused", "#SCROLL漢字 2", "#SPEED漢字 3", "#BASEBPM 600",
            "#99901:漢字", "#99904:漢字", "#99906:漢字", "#99907:漢字", "#9990A:漢字",
            "#9990B:漢字", "#999A0:漢字", "#999A6:漢字", "#999SC:漢字", "#999SP:漢字", "#99999:漢字",
            "#99910:漢字", "#9991A:漢字", "#9993A:漢字", "#00349:漢字",
        ];
        var expected = BmsChartParser.ParseImportSummary(essential);
        var actual = BmsChartParser.ParseImportSummary(Encoding.UTF8.GetBytes(string.Join('\n', essential.Concat(ignored))));
        assertEqual(expected, actual);
    }

    [Test]
    public void TestSampleIdsAreNotNeededForOrdinaryNotesLongNotesOrMines()
    {
        var actual = BmsChartParser.ParseImportSummary(Encoding.UTF8.GetBytes(
            "#BPM 120\n#00111:漢字\n#00151:漢字漢字\n#002D6:漢字"));
        var expected = BmsChartParser.ParseImportSummary(new[] { "#BPM 120", "#00111:01", "#00151:0101", "#002D6:0A" });
        assertEqual(expected, actual);
    }

    [Test]
    public void TestInactiveBranchesDoNotParseValuesOrConsumeNestedDecisions()
    {
        string[] lines =
        [
            "#BPM 120", "#RANDOM 2", "#IF 1", "#00111:01", "#ELSE", "#BASE 62", "#BPM漢字 300",
            "#RANDOM 3", "#IF 1", "#00249:01", "#ENDIF", "#ENDRANDOM", "#ENDIF", "#ENDRANDOM",
            "#SWITCH 2", "#CASE 1", "#00216:01", "#SKIP", "#CASE 2", "#00349:01", "#ENDSWITCH",
        ];
        var decisions = new Queue<int>([1, 1]);
        var actual = BmsChartParser.ParseImportSummary(Encoding.UTF8.GetBytes(string.Join('\n', lines)), randomValueSelector: _ => decisions.Dequeue());
        var expected = BmsChartParser.ParseImportSummary(new[] { "#BPM 120", "#00111:01", "#00216:01" });
        assertEqual(expected, actual);
        Assert.That(decisions, Is.Empty);
    }

    private static void assertMatchesFull(string[] lines, string path)
    {
        var parsed = BmsChartParser.Parse(lines, path, _ => 1);
        var summary = BmsChartParser.ParseImportSummary(lines, path, _ => 1);
        var bytes = BmsChartParser.ParseImportSummary(Encoding.UTF8.GetBytes(string.Join('\n', lines)), path, _ => 1);
        assertEqual(summary, bytes);
        Assert.Multiple(() =>
        {
            Assert.That(summary.Metadata.RawTitle, Is.EqualTo(parsed.Title));
            Assert.That(summary.Metadata.Artist, Is.EqualTo(parsed.Artist));
            Assert.That(summary.Metadata.Rank, Is.EqualTo(parsed.Rank));
            Assert.That(summary.Metadata.ExRank, Is.EqualTo(parsed.DefaultExRank));
            Assert.That(summary.Metadata.Total, Is.EqualTo(parsed.Total));
            Assert.That(summary.Metadata.PlayLevel, Is.EqualTo(parsed.PlayLevel));
            Assert.That(summary.Metadata.LockedLongNoteMode, Is.EqualTo(parsed.LockedLongNoteMode));
            Assert.That(summary.Metadata.KeyCount, Is.EqualTo(parsed.TotalColumns));
            Assert.That(summary.TotalObjectCount, Is.EqualTo(parsed.HitObjects.Count));
            Assert.That(summary.EndTimeObjectCount, Is.EqualTo(parsed.HitObjects.Count(n => n.IsLongNote)));
            Assert.That(summary.ScratchObjectCount, Is.EqualTo(parsed.HitObjects.Count(n => BmsLayout.IsScratchColumn(n.Column, parsed.LayoutVariant))));
            Assert.That(summary.Length, Is.EqualTo(parsed.HitObjects[^1].StartTime + parsed.HitObjects[^1].Duration).Within(0.000001));
            var expected = parsed.HitObjects.Where(n => !n.IsMine).ToArray();
            Assert.That(summary.StarRatingNoteTimings, Has.Count.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.That(summary.StarRatingNoteTimings[i].Column, Is.EqualTo(expected[i].Column));
                Assert.That(summary.StarRatingNoteTimings[i].StartTime, Is.EqualTo(expected[i].StartTime).Within(0.000001));
                Assert.That(summary.StarRatingNoteTimings[i].EndTime, Is.EqualTo(expected[i].StartTime + expected[i].Duration).Within(0.000001));
            }
        });
    }

    private static void assertEqual(BmsImportSummary expected, BmsImportSummary actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Metadata, Is.EqualTo(expected.Metadata));
            Assert.That(actual.Bpm, Is.EqualTo(expected.Bpm).Within(0.000001));
            Assert.That(actual.Length, Is.EqualTo(expected.Length).Within(0.000001));
            Assert.That(actual.TotalObjectCount, Is.EqualTo(expected.TotalObjectCount));
            Assert.That(actual.EndTimeObjectCount, Is.EqualTo(expected.EndTimeObjectCount));
            Assert.That(actual.ScratchObjectCount, Is.EqualTo(expected.ScratchObjectCount));
            Assert.That(actual.StarRatingNoteTimings, Is.EqualTo(expected.StarRatingNoteTimings));
        });
    }
}
