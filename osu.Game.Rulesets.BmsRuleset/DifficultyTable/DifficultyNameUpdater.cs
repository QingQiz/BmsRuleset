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
    public static void GetDifficultyName(BeatmapInfo beatmap, out string markerStr)
    {
        markerStr = string.Empty;

        if (BmsRuleset.DifficultyTableStore == null)
        {
            return;
        }

        var markers = BmsRuleset.DifficultyTableStore.GetMarkers(beatmap.MD5Hash);

        markerStr = markers.Count == 0
            ? string.Empty
            : string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
    }

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
                var clean = markerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);
                var markers = store.GetMarkers(beatmap.MD5Hash);
                var res = string.Empty;

                if (markers.Count == 0)
                {
                    if (beatmap.DifficultyName != clean) res = clean;
                }
                else
                {
                    var markerStr = string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
                    res = $"{clean} [{markerStr}]";
                }

                if (res != beatmap.DifficultyName) collect.Add((beatmap.ID, res));
            }
        });

        var total = collect.Count;
        var processed = 0;
        notification?.Text = "Refreshing ...";

        realm.Write(r =>
        {
            foreach (var collectItem in collect)
            {
                r.Find<BeatmapInfo>(collectItem.Item1)?.DifficultyName = collectItem.Item2;
                processed += 1;
                notification?.Progress = (float)processed / total;
            }
        });
    }

    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();
}
