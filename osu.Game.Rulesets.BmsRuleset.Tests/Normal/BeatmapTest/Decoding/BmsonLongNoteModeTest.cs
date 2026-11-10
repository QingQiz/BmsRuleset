using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonLongNoteModeTest
{
    [TestCase(0, BmsLongNoteMode.LongNote)]
    [TestCase(1, BmsLongNoteMode.LongNote)]
    [TestCase(2, BmsLongNoteMode.ChargeNote)]
    [TestCase(3, BmsLongNoteMode.HellChargeNote)]
    public void NoteOverridesTakePriorityOverTheChartDefault(int chartMode, BmsLongNoteMode expectedDefault)
    {
        var beatmap = convert(mixedChart(chartMode));
        var holds = beatmap.HitObjects.OfType<BmsLongNote>().ToArray();
        Assert.That(holds.Select(note => note.Mode), Is.EqualTo(new[]
        {
            BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote, BmsLongNoteMode.Undefined,
        }));
        Assert.That(holds.Select(note => note.EffectiveMode), Is.EqualTo(new[]
        {
            BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote, expectedDefault,
        }));
    }

    [Test]
    public void MixedModesDriveScoringReplayAndPracticeClones()
    {
        var decoded = BmsBeatmapDecoder.DecodeBytes(mixedChart(2), createInfo());
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        Assert.That(beatmap.CountScoringNotes(), Is.EqualTo(8));
        var replay = new BmsAutoGenerator(beatmap).Generate();
        var result = BmsReplaySimulation.Run(beatmap, replay, new ScoreInfo { BeatmapInfo = beatmap.BeatmapInfo });
        Assert.That(result.Judgements.Count(judgement => judgement.Result == HitResult.Perfect), Is.EqualTo(8));
        Assert.That(result.Gauges.Any(gauge => gauge.Time > 1000 && gauge.Time < 1500), Is.True);

        var practice = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings { StartTime = 1000, EndTime = 2500 });
        Assert.That(practice.HitObjects.OfType<BmsLongNote>().Select(note => note.EffectiveMode),
            Is.EqualTo(beatmap.HitObjects.OfType<BmsLongNote>().Select(note => note.EffectiveMode)));
        new BmsModHellChargeNote().ApplyToBeatmap(practice);
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().First().EffectiveMode, Is.EqualTo(BmsLongNoteMode.LongNote));
        Assert.That(decoded.HitObjects.OfType<BmsLongNote>().First().Mode, Is.EqualTo(BmsLongNoteMode.LongNote));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void ModeModsOverrideAllLongNotes(BmsLongNoteMode target)
    {
        var beatmap = convert(mixedChart(2));
        BmsModLongNoteModeBase mod = target switch
        {
            BmsLongNoteMode.LongNote => new BmsModLongNote(),
            BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
            _ => new BmsModHellChargeNote(),
        };
        mod.ApplyToBeatmap(beatmap);
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Select(note => note.Mode), Is.All.EqualTo(BmsLongNoteMode.Undefined));
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Select(note => note.EffectiveMode), Is.All.EqualTo(target));
    }

    [Test]
    public void Lr2OverridesPerNoteTypes()
    {
        var beatmap = convert(mixedChart(3));
        new BmsModLr2().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Select(note => note.EffectiveMode), Is.All.EqualTo(BmsLongNoteMode.LongNote));
    }

    [Test]
    public void InvertKeepsTheSourceLongNoteOverride()
    {
        var beatmap = convert(BmsonParserTest.Chart("""
            [{"name":"a.wav","notes":[{"x":1,"y":480,"l":240,"t":3},{"x":1,"y":960}]}]
            """));
        new BmsModInvert().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().EffectiveMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
    }

    [Test]
    public void LayeredNotesCompareEffectiveTypesAndIgnoreTapTypes()
    {
        var beatmap = convert(BmsonParserTest.Chart("""
            [{"name":"a.wav","notes":[{"x":1,"y":0,"t":1},{"x":1,"y":0,"t":3},{"x":2,"y":480,"l":240}]},
             {"name":"b.wav","notes":[{"x":1,"y":0,"t":2},{"x":2,"y":480,"l":240,"t":2}]}]
            """, info: """, "ln_type":2 """));
        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(2));
        Assert.That(beatmap.HitObjects.All(note => note.AdditionalSampleKeys.Count == 1), Is.True);
        Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().EffectiveMode, Is.EqualTo(BmsLongNoteMode.ChargeNote));
    }

    [Test]
    public void RelativeTotalCountsChargeTails()
    {
        var notes = string.Join(',', Enumerable.Range(0, 500).Select(index => $$"""{"x":1,"y":{{index * 480}},"l":240}"""));
        var result = BmsonParser.Parse(BmsonParserTest.Chart($$"""[{"name":"a.wav","notes":[{{notes}}]}]""", info: """, "ln_type":2 """));
        Assert.That(result.Total, Is.EqualTo(BmsGaugeCalculator.CalculateDefaultTotal(1000)));
    }

    [Test]
    public void ConflictingLayerTypesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(BmsonParserTest.Chart("""
            [{"name":"a.wav","notes":[{"x":1,"y":480,"l":240,"t":1}]},
             {"name":"b.wav","notes":[{"x":1,"y":480,"l":240,"t":2}]}]
            """)));
    }

    private static byte[] mixedChart(int chartMode) => BmsonParserTest.Chart("""
        [{"name":"a.wav","notes":[{"x":1,"y":480,"l":240,"t":1},{"x":2,"y":480,"l":240,"t":2},
         {"x":3,"y":480,"l":240,"t":3},{"x":4,"y":480,"l":240},{"x":5,"y":960}]}]
        """, info: $$""", "ln_type":{{chartMode}} """);

    private static BeatmapInfo createInfo()
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        return info;
    }

    private static BmsBeatmap convert(byte[] bytes) =>
        (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(bytes, createInfo()), new BmsRuleset()).Convert();
}
