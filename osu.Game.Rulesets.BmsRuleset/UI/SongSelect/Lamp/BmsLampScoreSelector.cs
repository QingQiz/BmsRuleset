using System.Collections.Generic;
using System.Linq;
using System;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;

/// <summary>
/// Central score selection for BMS song-select and course displays.
/// </summary>
public static class BmsLampScoreSelector
{
    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods) =>
        scores.Where(score => MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (lampPriority(BmsLampCalculator.Calculate(score)), score.TotalScore, -score.Date.UtcDateTime.Ticks));

    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods, double? maximumScore) =>
        scores.Where(score => MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (GameplayProfile.FromMods(score.Mods).StoredScore(score, maximumScore), -score.Date.UtcDateTime.Ticks));

    internal static (BmsLamp Lamp, ScoreRank? Rank) SelectBestCourse(IReadOnlyList<BmsCourseResult> results, IReadOnlyList<Mod> selectedMods)
    {
        var ruleset = new BmsRuleset();
        var matchingResults = results.Where(result =>
        {
            if (!hasHistoricalStage(result))
                return false;

            var mods = modsFromResult(result, ruleset);
            return mods.All(mod => mod.UserPlayable) && matchesSelectedMods(mods, selectedMods);
        }).ToArray();

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
        matchesSelectedMods(score.Mods, selectedMods);

    private static bool matchesSelectedMods(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods) =>
        scoreMods.Concat(selectedMods)
            .OfType<IApplicableToScoreSelection>()
            .Select(mod => mod.ScoreSelectionRule)
            .Distinct<IScoreSelectionRule>(ReferenceEqualityComparer.Instance)
            .All(rule => rule.IsScoreEligible(scoreMods, selectedMods));

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

    private static Mod[] modsFromResult(BmsCourseResult result, BmsRuleset ruleset)
    {
        // Class-tier gauge mods never appear in song select and are irrelevant to matching.
        return result.Attempt?.ModAcronyms
            .Select(resolveMod)
            .Where(mod => mod != null)
            .Select(mod => mod!)
            .ToArray() ?? [];

        Mod? resolveMod(string acronym) => acronym.ToUpperInvariant() is "C1" or "C2" or "C3"
            ? null
            : ruleset.CreateModFromAcronym(acronym);
    }

    private static bool hasHistoricalStage(BmsCourseResult result) =>
        result.Attempt?.Stages is { Length: > 0 } stages
        && stages.Any(stage => stage.ScoreId is { } scoreId && scoreId != Guid.Empty);
}
