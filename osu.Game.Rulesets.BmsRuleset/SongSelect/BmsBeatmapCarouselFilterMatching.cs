using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Screens.Select;
using osu.Game.Utils;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal sealed class BmsBeatmapCarouselFilterMatching(Func<FilterCriteria> getCriteria) : ICarouselFilter
{
    public int BeatmapItemsCount { get; private set; }

    public async Task<List<CarouselItem>> Run(IEnumerable<CarouselItem> items, CancellationToken cancellationToken) => await Task.Run(() =>
    {
        var criteria = getCriteria();
        var countMatching = 0;
        var result = new List<CarouselItem>();

        foreach (var item in items)
        {
            var beatmap = (BeatmapInfo)item.Model;

            if (beatmap.Hidden || !Matches(beatmap, criteria))
                continue;

            countMatching++;
            result.Add(item);
        }

        BeatmapItemsCount = countMatching;
        return result;
    }, cancellationToken).ConfigureAwait(false);

    internal static bool Matches(BeatmapInfo beatmap, FilterCriteria criteria)
    {
        var match = criteria.Ruleset == null
                    || BmsForeignBeatmapConverterRegistry.AllowsGameplay(beatmap, criteria.Ruleset, criteria.AllowConvertedBeatmaps);

        if (criteria.SelectedBeatmapSet != null)
            return beatmap.BeatmapSet?.Equals(criteria.SelectedBeatmapSet) == true && match;

        if (!match) return false;

        if (criteria.SearchTerms.Length > 0)
        {
            match = beatmap.Match(criteria.SearchTerms);

            if (!match && criteria.SearchNumber.HasValue)
                match = beatmap.OnlineID == criteria.SearchNumber.Value || beatmap.BeatmapSet?.OnlineID == criteria.SearchNumber.Value;
        }

        if (!match) return false;

        match &= !criteria.StarDifficulty.HasFilter || criteria.StarDifficulty.IsInRange(beatmap.StarRating.FloorToDecimalDigits(2));
        match &= !criteria.ApproachRate.HasFilter || criteria.ApproachRate.IsInRange(beatmap.Difficulty.ApproachRate);
        match &= !criteria.DrainRate.HasFilter || criteria.DrainRate.IsInRange(beatmap.Difficulty.DrainRate);
        match &= !criteria.CircleSize.HasFilter || criteria.CircleSize.IsInRange(beatmap.Difficulty.CircleSize);
        match &= !criteria.OverallDifficulty.HasFilter || criteria.OverallDifficulty.IsInRange(beatmap.Difficulty.OverallDifficulty);
        match &= !criteria.Length.HasFilter || criteria.Length.IsInRange(beatmap.Length);
        match &= !criteria.LastPlayed.HasFilter || criteria.LastPlayed.IsInRange(beatmap.LastPlayed ?? DateTimeOffset.MinValue);
        match &= !criteria.DateRanked.HasFilter || (beatmap.BeatmapSet?.DateRanked != null && criteria.DateRanked.IsInRange(beatmap.BeatmapSet.DateRanked.Value));
        match &= !criteria.DateSubmitted.HasFilter || (beatmap.BeatmapSet?.DateSubmitted != null && criteria.DateSubmitted.IsInRange(beatmap.BeatmapSet.DateSubmitted.Value));
        match &= !criteria.BPM.HasFilter || criteria.BPM.IsInRange(beatmap.BPM);
        match &= !criteria.BeatDivisor.HasFilter || criteria.BeatDivisor.IsInRange(beatmap.BeatDivisor);
        match &= !criteria.OnlineStatus.HasFilter || criteria.OnlineStatus.IsInRange(beatmap.Status);

        if (!match) return false;

        match &= !criteria.Creator.HasFilter || criteria.Creator.Matches(beatmap.Metadata.Author.Username);

        if (criteria.Artist.HasFilter)
            match &= criteria.Artist.ExcludeTerm
                ? criteria.Artist.Matches(beatmap.Metadata.Artist) && criteria.Artist.Matches(beatmap.Metadata.ArtistUnicode)
                : criteria.Artist.Matches(beatmap.Metadata.Artist) || criteria.Artist.Matches(beatmap.Metadata.ArtistUnicode);

        if (criteria.Title.HasFilter)
            match &= criteria.Title.ExcludeTerm
                ? criteria.Title.Matches(beatmap.Metadata.Title) && criteria.Title.Matches(beatmap.Metadata.TitleUnicode)
                : criteria.Title.Matches(beatmap.Metadata.Title) || criteria.Title.Matches(beatmap.Metadata.TitleUnicode);

        match &= !criteria.DifficultyName.HasFilter || criteria.DifficultyName.Matches(beatmap.DifficultyName);
        match &= !criteria.Source.HasFilter || criteria.Source.Matches(beatmap.Metadata.Source);

        foreach (var tagFilter in criteria.UserTags)
        {
            if (tagFilter.ExcludeTerm)
                match &= beatmap.Metadata.UserTags.All(tagFilter.Matches);
            else
                match &= beatmap.Metadata.UserTags.Any(tagFilter.Matches);
        }

        match &= !criteria.UserStarDifficulty.HasFilter || criteria.UserStarDifficulty.IsInRange(beatmap.StarRating);
        if (!match) return false;

        match &= criteria.CollectionBeatmapMD5Hashes?.Contains(beatmap.MD5Hash) ?? true;
        if (match && criteria.RulesetCriteria != null)
            match &= criteria.RulesetCriteria.Matches(beatmap, criteria);

        if (match && criteria.HasOnlineID == true)
            match &= beatmap.OnlineID >= 0;

        if (match && criteria.BeatmapSetId != null)
            match &= criteria.BeatmapSetId == beatmap.BeatmapSet?.OnlineID;

        return match;
    }
}
