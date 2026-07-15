using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osuTK;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Settings.Components;

internal sealed partial class DifficultyTableListContainer : FillFlowContainer
{
    private readonly DifficultyTableStore store;
    private readonly CollectionSyncManager? syncManager;
    private readonly Action<DT>? onDelete;
    private readonly Action<DT>? onUpdate;
    private readonly HashSet<string> expandedTables = [];

    public DifficultyTableListContainer(DifficultyTableStore store, CollectionSyncManager? syncManager,
                                        Action<DT>? onDelete = null,
                                        Action<DT>? onUpdate = null)
    {
        this.store = store;
        this.syncManager = syncManager;
        this.onDelete = onDelete;
        this.onUpdate = onUpdate;
        Direction = FillDirection.Vertical;
        AutoSizeAxes = Axes.Y;
        RelativeSizeAxes = Axes.X;
        Spacing = new Vector2(0, 6);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        store.TableListRebuildEvent -= onTableListRebuildEvent;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        store.TableListRebuildEvent += onTableListRebuildEvent;
        rebuild();
    }

    private void onTableListRebuildEvent(DT? _) => Schedule(rebuild);

    private void rebuild()
    {
        Clear();
        expandedTables.IntersectWith(store.Tables.Select(table => table.SourcePath ?? table.Name));

        foreach (var table in store.Tables)
        {
            var isSubdivided = syncManager?.IsSubdivided(table) ?? false;
            var tableKey = table.SourcePath ?? table.Name;
            Add(new DifficultyTableRowContainer(
                table,
                syncManager,
                isSubdivided,
                expandedTables.Contains(tableKey),
                expanded =>
                {
                    if (expanded)
                        expandedTables.Add(tableKey);
                    else
                        expandedTables.Remove(tableKey);
                },
                onDelete,
                onUpdate));
        }
    }
}
