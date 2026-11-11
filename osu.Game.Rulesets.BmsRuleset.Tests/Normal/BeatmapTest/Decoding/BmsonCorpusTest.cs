#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;

[TestFixture]
public class BmsonCorpusTest
{
    private static IEnumerable<TestCaseData> embeddedCharts()
    {
        using var manifest = JsonDocument.Parse(readResource("samples.json"));
        foreach (var sample in manifest.RootElement.EnumerateArray())
            yield return new TestCaseData(sample.Clone()).SetName($"EmbeddedBmson({sample.GetProperty("file").GetString()})");
    }

    private static IEnumerable<TestCaseData> localCharts()
    {
        var manifest = Environment.GetEnvironmentVariable("BMS_BMSON_CORPUS");
        if (string.IsNullOrEmpty(manifest))
        {
            yield return new TestCaseData(string.Empty).Ignore("Set BMS_BMSON_CORPUS to a text file listing local BMSON chart paths.");
            yield break;
        }

        foreach (var path in File.ReadLines(manifest).Where(path => !string.IsNullOrWhiteSpace(path)))
            yield return new TestCaseData(path).SetName($"LocalBmson({Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)})");
    }

    [TestCaseSource(nameof(embeddedCharts))]
    public void EmbeddedChartMatchesReference(JsonElement sample)
    {
        var file = sample.GetProperty("file").GetString()!;
        var bytes = readResource(file);
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Is.EqualTo(sample.GetProperty("sha256").GetString()));
        if (sample.TryGetProperty("error", out var error))
        {
            var exception = Assert.Throws<InvalidDataException>(() => BmsonParser.Parse(bytes));
            Assert.That(exception!.Message, Is.EqualTo(error.GetString()));
            return;
        }

        var beatmap = verifyChart(bytes, file);
        var expected = sample.GetProperty("expected");
        var holds = beatmap.HitObjects.OfType<BmsLongNote>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(beatmap.LayoutVariant, Is.EqualTo(BmsLayout.ParseModeHint(expected.GetProperty("mode").GetString()!)));
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(expected.GetProperty("objects").GetInt32()));
            Assert.That(holds, Has.Length.EqualTo(expected.GetProperty("longNotes").GetInt32()));
            Assert.That(new[] { BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote }
                .Select(mode => holds.Count(note => note.EffectiveMode == mode)),
                Is.EqualTo(expected.GetProperty("longNoteModes").EnumerateArray().Select(value => value.GetInt32())));
            Assert.That(holds.Sum(note => note.GetTailSamples().Count()), Is.EqualTo(expected.GetProperty("releaseSamples").GetInt32()));
            Assert.That(beatmap.HitObjects.Count(note => note is BmsLandmine), Is.EqualTo(expected.GetProperty("mines").GetInt32()));
            Assert.That(beatmap.InvisibleNotes, Has.Count.EqualTo(expected.GetProperty("invisible").GetInt32()));
            Assert.That(beatmap.HitObjects.Count(note => note.AdditionalSampleKeys.Count > 0), Is.EqualTo(expected.GetProperty("layeredNotes").GetInt32()));
            Assert.That(beatmap.TimingMap!.ScrollEvents, Has.Count.EqualTo(expected.GetProperty("scrollEvents").GetInt32()));
            Assert.That(beatmap.Bga.Events, Has.Count.EqualTo(expected.GetProperty("bgaEvents").GetInt32()));
            foreach (var point in expected.GetProperty("timing").EnumerateArray())
                Assert.That(beatmap.TimingMap.ProjectTickToTime(point.GetProperty("pulse").GetInt64()),
                    Is.EqualTo(point.GetProperty("milliseconds").GetDouble()).Within(0.001));
        });
    }

    [TestCaseSource(nameof(localCharts))]
    public void LocalChartPassesTheRuntimePipeline(string path) => verifyChart(File.ReadAllBytes(path), Path.GetFileName(path));

    private static byte[] readResource(string name)
    {
        using var stream = typeof(BmsonCorpusTest).Assembly.GetManifestResourceStream($"bmson_test_songs/{name}")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static BmsBeatmap verifyChart(byte[] bytes, string fileName)
    {
        var parsed = BmsonParser.Parse(bytes);
        var summary = BmsonParser.ParseImportSummary(bytes);
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo)
        {
            Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            MD5Hash = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
            BeatmapSet = new BeatmapSetInfo(),
        };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, fileName));
        BmsDifficultyInfo.FromChartMetadata(summary.Metadata).WriteToOsuDifficulty(info);
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(BmsBeatmapDecoder.DecodeBytes(bytes, info), new BmsRuleset()).Convert();

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(parsed.HitObjects.Count));
            Assert.That(summary.TotalObjectCount, Is.EqualTo(parsed.HitObjects.Count));
            Assert.That(summary.Metadata.LayoutVariant, Is.EqualTo(beatmap.LayoutVariant));
            Assert.That(beatmap.HitObjects.Select(note => note.StartTime), Is.Ordered);
            Assert.That(beatmap.HitObjects.All(note => double.IsFinite(note.StartTime) && note.Column >= 0 && note.Column < beatmap.TotalColumns), Is.True);
            Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().All(note => double.IsFinite(note.Duration) && note.Duration >= 0), Is.True);
        });

        var expectedSamples = beatmap.BackgroundSampleEvents.Count
                              + beatmap.HitObjects.Where(note => note is not BmsLandmine).Sum(note => note.GetSamples().Count())
                              + beatmap.HitObjects.OfType<BmsLongNote>().Sum(note => note.GetTailSamples().Count());
        var preview = BmsEventPreviewTimeline.Create(beatmap, beatmap, default);
        Assert.That(preview.Entries, Has.Count.EqualTo(expectedSamples));
        Assert.That(preview.Entries.Select(entry => entry.Time), Is.Ordered);
        Assert.That(BmsGameplayAudioController.GetSampleUsages(beatmap).All(usage => beatmap.SampleDefinitions.ContainsKey(usage.SampleKey)), Is.True);

        var bounds = BmsPracticeBeatmap.GetTimeBounds(beatmap);
        var practice = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings
        {
            StartTime = (bounds.StartTime + bounds.EndTime) / 2,
            EndTime = bounds.EndTime,
        });
        Assert.That(practice.HitObjects.All(note => ReferenceEquals(note.Beatmap, practice)), Is.True);
        Assert.That(practice.HitObjects.Any(note => beatmap.HitObjects.Contains(note)), Is.False);

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var simulation = BmsReplaySimulation.Run(beatmap, replay, new ScoreInfo { BeatmapInfo = beatmap.BeatmapInfo });
        Assert.That(simulation.Judgements.Count(result => result.Result == HitResult.Perfect), Is.EqualTo(beatmap.CountScoringNotes()));
        TestContext.Out.WriteLine($"{beatmap.LayoutVariant}: {beatmap.HitObjects.Count} objects, {beatmap.SampleDefinitions.Count} slices, {beatmap.Bga.Events.Count} BGA events");
        return beatmap;
    }
}
