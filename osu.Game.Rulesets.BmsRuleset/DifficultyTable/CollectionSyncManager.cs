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

        // Find all existing collections that belong to this table
        var existing = r.All<BeatmapCollection>()
            .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || c.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var desired = new List<(string name, List<string> hashes)>();

        if (IsSubdivided(table))
        {
            var levelsWithEntries = table.LevelOrder
                .Where(l => table.Entries.Any(e => e.Level.Equals(l, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var indexWidth = levelsWithEntries.Count > 0
                ? (int)Math.Floor(Math.Log10(levelsWithEntries.Count)) + 1
                : 1;

            for (var index = 0; index < levelsWithEntries.Count; index++)
            {
                var level = levelsWithEntries[index];
                var indexStr = index.ToString($"D{indexWidth}");
                var entries = table.Entries
                    .Where(e => e.Level.Equals(level, StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Md5Hash)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                desired.Add(($"{prefix}[{indexStr}] {table.Symbol}{level}", entries));
            }
        }
        else
        {
            var allMd5 = table.Entries.Select(e => e.Md5Hash)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (allMd5.Count > 0)
                desired.Add((baseName, allMd5));
        }

        reconcileCollections(r, existing, desired);
    }

    private static void reconcileCollections(Realm r, List<BeatmapCollection> existing,
                                             List<(string name, List<string> hashes)> desired)
    {
        var unmatchedExisting = existing.ToList();
        var unmatchedDesired = new List<(string name, List<string> hashes)>();

        foreach (var desiredCollection in desired)
        {
            var exactMatch = unmatchedExisting.FirstOrDefault(c =>
                c.Name.Equals(desiredCollection.name, StringComparison.OrdinalIgnoreCase));

            if (exactMatch == null)
            {
                unmatchedDesired.Add(desiredCollection);
                continue;
            }

            unmatchedExisting.Remove(exactMatch);
            updateCollection(exactMatch, desiredCollection.name, desiredCollection.hashes);
        }

        var reusableCount = Math.Min(unmatchedExisting.Count, unmatchedDesired.Count);

        // Realm-backed drawables can outlive a change notification, so retaining identity prevents them from reading detached rows.
        for (var i = 0; i < reusableCount; i++)
            updateCollection(unmatchedExisting[i], unmatchedDesired[i].name, unmatchedDesired[i].hashes);

        for (var i = reusableCount; i < unmatchedExisting.Count; i++)
            r.Remove(unmatchedExisting[i]);

        for (var i = reusableCount; i < unmatchedDesired.Count; i++)
            r.Add(new BeatmapCollection(unmatchedDesired[i].name, unmatchedDesired[i].hashes));
    }

    private static void updateCollection(BeatmapCollection collection, string name, List<string> hashes)
    {
        if (!collection.Name.Equals(name, StringComparison.Ordinal))
            collection.Name = name;

        var currentSet = new HashSet<string>(collection.BeatmapMD5Hashes, StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(hashes, StringComparer.OrdinalIgnoreCase);

        foreach (var toRemove in currentSet.Except(newSet).ToList())
            collection.BeatmapMD5Hashes.Remove(toRemove);

        foreach (var toAdd in newSet.Except(currentSet))
            collection.BeatmapMD5Hashes.Add(toAdd);
    }
}
