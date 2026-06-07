using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Collections;
using osu.Game.Database;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Syncs DifficultyTable entries to/from Realm BeatmapCollection objects.
/// Naming conventions:
///   Non-subdivided: [BMS] {TableName}
///   Subdivided:     [BMS] {TableName} [{index}] {Symbol}{Level}
///   Index width is dynamic: 1 digit for &lt;10 levels, 2 for &lt;100, etc.
/// </summary>
public class CollectionSyncManager
{
    public const string COLLECTION_PREFIX = "[​B​M​S​] ";

    /// <summary>
    /// Tracks which tables are currently subdivided.
    /// Key: table identifier (SourcePath ?? Name).
    /// </summary>
    private readonly HashSet<string> subdividedTables = [];

    /// <summary>
    /// Toggle subdivide state for a table.
    /// </summary>
    public void ToggleSubdivide(RealmAccess? realm, DifficultyTable table)
    {
        if (realm == null) return;

        var key = table.SourcePath ?? table.Name;
        if (!subdividedTables.Remove(key))
            subdividedTables.Add(key);

        // rebuild first. so we can update the divided status
        BmsRuleset.DifficultyTableStore?.NotifyToRebuildTableList(null);

        SyncInTransaction(realm, null);
    }

    public bool IsSubdivided(DifficultyTable table)
    {
        var key = table.SourcePath ?? table.Name;
        return subdividedTables.Contains(key);
    }


    /// <summary>
    /// Sync collections inside an active realm write transaction.
    /// Uses diff-based updates (not delete + recreate).
    /// </summary>
    public void SyncInTransaction(RealmAccess? realm, DifficultyTable? tableRemoved)
    {
        if (realm == null) return;

        // remove collections for removed table first
        if (tableRemoved != null)
        {
            var prefix = $"{COLLECTION_PREFIX}{tableRemoved.Name} ";
            var baseName = prefix.TrimEnd(' ');

            realm.Write(r =>
            {
                var existing = r.All<BeatmapCollection>()
                    .Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal)
                                || c.Name.Equals(baseName, StringComparison.Ordinal))
                    .ToList();

                foreach (var c in existing)
                {
                    r.Remove(c);
                }
            });
        }

        foreach (var table in BmsRuleset.DifficultyTableStore?.Tables ?? [])
        {
            realm.Write(r => syncDivideStatus(r, table));
        }
    }

    private void syncDivideStatus(Realm r, DifficultyTable table)
    {
        var prefix = $"{COLLECTION_PREFIX}{table.Name} ";
        var baseName = prefix.TrimEnd(' ');

        var subdivided = IsSubdivided(table);

        // Find all existing collections that belong to this table
        var existing = r.All<BeatmapCollection>()
            .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || c.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (subdivided)
        {
            // Determine which levels in LevelOrder actually have entries
            var levelsWithEntries = table.LevelOrder
                .Where(l => table.Entries.Any(e => e.Level == l))
                .ToList();

            // Dynamic index width based on number of levels with entries
            var indexWidth = levelsWithEntries.Count > 0
                ? (int)Math.Floor(Math.Log10(levelsWithEntries.Count)) + 1
                : 1;

            var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;

            foreach (var level in levelsWithEntries)
            {
                var indexStr = index.ToString($"D{indexWidth}");

                var entries = table.Entries
                    .Where(e => e.Level == level)
                    .Select(e => e.Md5Hash)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (entries.Count == 0)
                {
                    index++;
                    continue;
                }

                var collectionName = $"{prefix}[{indexStr}] {table.Symbol}{level}";
                expectedNames.Add(collectionName);

                var existingCol = r.All<BeatmapCollection>()
                    .FirstOrDefault(c => c.Name == collectionName);

                if (existingCol != null)
                {
                    // Diff-based update
                    var currentSet = new HashSet<string>(existingCol.BeatmapMD5Hashes, StringComparer.OrdinalIgnoreCase);
                    var newSet = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);

                    foreach (var toRemove in currentSet.Except(newSet).ToList())
                        existingCol.BeatmapMD5Hashes.Remove(toRemove);
                    foreach (var toAdd in newSet.Except(currentSet))
                        existingCol.BeatmapMD5Hashes.Add(toAdd);
                }
                else
                {
                    r.Add(new BeatmapCollection(collectionName, entries));
                }

                index++;
            }

            // Remove any existing collections that aren't in the expected set
            // (handles renamed collections from index-width changes or removed levels)
            foreach (var col in existing)
            {
                if (!expectedNames.Contains(col.Name))
                    r.Remove(col);
            }
        }
        else
        {
            // Not subdivided: merge into a single collection
            var allMd5 = table.Entries.Select(e => e.Md5Hash)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Remove per-level collections
            foreach (var col in existing)
            {
                if (col.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    r.Remove(col);
            }

            var baseCol = r.All<BeatmapCollection>()
                .FirstOrDefault(c => c.Name == baseName);

            if (baseCol != null)
            {
                var currentSet = new HashSet<string>(baseCol.BeatmapMD5Hashes, StringComparer.OrdinalIgnoreCase);
                var newSet = new HashSet<string>(allMd5, StringComparer.OrdinalIgnoreCase);

                foreach (var toRemove in currentSet.Except(newSet).ToList())
                    baseCol.BeatmapMD5Hashes.Remove(toRemove);
                foreach (var toAdd in newSet.Except(currentSet))
                    baseCol.BeatmapMD5Hashes.Add(toAdd);
            }
            else if (allMd5.Count > 0)
            {
                r.Add(new BeatmapCollection(baseName, allMd5));
            }
        }
    }
}
