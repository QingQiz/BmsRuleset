using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Collections;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Syncs DifficultyTable entries to/from Realm BeatmapCollection objects.
/// Each table becomes a collection named "BMS: {table.Name}".
/// When subdivided, becomes "BMS: {table.Name} {level}".
/// Stateless helper — called by DifficultyTableStore.ImportAsync within a realm.Write.
/// </summary>
public class CollectionSyncManager
{
    public const string COLLECTION_PREFIX = "BMS: ";

    /// <summary>
    /// Tracks which tables are currently subdivided.
    /// Key: table identifier (SourcePath).
    /// </summary>
    private readonly HashSet<string> subdividedTables = [];

    /// <summary>
    /// Toggle subdivide state for a table.
    /// </summary>
    public void ToggleSubdivide(DifficultyTable table)
    {
        var key = table.SourcePath ?? table.Name;
        if (!subdividedTables.Remove(key))
            subdividedTables.Add(key);
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
    public void SyncInTransaction(Realm r, DifficultyTable table)
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
            var desiredLevels = new HashSet<string>(table.LevelOrder);

            // Remove levels that no longer exist in the table
            foreach (var col in existing)
            {
                var colName = col.Name;
                if (colName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                    r.Remove(col); // base collection should not exist when subdivided
                else if (colName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var level = colName[prefix.Length..];
                    if (!desiredLevels.Contains(level))
                        r.Remove(col);
                }
            }

            // Create or update per-level collections
            foreach (var level in table.LevelOrder)
            {
                var entries = table.Entries
                    .Where(e => e.Level == level)
                    .Select(e => e.Md5Hash)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (entries.Count == 0) continue;

                var collectionName = $"{prefix}{level}";
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
                if (col.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !col.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
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
