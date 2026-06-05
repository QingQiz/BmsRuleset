using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Database;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Appends/removes difficulty table markers to/from BeatmapInfo.DifficultyName in Realm.
/// Marker format: " [★1 sl3 st5]" — space + bracket-wrapped space-separated list.
/// Created and owned by BmsSettingsSubsection.
/// </summary>
public partial class DifficultyNameUpdater
{
    [GeneratedRegex(@"\s\[[^\]]*\]$", RegexOptions.Compiled)]
    private static partial Regex markerSuffixRegex();

    private readonly RealmAccess realm;
    private readonly DifficultyTableStore store;

    /// <summary>
    /// Cache of original DifficultyName per BeatmapInfo.GUID, so we can revert.
    /// </summary>
    private readonly Dictionary<Guid, string> originalNames = new();

    public DifficultyNameUpdater(RealmAccess realm, DifficultyTableStore store)
    {
        this.realm = realm;
        this.store = store;
        store.TableLoaded += _ => RefreshAllMarkers();
        store.TableRemoved += _ => RefreshAllMarkers();
    }

    public void RefreshAllMarkers()
    {
        realm.Write(r =>
        {
            var allBeatmaps = r.All<BeatmapInfo>().ToList();
            var bmsBeatmaps = allBeatmaps.Where(b => b.Ruleset.ShortName == "bms").ToList();

            foreach (var beatmap in bmsBeatmaps)
            {
                if (!originalNames.ContainsKey(beatmap.ID))
                    originalNames[beatmap.ID] = beatmap.DifficultyName;

                var original = originalNames[beatmap.ID];
                var markers = store.GetMarkers(beatmap.MD5Hash);

                if (markers.Count == 0)
                {
                    beatmap.DifficultyName = original;
                }
                else
                {
                    var clean = markerSuffixRegex().Replace(original, string.Empty);
                    var markerStr = string.Join(" ", markers.Select(m => $"{m.table.Symbol}{m.entry.Level}"));
                    beatmap.DifficultyName = $"{clean} [{markerStr}]";
                }
            }
        });
    }
}
