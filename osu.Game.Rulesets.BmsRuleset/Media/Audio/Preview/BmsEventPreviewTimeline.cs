using System;
using System.Collections.Generic;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal readonly record struct BmsPreviewTimelineEntry(double Time, ushort SampleKey, string SamplePath, int Volume, bool ResumeAfterSeek);

internal sealed record BmsEventPreviewTimeline(
    IReadOnlyList<BmsPreviewTimelineEntry> Entries,
    double Length,
    bool DeriveLengthFromTracks = false,
    bool RetainLoadedTracks = false)
{
    internal const double DEFAULT_LENGTH = 30000;

    internal static BmsEventPreviewTimeline CreateSingleFile(string samplePath) => new(
        [new BmsPreviewTimelineEntry(0, 0, samplePath, 100, true)],
        DEFAULT_LENGTH,
        DeriveLengthFromTracks: true,
        RetainLoadedTracks: true);

    internal static BmsEventPreviewTimeline Create(
        Func<CancellationToken, IReadOnlyList<BmsPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        CancellationToken cancellationToken) =>
        Create(() => sampleEventFactory(cancellationToken), sampleDefinitions, cancellationToken);

    internal static BmsEventPreviewTimeline Create(
        Func<IReadOnlyList<BmsPreviewSampleEvent>> sampleEventFactory,
        IReadOnlyDictionary<ushort, string> sampleDefinitions,
        CancellationToken cancellationToken = default)
    {
        List<BmsPreviewTimelineEntry> entries = [];

        foreach (var (evt, resumeAfterSeek) in sampleEventFactory())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sampleDefinitions.TryGetValue(evt.SampleKey, out var samplePath))
                entries.Add(new BmsPreviewTimelineEntry(evt.Time, evt.SampleKey, samplePath, evt.Volume, resumeAfterSeek));
        }

        cancellationToken.ThrowIfCancellationRequested();
        entries.Sort((a, b) => a.Time.CompareTo(b.Time));

        if (entries.Count > 0 && entries[0].Time > 0)
        {
            var leadIn = entries[0].Time;

            for (var i = 0; i < entries.Count; i++)
                entries[i] = entries[i] with { Time = entries[i].Time - leadIn };
        }

        var length = entries.Count > 0 ? entries[^1].Time + 5000 : DEFAULT_LENGTH;
        return new BmsEventPreviewTimeline(entries, length);
    }
}
