using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;

internal sealed class BmsPracticeAudioPreparation(IEnumerable<BmsSampleUsage> usages)
{
    private readonly BmsSampleUsage[] backgroundEvents = usages.Where(usage => usage.ResumeAfterSeek).OrderBy(usage => usage.Time).ToArray();
    private IReadOnlyDictionary<int, BmsSampleDuration>? durations;

    internal void SetDurations(IReadOnlyDictionary<int, BmsSampleDuration> result) => durations = result;

    internal IReadOnlyDictionary<int, double> KnownLengths => durations?.Where(pair => pair.Value.State == BmsSampleDurationState.Known)
        .ToDictionary(pair => pair.Key, pair => pair.Value.Milliseconds) ?? new Dictionary<int, double>();

    internal BmsSampleUsage[] GetPlaybackUsages(IEnumerable<BmsSampleUsage> usages, double targetTime)
    {
        var offsets = GetResumeOffsets(targetTime);
        return usages.Where(usage => usage.ResumeAfterSeek ? usage.Time > targetTime : usage.LatestTriggerTime >= targetTime)
            .Concat(getLatestEvents(targetTime).Where(usage => offsets.ContainsKey(usage.SampleKey)))
            .ToArray();
    }

    internal IReadOnlyDictionary<int, string> GetDurationResources(double targetTime, IReadOnlyDictionary<int, string> resolvedResources)
        => getLatestEvents(targetTime).Where(usage => resolvedResources.ContainsKey(usage.SampleKey))
            .ToDictionary(usage => usage.SampleKey, usage => resolvedResources[usage.SampleKey]);

    internal IReadOnlyDictionary<int, double> GetResumeOffsets(double targetTime)
    {
        var result = new Dictionary<int, double>();
        foreach (var usage in getLatestEvents(targetTime))
        {
            double? length = null;
            if (durations != null && durations.TryGetValue(usage.SampleKey, out var duration))
            {
                if (duration.State == BmsSampleDurationState.Failed)
                    continue;
                if (duration.State == BmsSampleDurationState.Known)
                    length = duration.Milliseconds;
            }
            var offset = targetTime - usage.Time;
            // Unknown durations need decoder preparation before they can be safely excluded.
            if (length == null || offset < length)
                result[usage.SampleKey] = offset;
        }

        return result;
    }

    private IEnumerable<BmsSampleUsage> getLatestEvents(double targetTime)
    {
        var seen = new HashSet<int>();
        for (var i = backgroundEvents.Length - 1; i >= 0; i--)
        {
            var usage = backgroundEvents[i];
            if (usage.Time <= targetTime && seen.Add(usage.SampleKey))
                yield return usage;
        }
    }
}
