using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsInvisibleNoteTest
{
    [TestCase("31", ".bms", BmsLayoutVariant.Bms5K, 1)]
    [TestCase("36", ".bms", BmsLayoutVariant.Bms5K, 0)]
    [TestCase("38", ".bms", BmsLayoutVariant.Bme7K, 6)]
    [TestCase("39", ".bms", BmsLayoutVariant.Bme7K, 7)]
    [TestCase("41", ".bms", BmsLayoutVariant.Bms5KDouble, 6)]
    [TestCase("46", ".bms", BmsLayoutVariant.Bms5KDouble, 11)]
    [TestCase("49", ".bms", BmsLayoutVariant.Bme7KDouble, 14)]
    [TestCase("37", ".pms", BmsLayoutVariant.Pms9K, 8)]
    [TestCase("42", ".pms", BmsLayoutVariant.Pms9K, 5)]
    [TestCase("47", ".pms", BmsLayoutVariant.Pms9KDouble, 15)]
    public void TestLayoutAndImportSummary(string channel, string extension, BmsLayoutVariant variant, int column)
    {
        string[] lines = ["#BPM 120", $"#001{channel}:01"];
        var parsed = BmsChartParser.Parse(lines, extension);
        var summary = BmsChartParser.ParseImportSummary(lines, extension);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.LayoutVariant, Is.EqualTo(variant));
            Assert.That(parsed.InvisibleNotes.Single().Column, Is.EqualTo(column));
            Assert.That(parsed.HitObjects, Is.Empty);
            Assert.That(summary.Metadata.KeyCount, Is.EqualTo(parsed.TotalColumns));
            Assert.That(summary.TotalObjectCount, Is.Zero);
            Assert.That(summary.StarRatingNoteTimings, Is.Empty);
            Assert.That(summary.Length, Is.Zero);
        });
    }

    [Test]
    public void TestTimingVolumeBase62AndIndependentLnobj()
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#BASE 62", "#WAVaa hidden.wav", "#WAV01 visible.wav",
            "#VOLWAV 45", "#LNOBJ aa", "#STOP01 192", "#00009:01",
            "#00111:01aa", "#00131:aa00", "#00131:00aa",
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.HitObjects.Single().IsLongNote, Is.True);
            Assert.That(parsed.InvisibleNotes.Select(n => n.StartTime), Is.EqualTo(new[] { 4000d, 5000d }));
            Assert.That(parsed.InvisibleNotes.All(n => !n.IsLongNote && !n.IsMine), Is.True);
            Assert.That(parsed.InvisibleNotes.All(n => n.SampleVolume == 45), Is.True);
            Assert.That(parsed.InvisibleNotes.All(n => n.SampleKey == BmsChartParser.EncodePair('a', 'a')), Is.True);
        });
    }

    [Test]
    public void TestDuplicateHiddenNotesUseLastDefinitionWithoutReplacingVisibleNote()
    {
        var parsed = BmsChartParser.Parse(["#BPM 120", "#00111:01", "#00131:02", "#00131:03"]);
        Assert.That(parsed.HitObjects.Single().SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
        Assert.That(parsed.InvisibleNotes.Single().SampleKey, Is.EqualTo(BmsChartParser.Enc("03")));
    }

    [Test]
    public void TestConversionShiftsClonesAndPrecomputesInvisibleNotes()
    {
        var decoded = BmsBeatmapDecoder.DecodeBytes(Encoding.UTF8.GetBytes("#BPM 120\n#00011:01\n#00031:02"));
        var converted = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        var invisible = converted.InvisibleNotes.Single();
        var original = ((IBmsBeatmap)decoded).InvisibleNotes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(invisible.StartTime, Is.EqualTo(2000));
            Assert.That(invisible.ScrollPositionAtStartTime, Is.EqualTo(converted.TimingMap.GetScrollPositionAtTime(2000)));
            Assert.That(invisible.Beatmap, Is.SameAs(converted));
            Assert.That(invisible, Is.Not.SameAs(original));
            Assert.That(converted.HitObjects, Has.Count.EqualTo(1));
            Assert.That(converted.BackgroundSampleEvents, Is.Empty);
        });
        new BmsModMirror().ApplyToBeatmap(converted);
        Assert.That(invisible.Column, Is.EqualTo(converted.HitObjects.Single().Column));
        Assert.That(original.Column, Is.EqualTo(1));
    }

    [Test]
    public void TestRandomBranchesOnlyExposeSelectedInvisibleNotes()
    {
        string[] lines = ["#BPM 120", "#RANDOM 2", "#IF 1", "#00131:01", "#ELSE", "#00149:02", "#ENDIF", "#ENDRANDOM"];
        var parsed = BmsChartParser.Parse(lines, randomValueSelector: _ => 1);
        Assert.That(parsed.InvisibleNotes.Single().SampleKey, Is.EqualTo(BmsChartParser.Enc("01")));
        Assert.That(parsed.TotalColumns, Is.EqualTo(6));
        Assert.That(BmsChartParser.ParseImportSummary(lines, randomValueSelector: _ => 1).Metadata.KeyCount, Is.EqualTo(6));
        Assert.That(BmsChartParser.ParseImportSummary(lines, randomValueSelector: _ => 2).Metadata.KeyCount, Is.EqualTo(16));
    }

    [Test]
    public void TestRandomSharesLaneMappingWithoutChangingPlayablePattern()
    {
        var visibleOnly = new BmsBeatmap { TotalColumns = 8, LayoutVariant = BmsLayoutVariant.Bme7K };
        var withHidden = new BmsBeatmap { TotalColumns = 8, LayoutVariant = BmsLayoutVariant.Bme7K };
        for (var i = 0; i < 5; i++)
        {
            visibleOnly.HitObjects.Add(new BmsNote { StartTime = 1000 + 200 * i, Column = 1 });
            withHidden.HitObjects.Add(new BmsNote { StartTime = 1000 + 200 * i, Column = 1 });
        }
        withHidden.InvisibleNotes = [new BmsInvisibleNote { StartTime = 1000, Column = 1 }, new BmsInvisibleNote { StartTime = 1100, Column = 2 }];
        new BmsModNoteRandom { Seed = { Value = 42 } }.ApplyToBeatmap(visibleOnly);
        new BmsModNoteRandom { Seed = { Value = 42 } }.ApplyToBeatmap(withHidden);
        Assert.That(withHidden.HitObjects.Select(n => n.Column), Is.EqualTo(visibleOnly.HitObjects.Select(n => n.Column)));
        Assert.That(withHidden.InvisibleNotes[0].Column, Is.EqualTo(withHidden.HitObjects[0].Column));
    }

    [Test]
    public void TestSampleRetentionIncludesInvisibleOnlyLanesAndVisiblePriority()
    {
        var beatmap = new BmsBeatmap
        {
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 1000, Column = 1, SampleKey = 1 },
                              new BmsInvisibleNote { StartTime = 2000, Column = 2, SampleKey = 2 }],
            HitObjects = [new BmsNote { StartTime = 2000, Column = 2, SampleKey = 3 }],
        };
        beatmap.HitObjects[0].Beatmap = beatmap;
        var usages = BmsGameplayAudioController.GetSampleUsages(beatmap).ToArray();
        Assert.That(usages.Single(u => u.SampleKey == 1).LatestTriggerTime, Is.EqualTo(double.MaxValue));
        Assert.That(usages.Single(u => u.SampleKey == 2).LatestTriggerTime, Is.EqualTo(2000));
        Assert.That(usages.Any(u => u.SampleKey == 3 && u.LatestTriggerTime == double.MaxValue), Is.True);
        Assert.That(usages.All(u => !u.ResumeAfterSeek), Is.True);
    }

    [Test]
    public void TestInvisibleNoteRandomFollowsHeldLongNoteLane()
    {
        var longNote = new BmsLongNote { StartTime = 1000, Duration = 3000, Column = 1 };
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 8,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = [longNote],
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 2000, Column = 1 }],
        };
        new BmsModNoteRandom { Seed = { Value = 42 } }.ApplyToBeatmap(beatmap);
        Assert.That(beatmap.InvisibleNotes.Single().Column, Is.EqualTo(longNote.Column));
    }
}
