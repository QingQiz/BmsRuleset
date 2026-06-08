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
        if (notification != null)
            notification.Text = "Refreshing markers...";

        realm.Run(r =>
        {
            const int batch_size = 100;

            var allBmsBeatmaps = r.All<BeatmapInfo>().Filter("Ruleset.ShortName == 'bms'");
            var total = allBmsBeatmaps.Count();

            if (total == 0)
            {
                if (notification != null)
                    notification.Progress = 1;
                return;
            }

            var processed = 0;
            var batch = new List<(BeatmapInfo, string)>(batch_size);
            foreach (var beatmap in allBmsBeatmaps)
            {
                if (batch.Count == batch_size) updateBatch();

                var clean = markerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);
                var markers = store.GetMarkers(beatmap.MD5Hash);

                if (markers.Count == 0)
                {
                    if (beatmap.DifficultyName != clean) batch.Add((beatmap, clean));
                }
                else
                {
                    var markerStr = string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
                    batch.Add((beatmap, $"{clean} [{markerStr}]"));
                }

                processed++;
                if (notification != null)
                    notification.Progress = (float)processed / total;
            }

            updateBatch();

            if (notification != null)
                notification.Progress = 1;
            return;

            void updateBatch()
            {
                r.Write(() => batch.ForEach(b => b.Item1.DifficultyName = b.Item2));
                batch.Clear();
                if (notification != null) notification.Text = $"Refresh {processed}/{total} ...";
            }
        });
    }

    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();
}
