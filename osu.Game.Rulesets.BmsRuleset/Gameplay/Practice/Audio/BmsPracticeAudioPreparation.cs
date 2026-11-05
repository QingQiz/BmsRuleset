using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;

internal sealed class BmsPracticeAudioPreparation(IEnumerable<BmsSampleUsage> usages)
{
    private readonly BmsSampleUsage[] backgroundEvents = usages.Where(usage => usage.ResumeAfterSeek).OrderBy(usage => usage.Time).ToArray();
    private IReadOnlyDictionary<ushort, BmsSampleDuration>? durations;

    internal double? GetKnownDuration(ushort key)
        => durations != null && durations.TryGetValue(key, out var duration) && duration.State == BmsSampleDurationState.Known
            ? duration.Milliseconds : null;

    internal void SetDurations(IReadOnlyDictionary<ushort, BmsSampleDuration> result) => durations = result;

    internal IReadOnlyDictionary<ushort, double> KnownLengths => durations?.Where(pair => pair.Value.State == BmsSampleDurationState.Known)
        .ToDictionary(pair => pair.Key, pair => pair.Value.Milliseconds) ?? new Dictionary<ushort, double>();

    internal BmsSampleUsage[] GetPlaybackUsages(IEnumerable<BmsSampleUsage> usages, double targetTime)
    {
        var offsets = GetResumeOffsets(targetTime, new Dictionary<ushort, double>());
        return usages.Where(usage => usage.ResumeAfterSeek ? usage.Time > targetTime : usage.LatestTriggerTime >= targetTime)
            .Concat(getLatestEvents(targetTime).Where(usage => offsets.ContainsKey(usage.SampleKey)))
            .ToArray();
    }

    internal IReadOnlyDictionary<ushort, string> GetDurationResources(double targetTime, IReadOnlyDictionary<ushort, string> resolvedResources)
        => getLatestEvents(targetTime).Where(usage => resolvedResources.ContainsKey(usage.SampleKey))
            .ToDictionary(usage => usage.SampleKey, usage => resolvedResources[usage.SampleKey]);

    internal IReadOnlyDictionary<ushort, double> GetResumeOffsets(double targetTime, IReadOnlyDictionary<ushort, double> decodedLengths)
    {
        var result = new Dictionary<ushort, double>();
        foreach (var usage in getLatestEvents(targetTime))
        {
            if (durations != null && durations.TryGetValue(usage.SampleKey, out var duration) && duration.State == BmsSampleDurationState.Failed)
                continue;

            var length = decodedLengths.TryGetValue(usage.SampleKey, out var decodedLength) ? decodedLength : GetKnownDuration(usage.SampleKey);
            var offset = targetTime - usage.Time;
            // Unknown durations need decoder preparation before they can be safely excluded.
            if (length == null || offset < length)
                result[usage.SampleKey] = offset;
        }
        return result;
    }

    private IEnumerable<BmsSampleUsage> getLatestEvents(double targetTime)
    {
        var seen = new HashSet<ushort>();
        for (var i = backgroundEvents.Length - 1; i >= 0; i--)
        {
            var usage = backgroundEvents[i];
            if (usage.Time <= targetTime && seen.Add(usage.SampleKey))
                yield return usage;
        }
    }
}
