using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsBeatmapDecoderTest
{
    private static Beatmap decode(string text)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);

        return new BmsBeatmapDecoder().Decode(reader);
    }

    [Test]
    public void TestBmsDecoderRegisteredWithoutRulesetInstantiation()
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes("""
                                                                         #TITLE Global Decoder Registration
                                                                         #BPM 120
                                                                         #00111:01
                                                                         """));
        using var reader = new LineBufferedReader(memoryStream);

        var decoded = Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

        Assert.That(decoded.HitObjects.OfType<BmsHitObject>().Count(), Is.EqualTo(1));
        Assert.That(decoded.Metadata.Title, Is.EqualTo("Global Decoder Registration"));
    }

    [Test]
    public void TestConverterPreservesNativeTimingMap()
    {
        var beatmap = decode("""
                             #BPM 120
                             #BPM01 240
                             #00108:01
                             #00211:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TimingMap, Is.Not.Null);
        Assert.That(converted.TimingMap!.BpmEvents.Select(e => e.Bpm), Is.EqualTo(new[] { 120, 240 }));
    }

    [Test]
    public void TestConverterStoresInferredKeyCountMetadata()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { SourceChannel = "19" },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(8));
        Assert.That(converted.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
    }

    [Test]
    public void TestDecoderAndConverterPreserveBmsSampleDefinitionsAndBgmEvents()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 kick.wav
                             #WAV02 bgm.ogg
                             #00101:02
                             #00111:01
                             """);
        var hitObject = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(hitObject.SamplePath, Is.EqualTo("kick.wav"));
        Assert.That(converted.SampleDefinitions["01"], Is.EqualTo("kick.wav"));
        Assert.That(converted.SampleDefinitions["02"], Is.EqualTo("bgm.ogg"));
        Assert.That(converted.BackgroundSampleEvents, Has.Count.EqualTo(1));
        Assert.That(converted.BackgroundSampleEvents[0].SampleKey, Is.EqualTo("02"));
        Assert.That(converted.BackgroundSampleEvents[0].Time, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestExtendedBpmChangesProjectTimes()
    {
        var beatmap = decode("""
                             #BPM 120
                             #BPM01 240
                             #00108:01
                             #00211:01
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(beatmap.ControlPointInfo.TimingPoints, Has.Count.EqualTo(2));
        Assert.That(beatmap.ControlPointInfo.TimingPoints[0].BPM, Is.EqualTo(120).Within(0.001));
        Assert.That(beatmap.ControlPointInfo.TimingPoints[1].BPM, Is.EqualTo(240).Within(0.001));
        Assert.That(timingMap.BpmEvents.Select(e => e.Bpm), Is.EqualTo(new[] { 120, 240 }));
        Assert.That(note.StartTime, Is.EqualTo(3000).Within(0.001));
    }

    [Test]
    public void TestLnObjPairsVisibleTerminator()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNOBJ ZZ
                             #00111:2200
                             #00211:00ZZ
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.SampleKey, Is.EqualTo("22"));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(480));
        Assert.That(note.Duration, Is.EqualTo(3000).Within(0.001));
    }

    [Test]
    public void TestLnType1PairsLongNoteChannelObjects()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNTYPE 1
                             #00151:0102
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.Column, Is.EqualTo(1));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(288));
        Assert.That(note.Duration, Is.EqualTo(1000).Within(0.001));
    }

    [Test]
    public void TestLnType2ClosesRunOnZeroCell()
    {
        var beatmap = decode("""
                             #BPM 120
                             #LNTYPE 2
                             #00151:11110000
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(note.TickInfo.EndTick, Is.EqualTo(288));
        Assert.That(note.Duration, Is.EqualTo(1000).Within(0.001));
    }

    [Test]
    public void TestMeasureZeroStartsAtTimeZeroAndMeasureOneStartsAfterOneMeasure()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00011:01
                             #00112:02
                             """);
        var first = (BmsHitObject)beatmap.HitObjects[0];
        var second = (BmsHitObject)beatmap.HitObjects[1];

        Assert.That(first.TickInfo.Tick, Is.EqualTo(0));
        Assert.That(first.StartTime, Is.EqualTo(0).Within(0.001));
        Assert.That(second.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(second.StartTime, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapPreservesStopsAndMeasureLengths()
    {
        var beatmap = decode("""
                             #BPM 120
                             #STOP01 192
                             #00102:0.5
                             #00109:01
                             #00211:01
                             """);
        var note = (BmsHitObject)beatmap.HitObjects.Single();
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.TickResolution, Is.EqualTo(192));
        Assert.That(timingMap.Measures.Single(m => m.Index == 1).LengthRatio, Is.EqualTo(0.5));
        Assert.That(timingMap.Measures.Single(m => m.Index == 1).LengthTicks, Is.EqualTo(96));
        Assert.That(timingMap.StopEvents, Has.Count.EqualTo(1));
        Assert.That(timingMap.StopEvents[0].Tick, Is.EqualTo(192));
        Assert.That(timingMap.StopEvents[0].StopValue, Is.EqualTo(192));
        Assert.That(timingMap.StopEvents[0].Bpm, Is.EqualTo(120));
        Assert.That(timingMap.StopEvents[0].Duration, Is.EqualTo(2000).Within(0.001));
        Assert.That(note.TickInfo.Tick, Is.EqualTo(288));
        Assert.That(note.StartTime, Is.EqualTo(5000).Within(0.001));
    }

    [Test]
    public void TestPmsDoublePlayerMetadataUsesEighteenKeys()
    {
        var beatmap = new Beatmap
        {
            Difficulty = { CircleSize = 18 },
            HitObjects =
            {
                new BmsHitObject { SourceChannel = "16" },
                new BmsHitObject { SourceChannel = "21" },
                new BmsHitObject { SourceChannel = "29" },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(18));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "16").Column, Is.EqualTo(5));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "21").Column, Is.EqualTo(9));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "29").Column, Is.EqualTo(17));
    }

    [Test]
    public void TestPmsMetadataUsesNineKeysWithoutScratch()
    {
        var beatmap = new Beatmap
        {
            Difficulty = { CircleSize = 9 },
            HitObjects =
            {
                new BmsHitObject { SourceChannel = "16" },
                new BmsHitObject { SourceChannel = "17" },
                new BmsHitObject { SourceChannel = "18" },
                new BmsHitObject { SourceChannel = "19" },
            },
        };

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TotalColumns, Is.EqualTo(9));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "18").Column, Is.EqualTo(5));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "19").Column, Is.EqualTo(6));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "16").Column, Is.EqualTo(7));
        Assert.That(converted.HitObjects.Single(h => h.SourceChannel == "17").Column, Is.EqualTo(8));
    }

    [Test]
    public void TestSparseSevenKeyChartStoresKeyCountMetadata()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00119:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(beatmap.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(beatmap.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.TotalColumns, Is.EqualTo(8));
        Assert.That(converted.LayoutVariant, Is.EqualTo(BmsLayoutVariant.Bme7K));
        Assert.That(converted.HitObjects.Single().Column, Is.EqualTo(7));
        Assert.That(converted.Difficulty.CircleSize, Is.EqualTo(8));
        Assert.That(converted.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(8));
    }

    [Test]
    public void TestTickResolutionExpandsForPayloadDivisions()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00111:0102030405
                             """);

        var first = (BmsHitObject)beatmap.HitObjects[0];
        var last = (BmsHitObject)beatmap.HitObjects[^1];
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.TickResolution, Is.EqualTo(960));
        Assert.That(converted.TimingMap!.TickResolution, Is.EqualTo(960));
        Assert.That(first.TickInfo.Tick, Is.EqualTo(960));
        Assert.That(last.TickInfo.Tick, Is.EqualTo(1728));
    }

    [Test]
    public void TestVisibleNotesDecodeToNativeObjects()
    {
        var beatmap = decode("""
                             #TITLE Decoder Smoke
                             #ARTIST Test Artist
                             #BPM 120
                             #00111:0100
                             #00116:0002
                             """);

        Assert.That(beatmap.Metadata.Title, Is.EqualTo("Decoder Smoke"));
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Test Artist"));
        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(2));

        var first = (BmsHitObject)beatmap.HitObjects[0];
        var second = (BmsHitObject)beatmap.HitObjects[1];

        Assert.That(first.Column, Is.EqualTo(1));
        Assert.That(first.SampleKey, Is.EqualTo("01"));
        Assert.That(first.TickInfo.Tick, Is.EqualTo(192));
        Assert.That(first.StartTime, Is.EqualTo(2000).Within(0.001));

        Assert.That(second.Column, Is.EqualTo(0));
        Assert.That(second.SampleKey, Is.EqualTo("02"));
        Assert.That(second.TickInfo.Tick, Is.EqualTo(288));
        Assert.That(second.StartTime, Is.EqualTo(3000).Within(0.001));
    }
}
