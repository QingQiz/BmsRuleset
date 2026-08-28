using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsBeatmapCarousel : BeatmapCarousel
{
    private readonly DrawablePool<BmsPanelBeatmap> beatmapPanelPool = new(100);
    private readonly DrawablePool<BmsPanelBeatmapStandalone> standalonePanelPool = new(100);
    private readonly DrawablePool<BmsUnavailableBeatmapPanel> unavailablePanelPool = new(100);
    private readonly List<BeatmapSetInfo> unavailableSets = [];
    private bool subscribed;

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

    private void syncUnavailableEntries()
    {
        BmsRulesetRuntime.EnsureDifficultyTableStore(host, realm);

        foreach (var set in unavailableSets)
        {
            foreach (var beatmap in set.Beatmaps)
                Items.Remove(beatmap);
        }

        unavailableSets.Clear();

        var store = BmsRulesetRuntime.DifficultyTableStore;
        if (store == null)
            return;

        var additions = UnavailableTableBeatmapFactory.Create(store, new BmsRuleset().RulesetInfo, Items);
        Items.AddRange(additions.SelectMany(set => set.Beatmaps));
        unavailableSets.AddRange(additions);
    }
}
