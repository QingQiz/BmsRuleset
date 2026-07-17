using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsLampScoreSelector
{
    public static ScoreInfo? SelectBest(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods) =>
        scores.Where(score => MatchesSelectedMods(score, selectedMods))
            .MaxBy(score => (lampPriority(BmsLampCalculator.Calculate(score)), score.TotalScore, -score.Date.UtcDateTime.Ticks));

    private static int lampPriority(BmsLamp lamp) => lamp switch
    {
        BmsLamp.NoPlay => 0,
        BmsLamp.Failed => 1,
        BmsLamp.AssistClear => 2,
        BmsLamp.EasyClear => 3,
        BmsLamp.Clear => 4,
        BmsLamp.HardClear => 5,
        BmsLamp.ExHardClear => 6,
        BmsLamp.FullCombo => 7,
        BmsLamp.Perfect => 8,
        BmsLamp.Max => 9,
        _ => throw new ArgumentOutOfRangeException(nameof(lamp), lamp, null),
    };

    internal static bool MatchesSelectedMods(ScoreInfo score, IReadOnlyList<Mod> selectedMods) =>
        reductionModMatches<BmsModHideScratch>(score.Mods, selectedMods) &&
        reductionModMatches<BmsModAutoScratch>(score.Mods, selectedMods) &&
        reductionModMatches<BmsModConstant>(score.Mods, selectedMods) &&
        rateModsMatch(score.Mods, selectedMods);

    private static bool reductionModMatches<TMod>(IEnumerable<Mod> scoreMods, IEnumerable<Mod> selectedMods)
        where TMod : Mod => !has<TMod>(scoreMods) || has<TMod>(selectedMods);

    private static bool has<TMod>(IEnumerable<Mod> mods)
        where TMod : Mod => mods.Any(mod => mod is TMod);

    private static bool rateModsMatch(IEnumerable<Mod> scoreMods, IEnumerable<Mod> selectedMods)
    {
        var scoreRateMod = rateMod(scoreMods);
        var selectedRateMod = rateMod(selectedMods);

        return scoreRateMod == selectedRateMod ||
               scoreRateMod == null && selectedRateMod?.type == typeof(BmsModHalfTime);
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
