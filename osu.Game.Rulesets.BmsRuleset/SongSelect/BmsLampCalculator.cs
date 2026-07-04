using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsLampCalculator
{
    private static readonly HitResult[] playable_results =
    [
        HitResult.Perfect,
        HitResult.Great,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Meh,
    ];

    public static BmsLamp Calculate(ScoreInfo? score)
    {
        if (score == null)
            return BmsLamp.NoPlay;

        if (score.Rank == ScoreRank.F)
            return BmsLamp.Failed;

        if (hasOnly(score, HitResult.Perfect))
            return BmsLamp.Max;

        if (hasOnly(score, HitResult.Perfect, HitResult.Great))
            return BmsLamp.Perfect;

        if (hasOnly(score, HitResult.Perfect, HitResult.Great, HitResult.Good))
            return BmsLamp.FullCombo;

        return score.Mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType switch
        {
            BmsGaugeType.AssistEasy => BmsLamp.AssistClear,
            BmsGaugeType.Easy => BmsLamp.EasyClear,
            BmsGaugeType.Hard => BmsLamp.HardClear,
            BmsGaugeType.ExHard => BmsLamp.ExHardClear,
            _ => BmsLamp.Clear,
        };
    }

    private static bool hasOnly(ScoreInfo score, params HitResult[] allowedResults)
    {
        var played = score.Statistics
            .Where(kvp => kvp.Value > 0 && playable_results.Contains(kvp.Key))
            .ToArray();

        return played.Length > 0 && played.All(kvp => allowedResults.Contains(kvp.Key));
    }
}
