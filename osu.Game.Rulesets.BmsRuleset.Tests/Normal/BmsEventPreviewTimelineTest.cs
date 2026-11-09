using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsEventPreviewTimelineTest
{
    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(200, false)]
    [TestCase(200, true)]
    public void PreviewRecoveryIsIndependentOfSampleOffset(double offset, bool resume)
    {
        var beatmap = new BmsBeatmap
        {
            SampleDefinitions = new Dictionary<int, BmsSampleDefinition> { [1] = new("stem.wav", offset, ResumeInPreview: resume) },
            HitObjects = [new BmsNote { StartTime = 1000, SampleKey = 1 }],
        };
        var timeline = BmsEventPreviewTimeline.Create(beatmap, beatmap, default);
        Assert.That(timeline.Entries.Single().ResumeAfterSeek, Is.EqualTo(resume));
        Assert.That(timeline.Entries.Single().Sample.StartOffset, Is.EqualTo(offset));
    }

    [Test]
    public void TestMixedSourcesKeepAudioIdentityVolumeAndRecoveryRules()
    {
        var beatmap = new BmsBeatmap
        {
            SampleDefinitions = new Dictionary<int, BmsSampleDefinition> { [1] = new("shared.wav"), [2] = new("shared.wav"), [3] = new("tail.wav") },
            BackgroundSampleEvents = [new(0, 9, 100), new(1000, 1, 80)],
            HitObjects =
            [
                new BmsNote { StartTime = 1250, SampleKey = 2, SampleVolume = 70 },
                new BmsLongNote { StartTime = 1500, Duration = 1000, SampleKey = 1, SampleVolume = 60, TailSampleKey = 3, TailSampleVolume = 50 },
                new BmsLandmine { StartTime = 1750, SampleKey = 1 },
                new BmsNote { StartTime = 2000, SampleKey = 9 },
            ],
        };

        var timeline = BmsEventPreviewTimeline.Create(beatmap, beatmap, default);

        Assert.That(timeline.Entries, Is.EqualTo(new BmsPreviewTimelineEntry[]
        {
            new(0, 1, new BmsSampleDefinition("shared.wav"), 80, true),
            new(250, 2, new BmsSampleDefinition("shared.wav"), 70, false),
            new(500, 1, new BmsSampleDefinition("shared.wav"), 60, false),
            new(1500, 3, new BmsSampleDefinition("tail.wav"), 50, false),
        }));
        Assert.That(timeline.Length, Is.EqualTo(6500));
        Assert.That(timeline.ExtendLengthFromSamples, Is.True);
    }

    [Test]
    public void TestCoincidentRetriggersMatchPreviousPipeline()
    {
        var beatmap = new BmsBeatmap
        {
            SampleDefinitions = new Dictionary<int, BmsSampleDefinition> { [1] = new("shared.wav"), [2] = new("shared.wav") },
        };
        beatmap.BackgroundSampleEvents = Enumerable.Range(0, 80)
            .Select(index => new BmsSampleEvent(1000 + index % 3 * 100, (ushort)(index % 3 + 1), index + 1)).ToArray();
        beatmap.HitObjects = Enumerable.Range(0, 80).Select(index => (BmsHitObject)new BmsLongNote
        {
            StartTime = 1000 + index % 3 * 100, Duration = 100, SampleKey = (ushort)(index % 3 + 1),
            SampleVolume = index + 10, TailSampleKey = 1, TailSampleVolume = index + 20,
        }).ToList();

        assertPreviousPipeline(beatmap, beatmap);
    }

    [TestCase("Aleph-0 (by LeaF)", "_7NORMAL.bms")]
    [TestCase("Aleph-0 (by LeaF)", "_14ANOTHER.bms")]
    [TestCase("Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml")]
    public void TestRealChartMatchesPreviousPipeline(string directory, string filename)
    {
        var chart = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, directory, filename)),
            randomValueSelector: _ => 1);
        assertPreviousPipeline(chart, (IBmsBeatmap)chart);
    }

    [Test]
    public void TestCancelledBuildDoesNotPublishTimeline()
    {
        var beatmap = new BmsBeatmap { BackgroundSampleEvents = [new(1000, 1, 100)] };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => BmsEventPreviewTimeline.Create(beatmap, beatmap, cancellation.Token));
    }

    private static void assertPreviousPipeline(IBeatmap chart, IBmsBeatmap data)
    {
        // Reference the previous two-list pipeline to catch changes in same-time retrigger order.
        var events = data.BackgroundSampleEvents.Select(evt => (Event: evt, Resume: true)).ToList();
        foreach (var hit in chart.HitObjects.OfType<BmsHitObject>().Where(hit => hit is not BmsLandmine))
        {
            if (hit.SampleKey is { } key)
                events.Add((new BmsSampleEvent(hit.StartTime, key, hit.SampleVolume), false));
            if (hit is BmsLongNote { TailSampleKey: { } tailKey } hold)
                events.Add((new BmsSampleEvent(hold.EndTime, tailKey, hold.TailSampleVolume), false));
        }
        events.Sort((a, b) => a.Event.Time.CompareTo(b.Event.Time));
        var expected = events.Where(item => data.SampleDefinitions.ContainsKey(item.Event.SampleKey))
            .Select(item => new BmsPreviewTimelineEntry(item.Event.Time, item.Event.SampleKey,
                data.SampleDefinitions[item.Event.SampleKey], item.Event.Volume, item.Resume)).ToList();
        expected.Sort((a, b) => a.Time.CompareTo(b.Time));
        var leadIn = expected.Count > 0 ? Math.Max(0, expected[0].Time) : 0;
        for (var index = 0; index < expected.Count; index++)
            expected[index] = expected[index] with { Time = expected[index].Time - leadIn };

        var timeline = BmsEventPreviewTimeline.Create(chart, data, default);
        Assert.That(timeline.Entries, Is.EqualTo(expected));
        Assert.That(timeline.Length, Is.EqualTo(expected.Count > 0 ? expected[^1].Time + 5000 : BmsEventPreviewTimeline.DEFAULT_LENGTH));
    }
}
