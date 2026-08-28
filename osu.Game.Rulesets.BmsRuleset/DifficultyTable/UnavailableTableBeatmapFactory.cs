using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

internal static class UnavailableTableBeatmapFactory
{
    private const string source_prefix = "bms-table-unavailable:";

    internal static IReadOnlyList<BeatmapSetInfo> Create(DifficultyTableStore store, RulesetInfo ruleset, IEnumerable<BeatmapInfo> localBeatmaps)
    {
        var localHashes = localBeatmaps.Select(beatmap => beatmap.MD5Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sets = new List<BeatmapSetInfo>();

        foreach (var table in store.Tables)
        {
            foreach (var entry in table.Entries.Where(entry => !localHashes.Contains(entry.Md5Hash)))
            {
                if (!localHashes.Add(entry.Md5Hash))
                    continue;

                var beatmap = new BeatmapInfo(ruleset)
                {
                    DifficultyName = $"{table.Symbol}{entry.Level}",
                    MD5Hash = entry.Md5Hash,
                    Hash = entry.Sha256Hash ?? entry.Md5Hash,
                    Metadata = new BeatmapMetadata
                    {
                        Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Md5Hash : entry.Title,
                        Artist = entry.Artist ?? string.Empty,
                        Source = source_prefix + entry.Md5Hash,
                    },
                };
                var set = new BeatmapSetInfo([beatmap]);
                beatmap.BeatmapSet = set;
                sets.Add(set);
            }
        }

        return sets;
    }

    internal static bool IsUnavailable(BeatmapInfo beatmap) =>
        beatmap.Metadata.Source.StartsWith(source_prefix, StringComparison.Ordinal);

    internal static UnavailableTableEntry? Resolve(BeatmapInfo beatmap, DifficultyTableStore? store)
    {
        if (!IsUnavailable(beatmap) || store == null)
            return null;

        return Resolve(beatmap.MD5Hash, store);
    }

    internal static UnavailableTableEntry? Resolve(string hash, DifficultyTableStore? store)
    {
        if (store == null)
            return null;

        foreach (var table in store.Tables)
        {
            var entry = table.Entries.FirstOrDefault(entry =>
                string.Equals(entry.Md5Hash, hash, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Sha256Hash, hash, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
                return new UnavailableTableEntry(entry, table);
        }

        return null;
    }

    internal static string? GetDownloadUrl(TableEntry entry) =>
        validWebUrl(entry.Url) ?? validWebUrl(entry.UrlDiff);

    private static string? validWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;
}
