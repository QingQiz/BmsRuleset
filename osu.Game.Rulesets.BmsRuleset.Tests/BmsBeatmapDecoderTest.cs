using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
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

    private static Beatmap decode(string text, Func<int, int> randomValueSelector)
    {
        using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new LineBufferedReader(memoryStream);

        return new BmsBeatmapDecoder(randomValueSelector).Decode(reader);
    }

    [Test]
    public void TestAutoplayExtensionPathCarriesBranchDecisionFrame()
    {
        var beatmap = new BmsBeatmap
        {
            BranchDecisions = [new BmsBranchDecision(3, 1)],
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        ICreateReplayData autoplay = new BmsModAutoplay();

        var score = autoplay.CreateScoreFromReplayData(beatmap, [autoplay as Mod]);

        Assert.That(score.Replay.Frames.OfType<BmsReplayFrame>().First().BranchDecisions, Is.EqualTo("3:1"));
    }

    [Test]
    public void TestAutoplayReplayCarriesBranchDecisions()
    {
        var beatmap = new BmsBeatmap
        {
            BranchDecisions = [new BmsBranchDecision(2, 2)],
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        var autoplay = new BmsModAutoplay();

        var replayData = autoplay.CreateReplayData(beatmap, [autoplay]);
        var score = BmsModAutoplay.CreateScoreWithBranchDecisions(beatmap, replayData);

        Assert.That(score.ScoreInfo.Mods.OfType<BmsModBranchReplay>().Single().Decisions.Value, Is.EqualTo("2:2"));
        Assert.That(replayData.Replay.Frames.OfType<BmsReplayFrame>().First().BranchDecisions, Is.EqualTo("2:2"));
    }

    [Test]
    public void TestBaseBpmOverridesScrollReference()
    {
        // #BASEBPM should override the scroll reference BPM without affecting note timing.
        var beatmap = decode("""
                             #BPM 120
                             #BASEBPM 200
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;
        var note = (BmsHitObject)beatmap.HitObjects.Single();

        // Scroll reference uses #BASEBPM, not the header #BPM
        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(200).Within(0.000001));

        // Note timing is still driven by #BPM 120
        Assert.That(note.StartTime, Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestBaseBpmScrollDistance()
    {
        // Verify scroll distance at #BASEBPM 200 vs standard #BPM 120.
        // A measure (192 ticks) at base BPM 200 → scroll = 192 * 60000/200 / (192/4) = 192 * 300 / 48 = 1200
        // A measure (192 ticks) at base BPM 120 → scroll = 192 * 60000/120 / (192/4) = 192 * 500 / 48 = 2000
        var beatmap = decode("""
                             #BPM 120
                             #BASEBPM 200
                             #00111:01
                             #00211:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        var scrollDistance = timingMap.GetScrollPositionAtTick(192) - timingMap.GetScrollPositionAtTick(0);

        // With base BPM 200, 192 ticks → 1200 scroll units
        Assert.That(scrollDistance, Is.EqualTo(1200).Within(0.001));
    }

    [Test]
    public void TestBaseBpmWithNoScrollOverride()
    {
        // Without #BASEBPM, scroll reference falls back to header #BPM.
        var beatmap = decode("""
                             #BPM 120
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(120).Within(0.000001));
    }

    [Test]
    public void TestBaseBpmWithZeroValueIsIgnored()
    {
        // #BASEBPM with an invalid / zero value should be ignored.
        var beatmap = decode("""
                             #BPM 150
                             #BASEBPM 0
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var timingMap = converted.TimingMap!;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(150).Within(0.000001));
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
    public void TestBranchReplayModAppliesDecisionsToConverter()
    {
        var converter = new BmsBeatmapConverter(decode("#BPM 120\n#00111:01"), new BmsRuleset());
        var mod = new BmsModBranchReplay
        {
            Decisions = { Value = "2:1" },
        };

        mod.ApplyToBeatmapConverter(converter);

        Assert.That(converter.BranchReplayDecisions, Is.EqualTo("2:1"));
    }

    [Test]
    public void TestChannelInsideRandomButOutsideIfRemainsActive()
    {
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #00111:01
                             #IF 2
                             #00112:02
                             #ENDIF
                             #00113:03
                             #ENDRANDOM
                             """, _ => 1);

        Assert.That(beatmap.HitObjects.Cast<BmsHitObject>().Select(h => h.SourceChannel), Is.EqualTo(new[] { "11", "13" }));
    }

    [Test]
    public void TestCommentGoesToTags()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 120
                             #COMMENT A comment about the chart
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("A comment about the chart"));
    }

    [Test]
    public void TestConverterMaterialisesBranchDecisionsAtPlayConversion()
    {
        var decoded = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 1);
        var converter = new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchRandomValueSelector = _ => 2,
        };
        var converted = (BmsBeatmap)converter.Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(converted.BranchDecisions, Is.EqualTo(new[] { new BmsBranchDecision(2, 2) }));
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
    public void TestConverterUsesReplayBranchDecisions()
    {
        var decoded = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 1);
        var converter = new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            BranchReplayDecisions = "2:2",
        };

        var converted = (BmsBeatmap)converter.Convert();
        var note = converted.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
        Assert.That(converted.BranchDecisions, Is.EqualTo(new[] { new BmsBranchDecision(2, 2) }));
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
    public void TestDecoderParsesLandmineChannels()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV00 bomb.wav
                             #001D3:0000001E
                             """);

        var mine = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(mine.IsMine, Is.True);
        Assert.That(mine.Column, Is.EqualTo(3));
        Assert.That(mine.SourceChannel, Is.EqualTo("D3"));
        Assert.That(mine.SampleKey, Is.EqualTo("1E"));
        Assert.That(mine.SamplePath, Is.Empty);
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(25));
        Assert.That(mine.LandmineExplosionSamplePath, Is.EqualTo("bomb.wav"));
    }

    [Test]
    public void TestDecoderParsesSecondPlayerLandmineChannels()
    {
        var beatmap = decode("""
                             #BPM 120
                             #00129:01
                             #001E1:0A
                             """);

        var mine = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(1));
        Assert.That(mine.IsMine, Is.True);
        Assert.That(mine.Column, Is.EqualTo(6));
        Assert.That(mine.SourceChannel, Is.EqualTo("E1"));
        Assert.That(mine.LandmineDamagePercent, Is.EqualTo(5));
    }

    [Test]
    public void TestEndIfVariantsWithElseFlow()
    {
        // All three typo variants should correctly close an #IF block
        // so that a subsequent #ELSEIF/#ELSE belongs to the next #IF, not to the closed one.
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END
                             #ELSEIF 2
                             #00112:02
                             #IFEND
                             #END IF
                             #ENDRANDOM
                             """, _ => 2);

        // With selector=2, #IF 1 is skipped → lines inside it are inactive.
        // #ELSEIF with #END (from line 6) → closes a non-existent #IF, no-op.
        // Then #ELSEIF 2 matches → note 12:02 is active.
        // #IFEND closes that. #END IF is another no-op (no open #IF).
        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
    }

    [Test]
    public void TestEndIfWithHashEnd()
    {
        // #END should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END
                             #IF 2
                             #00112:02
                             #END
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("11"));
        Assert.That(note.SampleKey, Is.EqualTo("01"));
    }

    [Test]
    public void TestEndIfWithHashEndSpaceIf()
    {
        // #END IF (with space) should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #END IF
                             #IF 2
                             #00112:02
                             #END IF
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("11"));
        Assert.That(note.SampleKey, Is.EqualTo("01"));
    }

    [Test]
    public void TestEndIfWithHashIfEnd()
    {
        // #IFEND should be treated as #ENDIF
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #IFEND
                             #IF 2
                             #00112:02
                             #IFEND
                             #ENDRANDOM
                             """, _ => 1);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("11"));
        Assert.That(note.SampleKey, Is.EqualTo("01"));
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
    public void TestGenleIsGenreTypoFallback()
    {
        // #GENLE should be treated as #GENRE (song genre → Tags)
        var beatmap = decode("""
                             #TITLE Test
                             #ARTIST Me
                             #GENLE Some Genre
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("Some Genre"));
        Assert.That(beatmap.Metadata.Source, Is.EqualTo("BMS"));
    }

    [Test]
    public void TestInactiveNestedRandomDoesNotConsumeDecision()
    {
        var decisions = new Queue<int>([1]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("11"));
        Assert.That(decisions, Is.Empty);
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
    public void TestLongNoteTailNoSampleEventForLnType2()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #LNTYPE 2
                             #00151:0100
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // LNTYPE 2 terminates with "00" (control value, no sample) → no tail sample event.
        Assert.That(converted.LongNoteTailSampleEvents, Is.Empty);
    }

    [Test]
    public void TestLongNoteTailSameSampleKeyWithTail()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 ln.wav
                             #LNTYPE 1
                             #00151:0101
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // For LNTYPE 1 with payload "0101", both head and tail have value "01",
        // so the tail sample key should not be played.
        Assert.That(converted.HitObjects[0].IsLongNote, Is.True);
        Assert.That(converted.HitObjects[0].SampleKey, Is.EqualTo("01"));
        Assert.That(converted.LongNoteTailSampleEvents, Has.Count.EqualTo(0));
    }

    [Test]
    public void TestLongNoteTailSampleEventWithDistinctTailSample()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #WAV02 tail.wav
                             #LNTYPE 1
                             #00151:0102
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        // Terminating cell has value "02" → tail sample key is "02", NOT head's "01".
        Assert.That(converted.LongNoteTailSampleEvents, Has.Count.EqualTo(1));
        Assert.That(converted.LongNoteTailSampleEvents[0].SampleKey, Is.EqualTo("02"));
    }

    [Test]
    public void TestLongNoteTailSamplePathOnHitObject()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #WAV02 tail.wav
                             #LNTYPE 1
                             #00151:0102
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var note = (BmsHitObject)converted.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        Assert.That(note.TailSampleKey, Is.EqualTo("02"));
        Assert.That(note.TailSamplePath, Is.EqualTo("tail.wav"));
    }

    [Test]
    public void TestLongNoteTailSamplePathWithNoWavForTailValue()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAV01 head.wav
                             #LNTYPE 1
                             #00151:0103
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var note = (BmsHitObject)converted.HitObjects.Single();

        Assert.That(note.IsLongNote, Is.True);
        // Terminating value "03" has no #WAV definition → TailSamplePath should be empty
        Assert.That(note.TailSampleKey, Is.EqualTo("03"));
        Assert.That(note.TailSamplePath, Is.Empty);
        // No tail sample event either since the sample can't be resolved
        Assert.That(converted.LongNoteTailSampleEvents, Is.Empty);
    }

    [Test]
    public void TestMakerSetsAuthor()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #MAKER ChartCreator
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Author.Username, Is.EqualTo("ChartCreator"));
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
    public void TestMultiLnObjWithDistinctTailSamples()
    {
        var beatmap = decode("""
                             #BPM 120
                             #WAVaa onkeydown1.wav
                             #WAVbb onkeyup1.wav
                             #WAVcc onkeydown2.wav
                             #WAVdd onkeyup2.wav
                             #LNOBJ BB
                             #LNOBJ DD
                             #00111:00aa00bb
                             #00213:00cc00dd
                             """);

        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();
        var notes = converted.HitObjects.OrderBy(h => h.StartTime).ToList();

        Assert.That(notes, Has.Count.EqualTo(2));

        // First LN: head=aa, tail=bb
        Assert.That(notes[0].IsLongNote, Is.True);
        Assert.That(notes[0].SampleKey, Is.EqualTo("aa"));
        Assert.That(notes[0].TailSampleKey, Is.EqualTo("bb"));
        Assert.That(notes[0].TailSamplePath, Is.EqualTo("onkeyup1.wav"));

        // Second LN: head=cc, tail=dd
        Assert.That(notes[1].IsLongNote, Is.True);
        Assert.That(notes[1].SampleKey, Is.EqualTo("cc"));
        Assert.That(notes[1].TailSampleKey, Is.EqualTo("dd"));
        Assert.That(notes[1].TailSamplePath, Is.EqualTo("onkeyup2.wav"));
    }

    [Test]
    public void TestNativeTimingMapBpmChangesScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(192, 260, 0)],
            []);

        var oneMeasureAt130 = 60000d / 130 * 4;
        var halfMeasureAt130Scroll = timingMap.GetScrollPositionAtTime(oneMeasureAt130 / 2) - timingMap.GetScrollPositionAtTime(0);
        var oneMeasureAt260Scroll = timingMap.GetScrollPositionAtTime(oneMeasureAt130 + oneMeasureAt130 / 2) - timingMap.GetScrollPositionAtTime(oneMeasureAt130);

        Assert.That(oneMeasureAt260Scroll, Is.EqualTo(halfMeasureAt130Scroll * 2).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapHandlesExtremeHighBpmScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 1_000_000, 0)],
            []);

        var oneMeasureAtExtremeBpm = 60000d / 1_000_000 * 4;

        Assert.That(timingMap.GetScrollPositionAtTime(oneMeasureAtExtremeBpm), Is.EqualTo(timingMap.GetScrollPositionAtTick(192)).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapIgnoresZeroBpmEventForScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(192, 0, 2000)],
            []);

        var scrollBeforeZero = timingMap.GetScrollPositionAtTime(3000) - timingMap.GetScrollPositionAtTime(2000);
        var expectedAt120Bpm = timingMap.GetScrollPositionAtTick(288) - timingMap.GetScrollPositionAtTick(192);

        Assert.That(scrollBeforeZero, Is.EqualTo(expectedAt120Bpm).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapKeepsTickSpacingAcrossBpmChanges()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(192, 260, 0)],
            []);

        var firstMeasureDistance = timingMap.GetScrollPositionAtTick(192) - timingMap.GetScrollPositionAtTick(0);
        var secondMeasureDistance = timingMap.GetScrollPositionAtTick(384) - timingMap.GetScrollPositionAtTick(192);

        Assert.That(secondMeasureDistance, Is.EqualTo(firstMeasureDistance).Within(0.001));
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
    public void TestNativeTimingMapPreservesSubOneBpmScrollSpeed()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 130, 0), new BmsBpmEvent(0, 0.25, 0, 1)],
            []);

        var oneBeatAtQuarterBpm = 60000d / 0.25;

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(130).Within(0.000001));
        Assert.That(timingMap.GetScrollPositionAtTime(oneBeatAtQuarterBpm), Is.EqualTo(timingMap.GetScrollPositionAtTick(48)).Within(0.001));
    }

    [Test]
    public void TestNativeTimingMapStopFreezesScrollPosition()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0)],
            [new BmsStopEvent(192, 2000, 192, 120, 0)]);

        var stoppedPosition = timingMap.GetScrollPositionAtTick(192);

        Assert.That(timingMap.GetScrollPositionAtTime(2500), Is.EqualTo(stoppedPosition).Within(0.001));
        Assert.That(timingMap.GetScrollPositionAtTime(3999), Is.EqualTo(stoppedPosition).Within(0.001));
        Assert.That(timingMap.GetScrollPositionAtTime(4500), Is.GreaterThan(stoppedPosition));
    }

    [Test]
    public void TestNativeTimingMapUsesHeaderBpmAsScrollReference()
    {
        var timingMap = new BmsTimingMap(
            192,
            [],
            [new BmsBpmEvent(0, 120, 0), new BmsBpmEvent(0, 240, 0, 1)],
            []);

        Assert.That(timingMap.ScrollReferenceBpm, Is.EqualTo(120));
        Assert.That(timingMap.GetScrollPositionAtTick(192), Is.EqualTo(2000).Within(0.001));
    }

    [Test]
    public void TestNestedRandomUsesIndependentBranchDecisions()
    {
        var decisions = new Queue<int>([2, 1]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
        Assert.That(decisions, Is.Empty);
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
    public void TestRandomDecisionsCanVaryBetweenDecodes()
    {
        const string chart = """
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """;

        var first = decode(chart, _ => 1);
        var second = decode(chart, _ => 2);

        Assert.That(((BmsHitObject)first.HitObjects.Single()).SourceChannel, Is.EqualTo("11"));
        Assert.That(((BmsHitObject)second.HitObjects.Single()).SourceChannel, Is.EqualTo("12"));
    }

    [Test]
    public void TestRandomIfMaterialisesSelectedBranchOnly()
    {
        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #00112:02
                             #ENDIF
                             #ENDRANDOM
                             """, _ => 2);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
    }

    [Test]
    public void TestRandomInsideSwitchUsesBothBranchDecisions()
    {
        var decisions = new Queue<int>([2, 1]);

        var beatmap = decode("""
                             #BPM 120
                             #SWITCH 2
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #RANDOM 2
                             #IF 1
                             #00112:02
                             #ENDIF
                             #IF 2
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             #SKIP
                             #ENDSW
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
        Assert.That(decisions, Is.Empty);
    }

    [Test]
    public void TestRankDefaultsToNormalWhenAbsent()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(2)); // NORMAL
        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(2));
    }

    [Test]
    public void TestRankParsedFromChart()
    {
        var beatmap = decode("""
                             #RANK 1
                             #TITLE Test
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(1));
        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(1));
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    public void TestRankPreservedForAllValidValues(int rank)
    {
        var beatmap = decode($"#RANK {rank}\n#BPM 130\n#00111:01");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Rank, Is.EqualTo(rank));
    }

    [Test]
    public void TestSetRandomAndElseIfElse()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETRANDOM 3
                             #IF 1
                             #00111:01
                             #ELSEIF 3
                             #00112:02
                             #ELSE
                             #00113:03
                             #ENDIF
                             #ENDRANDOM
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("12"));
        Assert.That(note.SampleKey, Is.EqualTo("02"));
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
    public void TestSubArtistAppendedToArtist()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #ARTIST Main
                             #SUBARTIST Feat
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Artist, Is.EqualTo("Main (Feat)"));
    }

    [Test]
    public void TestSubtitleAppendedToTitle()
    {
        var beatmap = decode("""
                             #TITLE Main
                             #SUBTITLE Sub
                             #BPM 120
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Title, Is.EqualTo("Main - Sub"));
    }

    [Test]
    public void TestSwitchDefaultRunsWhenNoCaseMatches()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETSWITCH 4
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #00112:02
                             #SKIP
                             #DEF
                             #00113:03
                             #ENDSW
                             """);

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("13"));
        Assert.That(note.SampleKey, Is.EqualTo("03"));
    }

    [Test]
    public void TestSwitchFallsThroughUntilSkip()
    {
        var beatmap = decode("""
                             #BPM 120
                             #SETSWITCH 2
                             #CASE 1
                             #00111:01
                             #SKIP
                             #CASE 2
                             #00112:02
                             #CASE 3
                             #00113:03
                             #SKIP
                             #DEF
                             #00114:04
                             #ENDSW
                             """);

        Assert.That(beatmap.HitObjects.Cast<BmsHitObject>().Select(h => h.SourceChannel), Is.EqualTo(new[] { "12", "13" }));
    }

    [Test]
    public void TestSwitchInsideRandomUsesBothBranchDecisions()
    {
        var decisions = new Queue<int>([2, 3]);

        var beatmap = decode("""
                             #BPM 120
                             #RANDOM 2
                             #IF 1
                             #00111:01
                             #ENDIF
                             #IF 2
                             #SWITCH 3
                             #CASE 1
                             #00112:02
                             #SKIP
                             #CASE 3
                             #00113:03
                             #SKIP
                             #DEF
                             #00114:04
                             #ENDSW
                             #ENDIF
                             #ENDRANDOM
                             """, _ => decisions.Dequeue());

        var note = (BmsHitObject)beatmap.HitObjects.Single();

        Assert.That(note.SourceChannel, Is.EqualTo("13"));
        Assert.That(note.SampleKey, Is.EqualTo("03"));
        Assert.That(decisions, Is.Empty);
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
    public void TestTotalDefaultsToZeroWhenAbsent()
    {
        var beatmap = decode("""
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(0).Within(0.001));
    }

    [Test]
    public void TestTotalParsedFromChart()
    {
        var beatmap = decode("""
                             #TOTAL 250
                             #BPM 130
                             #00111:01
                             """);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(250).Within(0.001));
    }

    [Test]
    public void TestTotalPreservesDecimalValue()
    {
        var beatmap = decode($"#TOTAL 160.5\n#BPM 130\n#00111:01");
        var converted = (BmsBeatmap)new BmsBeatmapConverter(beatmap, new BmsRuleset()).Convert();

        Assert.That(converted.Total, Is.EqualTo(160.5).Within(0.001));
    }

    [Test]
    public void TestUrlAndEmailGoToTags()
    {
        var beatmap = decode("""
                             #TITLE Test
                             #BPM 120
                             %URL https://example.com
                             %EMAIL author@example.com
                             #00111:01
                             """);
        Assert.That(beatmap.Metadata.Tags, Does.Contain("https://example.com"));
        Assert.That(beatmap.Metadata.Tags, Does.Contain("author@example.com"));
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
