#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public class BmsonLocalCorpusTest
{
    private static IEnumerable<TestCaseData> charts()
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

    [TestCaseSource(nameof(charts))]
    public void ParseConvertPreviewPracticeAndAutoplay(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var parsed = BmsonParser.Parse(bytes);
        var summary = BmsonParser.ParseImportSummary(bytes);
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo)
        {
            Hash = "local-chart",
            BeatmapSet = new BeatmapSetInfo(),
        };
        info.BeatmapSet.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = info.Hash }, Path.GetFileName(path)));
        info.Metadata.Source = Path.GetDirectoryName(path)!;
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
    }
}
