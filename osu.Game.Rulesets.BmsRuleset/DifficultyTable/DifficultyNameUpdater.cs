using System.Linq;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Database;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Updates beatmap DifficultyName with difficulty table markers.
/// No caches, no events, no locking — operates inside an active realm write transaction.
/// Marker format: " [★1 sl3 st5]" — space + bracket-wrapped space-separated list.
/// </summary>
public partial class DifficultyNameUpdater(RealmAccess realm, DifficultyTableStore store)
{

    /// <summary>
    /// Full rebuild — processes every BMS beatmap against all current tables.
    /// Used by tests and after BMS file imports.
    /// </summary>
    public void RefreshAllMarkers()
    {
        realm.Write(r =>
        {
            var beatmaps = r.All<BeatmapInfo>()
                .Filter("Ruleset.ShortName == 'bms'")
                .ToList();

            foreach (var beatmap in beatmaps)
            {
                var clean = markerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);
                var markers = store.GetMarkers(beatmap.MD5Hash);

                if (markers.Count == 0)
                    beatmap.DifficultyName = clean;
                else
                {
                    var markerStr = string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
                    beatmap.DifficultyName = $"{clean} [{markerStr}]";
                }
            }
        });
    }

    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();
}
