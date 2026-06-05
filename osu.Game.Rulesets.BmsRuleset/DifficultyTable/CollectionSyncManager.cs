using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Collections;
using osu.Game.Database;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

/// <summary>
/// Syncs DifficultyTable entries to/from Realm BeatmapCollection objects.
/// Each table becomes a collection named "BMS: {table.Name}".
/// When subdivided, becomes "BMS: {table.Name} {level}".
/// Created and owned by BmsSettingsSubsection, which provides RealmAccess.
/// </summary>
public class CollectionSyncManager
{
    public const string COLLECTION_PREFIX = "BMS: ";

    private readonly RealmAccess realm;
    private readonly DifficultyTableStore store;

    /// <summary>
    /// Tracks which tables are currently subdivided.
    /// Key: table identifier (SourcePath).
    /// </summary>
    private readonly HashSet<string> subdividedTables = [];

    public CollectionSyncManager(RealmAccess realm, DifficultyTableStore store)
    {
        this.realm = realm;
        this.store = store;
        store.TableLoaded += onTableLoaded;
        store.TableRemoved += onTableRemoved;
    }

    /// <summary>
    /// Create or update collections for a newly loaded table.
    /// </summary>
    private void onTableLoaded(DifficultyTable table)
    {
        var key = table.SourcePath ?? table.Name;
        var isSubdivided = subdividedTables.Contains(key);
        syncCollections(table, isSubdivided);
    }

    /// <summary>
    /// Remove collections when a table is removed.
    /// </summary>
    private void onTableRemoved(DifficultyTable table)
    {
        var prefix = collectionPrefixFor(table);
        realm.Write(r =>
        {
            var toRemove = r.All<BeatmapCollection>()
                .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var c in toRemove)
                r.Remove(c);
        });
    }

    /// <summary>
    /// Toggle subdivide state for a table.
    /// </summary>
    public void ToggleSubdivide(DifficultyTable table)
    {
        var key = table.SourcePath ?? table.Name;
        var currentlySubdivided = subdividedTables.Contains(key);

        if (currentlySubdivided)
            subdividedTables.Remove(key);
        else
            subdividedTables.Add(key);

        syncCollections(table, !currentlySubdivided);
        store.NotifyTablesChanged();
    }

    /// <summary>
    /// Whether a table is currently subdivided.
    /// </summary>
    public bool IsSubdivided(DifficultyTable table)
    {
        var key = table.SourcePath ?? table.Name;
        return subdividedTables.Contains(key);
    }

    private void syncCollections(DifficultyTable table, bool subdivided)
    {
        var prefix = collectionPrefixFor(table);

        realm.Write(r =>
        {
            var existing = r.All<BeatmapCollection>()
                .Where(c => c.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var c in existing)
                r.Remove(c);

            if (subdivided)
            {
                foreach (var level in table.LevelOrder)
                {
                    var entries = table.Entries.Where(e => e.Level == level).ToList();
                    if (entries.Count == 0) continue;

                    var collection = new BeatmapCollection(
                        $"{prefix}{level}",
                        entries.Select(e => e.Md5Hash).ToList());
                    r.Add(collection);
                }
            }
            else
            {
                var allMd5 = table.Entries.Select(e => e.Md5Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (allMd5.Count > 0)
                {
                    var collection = new BeatmapCollection(prefix.TrimEnd(' '), allMd5);
                    r.Add(collection);
                }
            }
        });
    }

    private static string collectionPrefixFor(DifficultyTable table) => $"{COLLECTION_PREFIX}{table.Name} ";
}
