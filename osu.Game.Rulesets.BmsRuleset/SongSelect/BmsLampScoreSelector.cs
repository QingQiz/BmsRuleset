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
        scores.Where(score => matchesSelectedMods(score, selectedMods))
            .MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));

    private static bool matchesSelectedMods(ScoreInfo score, IReadOnlyList<Mod> selectedMods) =>
        has<BmsModHideScratch>(score.Mods) == has<BmsModHideScratch>(selectedMods)
        && has<BmsModAutoScratch>(score.Mods) == has<BmsModAutoScratch>(selectedMods)
        && has<BmsModConstant>(score.Mods) == has<BmsModConstant>(selectedMods)
        && rateMod(score.Mods) == rateMod(selectedMods);

    private static bool has<TMod>(IEnumerable<Mod> mods)
        where TMod : Mod => mods.Any(mod => mod is TMod);

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
