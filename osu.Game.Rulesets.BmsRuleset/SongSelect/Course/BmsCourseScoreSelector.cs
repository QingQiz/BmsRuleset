using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal static class BmsCourseScoreSelector
{
    internal static (BmsLamp Lamp, ScoreRank? Rank) SelectBest(IReadOnlyList<BmsCourseResult> results, IReadOnlyList<Mod> selectedMods)
    {
        var scores = results.Select(scoreFromResult).ToArray();
        var matchingResults = results.Where((_, index) => BmsLampScoreSelector.MatchesSelectedMods(scores[index], selectedMods)).ToArray();

        var lamp = matchingResults.Length == 0 ? BmsLamp.NoPlay : matchingResults.Max(result => result.Lamp);

        ScoreRank? rank = null;

        foreach (var result in matchingResults)
        {
            if (result.Rank is not { } candidate)
                continue;

            if (rank == null || candidate > rank)
                rank = candidate;
        }

        return (lamp, rank);
    }

    private static ScoreInfo scoreFromResult(BmsCourseResult result)
    {
        var ruleset = new BmsRuleset();

        // Only the mod set matters here: it drives the rank filter, not the lamp.
        var mods = result.Attempt?.ModAcronyms
            .Select(resolveMod)
            .Where(mod => mod != null)
            .Select(mod => mod!)
            .ToArray() ?? [];

        return new ScoreInfo
        {
            Mods = mods,
        };

        // Class-tier gauge mods never appear in song select and are irrelevant to matching.
        Mod? resolveMod(string acronym) => acronym.ToUpperInvariant() is "C1" or "C2" or "C3"
            ? null
            : ruleset.CreateModFromAcronym(acronym);
    }
}
