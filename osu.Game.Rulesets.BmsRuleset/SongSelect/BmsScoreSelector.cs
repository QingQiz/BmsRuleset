using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

/// <summary>
/// Central score selection for BMS song-select and course displays.
/// </summary>
public static class BmsScoreSelector
{
    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods) =>
        scores.Where(score => MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (lampPriority(BmsLampCalculator.Calculate(score)), score.TotalScore, -score.Date.UtcDateTime.Ticks));

    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods, int maximumExScore) =>
        scores.Where(score => MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (BmsExScore.Calculate(score, maximumExScore), -score.Date.UtcDateTime.Ticks));

    internal static (BmsLamp Lamp, ScoreRank? Rank) SelectBestCourse(IReadOnlyList<BmsCourseResult> results, IReadOnlyList<Mod> selectedMods)
    {
        var scores = results.Select(scoreFromResult).ToArray();
        var matchingResults = results.Where((result, index) =>
            hasHistoricalStage(result)
            && scores[index].Mods.All(mod => mod.UserPlayable)
            && MatchesSelectedMods(scores[index], selectedMods)).ToArray();

        if (matchingResults.Length == 0)
            return (BmsLamp.NoPlay, null);

        var lamp = matchingResults.MaxBy(result => lampPriority(result.Lamp)).Lamp;
        var ranks = matchingResults
            .Select(result => result.Rank)
            .Where(rank => rank != null)
            .Cast<ScoreRank>()
            .ToArray();
        ScoreRank? rank = ranks.Length == 0 ? null : ranks.Max();

        return (lamp, rank);
    }

    internal static bool MatchesSelectedMods(ScoreInfo score, IReadOnlyList<Mod> selectedMods) =>
        reductionModMatches<BmsModHideScratch>(score.Mods, selectedMods) &&
        reductionModMatches<BmsModAutoScratch>(score.Mods, selectedMods) &&
        reductionModMatches<BmsModConstant>(score.Mods, selectedMods) &&
        rateModsMatch(score.Mods, selectedMods);

    internal static bool MatchesExactMods(
        IEnumerable<Mod> scoreMods,
        IEnumerable<Mod> selectedMods,
        Func<Mod, bool> isFilterableMod)
        => MatchesExactAcronyms(scoreMods, selectedMods.Where(isFilterableMod).Select(mod => mod.Acronym), isFilterableMod);

    internal static bool MatchesExactAcronyms(
        IEnumerable<Mod> scoreMods,
        IEnumerable<string> selectedAcronyms,
        Func<Mod, bool> isFilterableMod)
        => selectedAcronyms.ToHashSet().SetEquals(scoreMods.Where(isFilterableMod).Select(mod => mod.Acronym));

    private static int lampPriority(BmsLamp lamp) => lamp switch
    {
        BmsLamp.NoPlay => 0,
        BmsLamp.Failed => 1,
        BmsLamp.AssistClear => 2,
        BmsLamp.LightAssistClear => 3,
        BmsLamp.EasyClear => 4,
        BmsLamp.Clear => 5,
        BmsLamp.HardClear => 6,
        BmsLamp.ExHardClear => 7,
        BmsLamp.FullCombo => 8,
        BmsLamp.Perfect => 9,
        BmsLamp.Max => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(lamp), lamp, null),
    };

    private static ScoreInfo scoreFromResult(BmsCourseResult result)
    {
        var ruleset = new BmsRuleset();

        // Class-tier gauge mods never appear in song select and are irrelevant to matching.
        var mods = result.Attempt?.ModAcronyms
            .Select(resolveMod)
            .Where(mod => mod != null)
            .Select(mod => mod!)
            .ToArray() ?? [];

        return new ScoreInfo
        {
            Mods = mods,
        };

        Mod? resolveMod(string acronym) => acronym.ToUpperInvariant() is "C1" or "C2" or "C3"
            ? null
            : ruleset.CreateModFromAcronym(acronym);
    }

    private static bool hasHistoricalStage(BmsCourseResult result) =>
        result.Attempt?.Stages is { Length: > 0 } stages
        && stages.Any(stage => stage.ScoreId is { } scoreId && scoreId != Guid.Empty);

    private static bool reductionModMatches<TMod>(IEnumerable<Mod> scoreMods, IEnumerable<Mod> selectedMods)
        where TMod : Mod => !has<TMod>(scoreMods) || has<TMod>(selectedMods);

    private static bool has<TMod>(IEnumerable<Mod> mods)
        where TMod : Mod => mods.Any(mod => mod is TMod);

    private static bool rateModsMatch(IEnumerable<Mod> scoreMods, IEnumerable<Mod> selectedMods)
    {
        var scoreRateMod = rateMod(scoreMods);
        var selectedRateMod = rateMod(selectedMods);

        return scoreRateMod == selectedRateMod ||
               (scoreRateMod == null && selectedRateMod?.type == typeof(BmsModHalfTime));
    }

    private static (Type type, double speedChange)? rateMod(IEnumerable<Mod> mods)
    {
        foreach (var mod in mods)
        {
            if (mod is BmsModHalfTime halfTime)
                return (typeof(BmsModHalfTime), halfTime.SpeedChange.Value);

            if (mod is BmsModDoubleTime doubleTime)
                return (typeof(BmsModDoubleTime), doubleTime.SpeedChange.Value);
        }

        return null;
    }
}
