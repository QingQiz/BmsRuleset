using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;

internal readonly record struct BmsPreviewTimelineEntry(double Time, ushort SampleKey, string SamplePath, int Volume, bool ResumeAfterSeek);

internal sealed record BmsEventPreviewTimeline(
    IReadOnlyList<BmsPreviewTimelineEntry> Entries,
    double Length,
    bool DeriveLengthFromSamples = false,
    bool ExtendLengthFromSamples = false)
{
    internal const double DEFAULT_LENGTH = 30000;

    internal static BmsEventPreviewTimeline CreateSingleFile(string samplePath) => new(
        [new BmsPreviewTimelineEntry(0, 0, samplePath, 100, true)],
        DEFAULT_LENGTH,
        DeriveLengthFromSamples: true);

    internal static BmsEventPreviewTimeline Create(IBeatmap beatmap, IBmsBeatmap bmsBeatmap, CancellationToken cancellationToken)
    {
        var entries = new List<BmsPreviewTimelineEntry>(bmsBeatmap.BackgroundSampleEvents.Count + beatmap.HitObjects.Count);

        foreach (var evt in bmsBeatmap.BackgroundSampleEvents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            addEntry(evt.Time, evt.SampleKey, evt.Volume, true);
        }

        // Preview includes player-triggered keysounds because it has no gameplay input path.
        foreach (var obj in beatmap.HitObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (obj is not BmsHitObject hit || hit is BmsLandmine)
                continue;

            if (hit.SampleKey is { } sampleKey)
                addEntry(hit.StartTime, sampleKey, hit.SampleVolume, false);
            if (hit is BmsLongNote { TailSampleKey: { } tailKey } hold)
                addEntry(hold.EndTime, tailKey, hold.TailSampleVolume, false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Both sorts affect coincident retriggers: retain their existing order across undefined-key filtering.
        entries.Sort((a, b) => a.Time.CompareTo(b.Time));
        entries.RemoveAll(entry => !bmsBeatmap.SampleDefinitions.ContainsKey(entry.SampleKey));
        return Create(entries, cancellationToken);

        void addEntry(double time, ushort key, int volume, bool resumeAfterSeek)
        {
            bmsBeatmap.SampleDefinitions.TryGetValue(key, out var path);
            entries.Add(new BmsPreviewTimelineEntry(time, key, path ?? string.Empty, volume, resumeAfterSeek));
        }
    }

    internal static BmsEventPreviewTimeline Create(List<BmsPreviewTimelineEntry> entries, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries.Sort((a, b) => a.Time.CompareTo(b.Time));

        if (entries.Count > 0 && entries[0].Time > 0)
        {
            var leadIn = entries[0].Time;

            for (var i = 0; i < entries.Count; i++)
                entries[i] = entries[i] with { Time = entries[i].Time - leadIn };
        }

        var length = entries.Count > 0 ? entries[^1].Time + 5000 : DEFAULT_LENGTH;
        return new BmsEventPreviewTimeline(entries, length, ExtendLengthFromSamples: true);
    }
}
