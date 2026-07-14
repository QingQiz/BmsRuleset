using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Updates beatmap DifficultyName with difficulty table markers.
/// Marker format: " [★1 sl3 st5]" — space + bracket-wrapped space-separated list.
/// </summary>
public partial class DifficultyNameUpdater(RealmAccess realm, DifficultyTableStore store)
{
    private const char marker_ownership_sentinel = '\u200B';

    public static void GetDifficultyName(BeatmapInfo beatmap, out string markerStr)
    {
        markerStr = string.Empty;

        if (BmsRulesetRuntime.DifficultyTableStore == null)
        {
            return;
        }

        var markers = BmsRulesetRuntime.DifficultyTableStore.GetMarkers(beatmap.MD5Hash);

        markerStr = FormatMarkers(markers);
    }

    internal static string AddMarkerSuffix(string difficultyName, string markerStr) =>
        $"{difficultyName}{marker_ownership_sentinel} [{markerStr}]";

    /// <summary>
    /// Full rebuild — applies markers from every loaded table to the matching
    /// beatmaps. Only processes entries that are actually in tables —
    /// O(total-table-entries), not O(all-beatmaps-in-database).
    /// Each chunk is a separate realm.Write so the write mutex is held only briefly.
    /// </summary>
    public void RefreshAllMarkers(ProgressNotification? notification = null)
    {
        notification?.Text = "Collecting ...";

        List<(Guid, string)> collect = [];

        realm.Run(r =>
        {
            var allBmsBeatmaps = r.All<BeatmapInfo>().Filter("Ruleset.ShortName == 'bms'");
            foreach (var beatmap in allBmsBeatmaps)
            {
                var markers = store.GetMarkers(beatmap.MD5Hash);
                var markerStr = FormatMarkers(markers);
                var clean = ownedMarkerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);

                var res = markers.Count == 0
                    ? clean
                    : AddMarkerSuffix(clean, markerStr);

                if (res != beatmap.DifficultyName) collect.Add((beatmap.ID, res));
            }
        });

        var total = collect.Count;
        var processed = 0;
        notification?.Text = "Refreshing ...";
        notification?.Progress = 0;

        realm.Write(r =>
        {
            foreach (var collectItem in collect)
            {
                r.Find<BeatmapInfo>(collectItem.Item1)?.DifficultyName = collectItem.Item2;
                processed++;
                notification?.Text = $"Refreshing {processed}/{total}...";
                notification?.Progress = (float)processed / total;
            }
        });
    }

    internal static string FormatMarkers(IReadOnlyList<(DifficultyTable table, TableEntry entry)> markers) =>
        markers.Count == 0
            ? string.Empty
            : string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));

    [GeneratedRegex(@"\u200B\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex ownedMarkerSuffixRegex();
}
