using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;

internal sealed class BmsBeatmapCarouselDifficultyFilter(
    Func<FilterCriteria> getCriteria,
    Func<BmsSongSelectDifficultyCache> getCache,
    ICarouselFilter[] filters) : ICarouselFilter
{
    public int BeatmapItemsCount => filters.Last().BeatmapItemsCount;

    private readonly SemaphoreSlim runLock = new(1);

    internal static bool UsesDifficulty(FilterCriteria criteria) =>
        criteria.Sort == SortMode.Difficulty || criteria.Group == GroupMode.Difficulty
                                             || criteria.StarDifficulty.HasFilter || criteria.UserStarDifficulty.HasFilter;

    public async Task<List<CarouselItem>> Run(IEnumerable<CarouselItem> items, CancellationToken cancellationToken)
    {
        await runLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var projections = new List<(BeatmapInfo Beatmap, double OriginalStars)>();
        try
        {
            var criteria = getCriteria();
            var source = items.ToList();
            if (criteria.Ruleset!.ShortName == Constant.SHORT_NAME && UsesDifficulty(criteria))
            {
                var cache = getCache();
                var hideScratch = criteria.Mods!.OfType<BmsModHideScratch>().Any();
                source = source.Select(item =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var beatmap = (BeatmapInfo)item.Model;
                    if (UnavailableTableBeatmapFactory.IsUnavailable(beatmap))
                        return item;

                    var copy = beatmap.Clone();
                    projections.Add((copy, beatmap.StarRating));
                    copy.StarRating = cache.GetStars(beatmap, hideScratch);
                    return new CarouselItem(copy);
                }).ToList();
            }

            foreach (var filter in filters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                source = await filter.Run(source, cancellationToken).ConfigureAwait(false);
            }

            return source;
        }
        finally
        {
            // Only filtering consumes the projected stars. Selection and database operations
            // must retain the source ruleset's metadata.
            foreach (var (beatmap, originalStars) in projections)
                beatmap.StarRating = originalStars;
            runLock.Release();
        }
    }
}
