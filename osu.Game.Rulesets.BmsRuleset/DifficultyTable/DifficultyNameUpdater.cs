using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Database;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Updates beatmap DifficultyName with difficulty table markers.
/// Marker format: " [★1 sl3 st5]" — space + bracket-wrapped space-separated list.
/// </summary>
public partial class DifficultyNameUpdater(RealmAccess realm, DifficultyTableStore store)
{
    private const char marker_ownership_sentinel = '\u200B';
    private readonly object refreshLock = new();

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
    /// Reconciles markers on BMS beatmaps, including suffixes belonging to removed tables.
    /// Only changed names are written, and the detached library receives the final bulk state.
    /// </summary>
    public void RefreshAllMarkers(ProgressNotification? notification = null)
    {
        // An older refresh must not commit after a newer one when table operations overlap.
        lock (refreshLock)
            refreshAllMarkers(notification);
    }

    private void refreshAllMarkers(ProgressNotification? notification)
    {
        notification?.Text = BmsStrings.Collecting;
        var markerNames = store.GetMarkerNames();

        List<(Guid, string)> collect = [];

        realm.Run(r =>
        {
            var allBmsBeatmaps = r.All<BeatmapInfo>().Filter("Ruleset.ShortName == 'bms'");
            foreach (var beatmap in allBmsBeatmaps)
            {
                var markerStr = markerNames.GetValueOrDefault(beatmap.MD5Hash, string.Empty);
                var clean = ownedMarkerSuffixRegex().Replace(beatmap.DifficultyName, string.Empty);

                var res = markerStr.Length == 0
                    ? clean
                    : AddMarkerSuffix(clean, markerStr);

                if (res != beatmap.DifficultyName) collect.Add((beatmap.ID, res));
            }
        });

        var total = collect.Count;
        if (total == 0)
            return;

        using var bulkUpdate = BmsBulkBeatmapUpdate.Begin(realm);
        var processed = 0;
        notification?.Text = BmsStrings.Refreshing;
        notification?.Progress = 0;

        realm.Write(r =>
        {
            foreach (var collectItem in collect)
            {
                r.Find<BeatmapInfo>(collectItem.Item1)?.DifficultyName = collectItem.Item2;
                processed++;
                if (processed % 128 == 0 || processed == total)
                {
                    notification?.Text = BmsStrings.RefreshingProgress(processed, total);
                    notification?.Progress = (float)processed / total;
                }
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
