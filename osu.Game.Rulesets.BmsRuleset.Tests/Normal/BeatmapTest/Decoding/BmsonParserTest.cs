using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonParserTest
{
    internal static byte[] Chart(string channels = "[]", string events = "", string info = "") =>
        Encoding.UTF8.GetBytes($$"""
                                 {"version":"1.0.0","info":{"title":"Song [title]","artist":"Artist","init_bpm":120{{info}}},
                                  "sound_channels":{{channels}}{{events}}}
                                 """);

    [Test]
    public void SlicesFollowDistinctBoundariesAndRestartWithinEachChannel()
    {
        var result = BmsonParser.Parse(Chart("""
                                             [{"name":"stem.wav","notes":[
                                               {"x":1,"y":240},{"x":2,"y":360,"c":true},{"x":0,"y":360,"c":true},
                                               {"x":3,"y":720},{"x":4,"y":840,"c":true},{"x":5,"y":1200,"c":true}]},
                                              {"name":"stem.wav","notes":[{"x":0,"y":240,"c":true}]}]
                                             """));
        var clips = result.SampleDefinitions.Values.ToArray();
        Assert.That(clips.Select(clip => clip.StartOffset), Is.EqualTo(new[] { 0, 250, 0, 250, 1000, 0 }));
        Assert.That(clips.Select(clip => clip.EndOffset), Is.EqualTo(new double?[] { 250, 1000, 250, 1000, null, null }));
        Assert.That(result.BackgroundSampleEvents, Has.Count.EqualTo(1), "The playable use suppresses the BGM use of the same slice.");
        Assert.That(result.BackgroundSampleEvents[0].SampleKey, Is.Not.EqualTo(result.HitObjects[0].SampleKey), "Separate channels retain independent voices.");
    }

    [Test]
    public void MixedContinuationRestartsOnceForTheWholePulse()
    {
        var result = BmsonParser.Parse(Chart("""
                                             [{"name":"stem.wav","notes":[
                                               {"x":1,"y":0},{"x":2,"y":240,"c":true},{"x":3,"y":240,"c":false},
                                               {"x":4,"y":480,"c":true}]}]
                                             """));
        Assert.That(result.HitObjects[1].SampleKey, Is.EqualTo(result.HitObjects[2].SampleKey));
        Assert.That(result.SampleDefinitions.Values.Select(clip => clip.StartOffset), Is.EqualTo(new[] { 0, 0, 500 }));
    }

    [Test]
    public void TimingUsesLastBpmAdditiveStopsAndPreStopNoteTimes()
    {
        var result = BmsonParser.Parse(Chart("""
                                             [{"name":"stem.wav","notes":[{"x":1,"y":240,"l":480},{"x":2,"y":720,"c":true}]}]
                                             """, """
                                                  ,"bpm_events":[{"y":240,"bpm":60},{"y":240,"bpm":240}],
                                                  "stop_events":[{"y":240,"duration":240},{"y":240,"duration":480}],
                                                  "bga":{"bga_header":[{"id":100000,"name":"layer.png"}],"layer_events":[{"y":240,"id":100000}]}
                                                  """));
        Assert.That(result.HitObjects[0].StartTime, Is.EqualTo(500));
        Assert.That(result.HitObjects[0].Duration, Is.EqualTo(1250));
        Assert.That(result.HitObjects[1].StartTime, Is.EqualTo(1750));
        Assert.That(result.SampleDefinitions[1].EndOffset, Is.EqualTo(1250));
        Assert.That(result.TimingMap.GetScrollPositionAtTime(800), Is.EqualTo(result.TimingMap.GetScrollPositionAtTime(500)));
        Assert.That(result.Bga.Events.Single().Time, Is.EqualTo(500));
        Assert.That(result.Bga.BitmapDefinitions[result.Bga.Events.Single().DefinitionKey], Is.EqualTo("layer.png"));
    }

    [TestCase("beat-5k", 8, 0, 6)]
    [TestCase("beat-7k", 7, 7, 8)]
    [TestCase("beat-7k", 8, 0, 8)]
    [TestCase("beat-10k", 9, 6, 12)]
    [TestCase("beat-10k", 16, 11, 12)]
    [TestCase("beat-14k", 9, 8, 16)]
    [TestCase("beat-14k", 16, 15, 16)]
    [TestCase("popn-9k", 9, 8, 9)]
    public void MapsCanonicalLanes(string mode, int lane, int column, int columns)
    {
        var result = BmsonParser.Parse(Chart($$"""[{"name":"a.wav","notes":[{"x":{{lane}},"y":480}]}]""",
            info: $$""","mode_hint":"{{mode}}","resolution":480"""));
        Assert.That(result.HitObjects.Single().Column, Is.EqualTo(column));
        Assert.That(result.HitObjects.Single().StartTime, Is.EqualTo(500));
        Assert.That(result.TotalColumns, Is.EqualTo(columns));
    }

    [TestCase("", new long[] { 0, 960 })]
    [TestCase(",\"lines\":null", new long[] { 0, 960 })]
    [TestCase(",\"lines\":[]", new long[0])]
    [TestCase(",\"lines\":[{\"y\":240},{\"y\":720}]", new long[] { 240, 720 })]
    public void BarLinesPreserveMissingEmptyAndExplicitForms(string lines, long[] expected)
        => Assert.That(BmsonParser.Parse(Chart(events: lines)).TimingMap.BarLines, Is.EqualTo(expected));

    [Test]
    public void LayeredNotesSurviveConversionPreviewPracticeAndKeysoundMods()
    {
        var bytes = Chart("""
                          [{"name":"a.wav","notes":[{"x":8,"y":240}]},
                           {"name":"b.wav","notes":[{"x":8,"y":240}]}]
                          """, info: ""","chart_name":"ANOTHER","subtitle":"Song subtitle","judge_rank":150,"total":50""");
        var info = new BeatmapInfo { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, info);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(1));
        Assert.That(beatmap.HitObjects[0].SampleKey, Is.EqualTo(1));
        Assert.That(beatmap.HitObjects[0].AdditionalSampleKeys, Is.EqualTo(new[] { 2 }));
        Assert.That(beatmap.HitObjects[0].EffectiveJudgementRate, Is.EqualTo(1.5));
        Assert.That(beatmap.Total, Is.EqualTo(BmsGaugeCalculator.CalculateDefaultTotal(1) / 2));
        Assert.That(beatmap.BeatmapInfo.DifficultyName, Is.EqualTo("ANOTHER"));
        Assert.That(BmsDifficultyInfo.FromOsuDifficulty(beatmap.Difficulty).ExRank, Is.EqualTo(200));
        var summary = BmsonParser.ParseImportSummary(bytes);
        Assert.That(summary.TotalObjectCount, Is.EqualTo(1));
        Assert.That(summary.Metadata.RawTitle, Is.EqualTo("Song [title]"));
        Assert.That(summary.Metadata.DifficultyName, Is.EqualTo("ANOTHER"));
        Assert.That(summary.Metadata.ExRank, Is.EqualTo(200));
        Assert.That(BmsGameplayAudioController.GetSampleUsages(beatmap).Select(usage => usage.SampleKey), Is.EquivalentTo(new[] { 1, 2 }));
        beatmap.HitObjects[0].SampleVolume = 70;
        var preview = BmsEventPreviewTimeline.Create(beatmap, beatmap, default);
        Assert.That(preview.Entries, Has.Count.EqualTo(2));
        Assert.That(preview.Entries.Select(entry => entry.Volume), Is.EqualTo(new[] { 70, 70 }));
        Assert.That(preview.Entries.All(evt => evt.ResumeAfterSeek), Is.True);
        var clone = beatmap.HitObjects[0].CloneForConversion();
        Assert.That(clone.AdditionalSampleKeys, Is.EqualTo(beatmap.HitObjects[0].AdditionalSampleKeys));
        new BmsModBackgroundKeysound().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.BackgroundSampleEvents, Has.Count.EqualTo(2));
        Assert.That(beatmap.BackgroundSampleEvents.Select(evt => evt.Volume), Is.EqualTo(new[] { 70, 70 }));
        Assert.That(beatmap.HitObjects[0].SampleKey, Is.Null);
        Assert.That(beatmap.HitObjects[0].AdditionalSampleKeys, Is.Empty);
    }

    [Test]
    public void ReferenceBpmModesUseEffectiveTempos()
    {
        var chart = Chart("""[{"name":"a.wav","notes":[{"x":1,"y":240}]}]""",
            ""","bpm_events":[{"y":240,"bpm":180}]""");
        var info = new BeatmapInfo { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(chart, info);
        foreach (var (mode, expected) in new[]
                 {
                     (BmsReferenceBpmMode.StartBpm, 120), (BmsReferenceBpmMode.MainBpm, 180),
                     (BmsReferenceBpmMode.MaxBpm, 180), (BmsReferenceBpmMode.MinBpm, 120),
                 })
        {
            var converted = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { ReferenceBpmMode = mode }.Convert();
            Assert.That(converted.TimingMap!.ScrollReferenceBpm, Is.EqualTo(expected), mode.ToString());
            Assert.That(converted.BeatmapInfo.DifficultyName, Is.EqualTo("chart"));
        }
        Assert.That(((IBmsBeatmap)decoded).TimingMap!.ScrollReferenceBpm, Is.EqualTo(120));
    }

    [TestCase("../a.wav")]
    [TestCase("/a.wav")]
    [TestCase("C:/a.wav")]
    public void RejectsUnsafeResources(string path)
        => Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(Chart($$"""[{"name":"{{path}}","notes":[]}]""")));

    [TestCase("version", "\"0.21\"")]
    [TestCase("version", "null")]
    [TestCase("info.init_bpm", "0")]
    [TestCase("info.init_bpm", "null")]
    [TestCase("info.mode_hint", "\"generic-8keys\"")]
    [TestCase("info.resolution", "0.5")]
    public void RejectsUnsupportedOrInvalidCharts(string field, string json)
    {
        var chart = JsonNode.Parse(Chart())!;
        if (field.StartsWith("info."))
            chart["info"]![field[5..]] = JsonNode.Parse(json);
        else
            chart[field] = JsonNode.Parse(json);
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(Encoding.UTF8.GetBytes(chart.ToJsonString())));
    }

    [Test]
    public void MissingVersionAndConflictingLayersAreRejected()
    {
        var chart = JsonNode.Parse(Chart())!.AsObject();
        chart.Remove("version");
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(Encoding.UTF8.GetBytes(chart.ToJsonString())));
        Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(Chart("""
                                                                          [{"name":"a.wav","notes":[{"x":1,"y":0,"l":240}]},
                                                                           {"name":"b.wav","notes":[{"x":1,"y":0,"l":480}]}]
                                                                          """)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => BmsonParser.Parse(Chart(), cancellationToken: cancellation.Token));
    }

    [Test]
    public void ExplicitZeroTotalKeepsSuccessfulJudgementsFromRefillingNormalGauge()
    {
        var result = BmsonParser.Parse(Chart("""[{"name":"a.wav","notes":[{"x":1,"y":0}]}]""", info: ""","total":0"""));
        var profile = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);
        var calculator = new BmsGaugeCalculator(profile, result.Total, 1);
        Assert.That(calculator.Total, Is.Zero);
        Assert.That(calculator.GetDeltaFor(osu.Game.Rulesets.Scoring.HitResult.Perfect, .5), Is.Zero);
        Assert.That(BmsonParser.Parse(Chart(info: ""","total":-50""")).Total,
            Is.EqualTo(BmsGaugeCalculator.CalculateDefaultTotal(0) / 2));
    }

    [Test]
    public void ConvertedExRankUsesExistingDisplayAttributes()
    {
        var info = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, KeyCount = 8, ExRank = 200 }.WriteToOsuDifficulty(info);
        var rank = new BmsRuleset().GetBeatmapAttributesForDisplay(info, []).Single(attribute => attribute.Acronym == "EX");
        Assert.That(rank.AdjustedValue, Is.EqualTo(200));
        Assert.That(rank.AdditionalMetrics.Single(metric => metric.Name.ToString() == "Normal note PGREAT").Value.ToString(),
            Is.EqualTo("-30 to +30 ms"));
    }

    [TestCase("beat-5k", 0, 0, 0)]
    [TestCase("beat-7k", .5, 2d / 3, .005)]
    [TestCase("beat-7k", 50, 200d / 3, .5)]
    [TestCase("beat-7k", 75, 100, .75)]
    [TestCase("beat-7k", 100, 400d / 3, 1)]
    [TestCase("beat-7k", 150, 200, 1.5)]
    [TestCase("beat-7k", 150.5, 602d / 3, 1.505)]
    [TestCase("beat-14k", 100, 400d / 3, 1)]
    [TestCase("popn-9k", 0, 0, 0)]
    [TestCase("popn-9k", 70, 100, .7)]
    [TestCase("popn-9k", 100, 1000d / 7, 1)]
    public void JudgeRankConvertsToContinuousBmsExRank(string mode, double judgeRank, double expectedExRank, double expectedRate)
    {
        var bytes = Chart("""[{"name":"a.wav","notes":[{"x":1,"y":0}]}]""",
            info: $$""","mode_hint":"{{mode}}","judge_rank":{{judgeRank.ToString(CultureInfo.InvariantCulture)}}""");
        var parsed = BmsonParser.Parse(bytes);
        Assert.That(parsed.DefaultExRank, Is.EqualTo(expectedExRank).Within(1e-10));
        Assert.That(BmsJudgementProfileProvider.RateForExRank(parsed.LayoutVariant, parsed.DefaultExRank!.Value), Is.EqualTo(expectedRate).Within(1e-10));

        var info = new BeatmapInfo { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, info);
        var converted = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        Assert.That(converted.HitObjects.Single().EffectiveJudgementRate, Is.EqualTo(expectedRate).Within(1e-10));
        Assert.That(BmsDifficultyInfo.FromOsuDifficulty(converted.Difficulty).ExRank, Is.EqualTo(expectedExRank).Within(1e-4));

        var adjusted = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()) { JudgementRateCalculator = (_, _, _) => .5 }.Convert();
        Assert.That(adjusted.HitObjects.Single().EffectiveJudgementRate, Is.EqualTo(.5), "The converted chart follows the same calculator contract as BMS.");
        Assert.That(((BmsHitObject)decoded.HitObjects.Single()).JudgementRate, Is.NaN);
    }

    [TestCase("#TITLE Song [HYPER]", "", "chart", "HYPER")]
    [TestCase("#TITLE Song [HYPER]\n#SUBTITLE [ANOTHER]", "ANOTHER", "ANOTHER", "ANOTHER")]
    [TestCase("#TITLE Song\n#PLAYLEVEL 12", "", "chart", "chart")]
    public void BmsParsingLeavesDisplayNameResolutionToLoading(string headers, string explicitName, string directName, string importedName)
    {
        var bytes = Encoding.UTF8.GetBytes(headers + "\n#BPM 120\n#00011:01");
        var parsed = BmsChartParser.Parse(BmsChartParser.ReadAllLines(bytes), "chart.bms");
        var summary = BmsChartParser.ParseImportSummary(bytes, "chart.bms");
        Assert.That(parsed.ExplicitDifficultyName, Is.EqualTo(explicitName));
        Assert.That(summary.Metadata.DifficultyName, Is.EqualTo(explicitName));
        var info = new BeatmapInfo { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bms"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, info);
        Assert.That(decoded.BeatmapInfo.DifficultyName, Is.EqualTo(directName));
        Assert.That(new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert().BeatmapInfo.DifficultyName, Is.EqualTo(directName));

        var importedInfo = info.Clone();
        importedInfo.DifficultyName = importedName;
        var imported = BmsBeatmapDecoder.DecodeBytes(bytes, importedInfo);
        Assert.That(imported.BeatmapInfo.DifficultyName, Is.EqualTo(importedName));
        Assert.That(new BmsBeatmapConverter(imported, new BmsRuleset()).Convert().BeatmapInfo.DifficultyName, Is.EqualTo(importedName));
    }

    [TestCase("Normal")]
    [TestCase("ANOTHER\u200B [★12]")]
    public void ExistingBmsonDifficultyNameSurvivesDecoding(string name)
    {
        var info = new BeatmapInfo { Hash = "chart", BeatmapSet = new BeatmapSetInfo(), DifficultyName = name };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(Chart(info: ",\"chart_name\":\"Source chart name\""), info);
        Assert.That(decoded.BeatmapInfo.DifficultyName, Is.EqualTo(name));
        Assert.That(new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert().BeatmapInfo.DifficultyName, Is.EqualTo(name));
    }

    [Test]
    public void PracticeAndScratchModsPreserveAllLayeredSamples()
    {
        var bytes = Chart("""
                          [{"name":"a.wav","notes":[{"x":8,"y":0},{"x":1,"y":480,"c":true}]},
                           {"name":"b.wav","notes":[{"x":8,"y":0},{"x":1,"y":480,"c":true}]}]
                          """);
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo) { Hash = "chart", BeatmapSet = new BeatmapSetInfo() };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, "chart.bmson"));
        var decoded = BmsBeatmapDecoder.DecodeBytes(bytes, info);
        var full = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        var practice = BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 500, EndTime = 1500 });
        Assert.That(practice.BackgroundSampleEvents.Select(evt => evt.SampleKey), Is.EquivalentTo(new[] { 1, 3 }));
        Assert.That(practice.HitObjects.Single().SampleKey, Is.EqualTo(2));
        Assert.That(practice.HitObjects.Single().AdditionalSampleKeys, Is.EqualTo(new[] { 4 }));
        Assert.That(practice.Total, Is.EqualTo(full.Total));
        new BmsModHideScratch().ApplyToBeatmap(full);
        Assert.That(full.BackgroundSampleEvents.Select(evt => evt.SampleKey), Is.EquivalentTo(new[] { 1, 3 }));
        Assert.That(full.HitObjects.Single().SampleKey, Is.EqualTo(2));
        Assert.That(full.HitObjects.Single().AdditionalSampleKeys, Is.EqualTo(new[] { 4 }));
    }
}
