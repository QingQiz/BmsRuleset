using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal partial class BmsBeatmapCarousel : BeatmapCarousel
{
    internal Action<BeatmapInfo>? BeatmapMetadataUpdated { get; init; }

    internal Action<BeatmapInfo, GroupedBeatmap?>? BeatmapSelectionRemoved { get; init; }

    private readonly DrawablePool<BmsPanelBeatmap> beatmapPanelPool = new(100);
    private readonly DrawablePool<BmsPanelBeatmapStandalone> standalonePanelPool = new(100);
    private readonly DrawablePool<BmsUnavailableBeatmapPanel> unavailablePanelPool = new(100);
    private readonly List<BeatmapSetInfo> unavailableSets = [];
    private bool subscribed;
    private bool replacingUnavailableEntries;

    public BmsBeatmapCarousel()
    {
        Filters = Filters.Select(filter => filter is BeatmapCarouselFilterMatching
            ? new BmsBeatmapCarouselFilterMatching(() => Criteria!)
            : filter).ToArray();
    }

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        AddInternal(beatmapPanelPool);
        AddInternal(standalonePanelPool);
        AddInternal(unavailablePanelPool);
        Schedule(syncUnavailableEntries);
        BmsRulesetRuntime.DifficultyTablesChanged += difficultyTablesChanged;
        subscribed = true;
    }

    protected override Drawable GetDrawableForDisplay(CarouselItem item)
    {
        if (item.Model is GroupedBeatmap)
        {
            var beatmap = ((GroupedBeatmap)item.Model).Beatmap;

            if (UnavailableTableBeatmapFactory.IsUnavailable(beatmap))
                return unavailablePanelPool.Get();

            return item.DrawHeight == PanelBeatmapStandalone.HEIGHT ? standalonePanelPool.Get() : beatmapPanelPool.Get();
        }

        return base.GetDrawableForDisplay(item);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (subscribed)
            BmsRulesetRuntime.DifficultyTablesChanged -= difficultyTablesChanged;

        base.Dispose(isDisposing);
    }

    private void difficultyTablesChanged() => Schedule(syncUnavailableEntries);

    protected override bool HandleItemsChanged(NotifyCollectionChangedEventArgs args) =>
        replacingUnavailableEntries || base.HandleItemsChanged(args);

    internal BeatmapInfo[] IncludeUnavailableEntries(BeatmapInfo[] charts)
    {
        var previous = unavailableSets.SelectMany(set => set.Beatmaps).ToDictionary(chart => chart.MD5Hash, StringComparer.OrdinalIgnoreCase);
        unavailableSets.Clear();
        var store = BmsRulesetRuntime.DifficultyTableStore;
        if (store != null)
            unavailableSets.AddRange(UnavailableTableBeatmapFactory.Create(store, new BmsRuleset().RulesetInfo, charts));

        foreach (var chart in unavailableSets.SelectMany(set => set.Beatmaps))
        {
            if (!previous.TryGetValue(chart.MD5Hash, out var existing))
                continue;

            // Rebuilding the library must not turn an unchanged missing chart into a new carousel selection.
            chart.ID = existing.ID;
            chart.BeatmapSet!.ID = existing.BeatmapSet!.ID;
        }

        return [..charts, ..unavailableSets.SelectMany(set => set.Beatmaps)];
    }

    private void syncUnavailableEntries()
    {
        BmsRulesetRuntime.EnsureDifficultyTableStore(host, realm);

        var charts = Items.Where(chart => !UnavailableTableBeatmapFactory.IsUnavailable(chart)).ToArray();
        var updated = IncludeUnavailableEntries(charts);

        // A difficulty table refresh often follows a beatmap snapshot publication. In that case the
        // snapshot already contains the current placeholders; replacing the list again would make
        // realised panels briefly unbind their beatmap and hide their lamp while scores reload.
        if (Items.Count == updated.Length && Items.Zip(updated).All(pair => equivalent(pair.First, pair.Second)))
            return;

        replacingUnavailableEntries = true;
        try
        {
            // Missing entries can number in the thousands too; remove them as a range before re-filtering.
            Items.ReplaceRange(0, Items.Count, updated);
        }
        finally
        {
            replacingUnavailableEntries = false;
        }

        static bool equivalent(BeatmapInfo left, BeatmapInfo right) =>
            left.ID == right.ID
            && left.BeatmapSet?.ID == right.BeatmapSet?.ID
            && string.Equals(left.Hash, right.Hash, StringComparison.Ordinal)
            && string.Equals(left.MD5Hash, right.MD5Hash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.DifficultyName, right.DifficultyName, StringComparison.Ordinal)
            && left.Hidden == right.Hidden;
    }
}
