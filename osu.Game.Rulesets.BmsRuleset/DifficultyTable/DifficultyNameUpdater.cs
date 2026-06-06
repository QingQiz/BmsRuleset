using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Database;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Updates beatmap DifficultyName with difficulty table markers.
/// Marker format: " [★1 sl3 st5]" — space + bracket-wrapped space-separated list.
/// </summary>
public partial class DifficultyNameUpdater(RealmAccess realm, DifficultyTableStore store)
{
    /// <summary>
    /// Full rebuild — applies markers from every loaded table to the matching
    /// beatmaps. Only processes entries that are actually in tables —
    /// O(total-table-entries), not O(all-beatmaps-in-database).
    /// Each chunk is a separate realm.Write so the write mutex is held only briefly.
    /// </summary>
    public void RefreshAllMarkers(Live<BeatmapSetInfo>? beatmapset = null)
    {
        realm.Run(r =>
        {
            var q = beatmapset == null
                ? r.All<BeatmapInfo>().Filter("Ruleset.ShortName == 'bms'")
                : beatmapset.Value.Beatmaps.Filter("Ruleset.ShortName == 'bms'");

            const int batch_size = 100;

            var batch = new List<(BeatmapInfo, string)>(batch_size);
            foreach (var beatmap in q)
            {
                if (batch.Count == batch_size)
                {
                    r.Write(() => { batch.ForEach(b => b.Item1.DifficultyName = b.Item2); });
                    batch.Clear();
                }

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
            }

            updateBatch(r, batch);
        });
        return;

        void updateBatch(Realm r, List<(BeatmapInfo, string)> batch)
        {
            r.Write(() => batch.ForEach(b => b.Item1.DifficultyName = b.Item2));
            batch.Clear();
        }
    }

    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();
}
