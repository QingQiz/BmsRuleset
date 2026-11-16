using System;
using System.Linq;
using System.Collections.Generic;
using RhythmEngine.Profiles.Bms;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal sealed partial class BmsGameplayProfile
{
    internal override LocalisableString ScoreLabel => BmsStrings.ExScore;

    internal override double? StoredScore(ScoreInfo score, double? knownMaximum = null)
    {
        if (score.Statistics.Count > 0)
            return storedExScore(score.Statistics);

        // Older stored scores may have only accuracy; recover EX only when a maximum is known.
        var maximum = knownMaximum ?? StoredMaximumScore(score);
        return maximum.HasValue ? Math.Clamp(Math.Round(score.Accuracy * maximum.Value), 0, maximum.Value) : null;
    }

    private static double storedExScore(IReadOnlyDictionary<HitResult, int> statistics)
        => statistics.Sum(pair => pair.Key is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh
            ? BmsProfiles.GetResultValue(ResultIdFor(pair.Key)).Score * pair.Value
            : 0);

    internal override double? StoredMaximumScore(ScoreInfo score)
        => score.MaximumStatistics.Count > 0 ? storedExScore(score.MaximumStatistics) : null;

    internal override double? ScoreForRank(ScoreRank rank, double maximum) => Math.Ceiling(AccuracyCutoffFromRank(rank) * maximum);

    internal override LocalisableString RankLabel(ScoreRank rank) => BmsStrings.RankLetter(rank switch
    {
        ScoreRank.X or ScoreRank.XH => "s",
        ScoreRank.S or ScoreRank.SH => "aaa",
        ScoreRank.A => "aa",
        ScoreRank.B => "a",
        ScoreRank.C => "b",
        ScoreRank.D => "c",
        _ => "f",
    });

    internal override LocalisableString JudgementLabel(HitResult result) => BmsStrings.ResultJudgement(result);

    internal override BmsLamp LampFor(ScoreInfo score)
    {
        if (score.Rank == ScoreRank.F)
            return BmsLamp.Failed;

        if (hasOnly(HitResult.Perfect))
            return BmsLamp.Max;
        if (hasOnly(HitResult.Perfect, HitResult.Great))
            return BmsLamp.Perfect;
        if (hasOnly(HitResult.Perfect, HitResult.Great, HitResult.Good))
            return BmsLamp.FullCombo;

        return score.Mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType switch
        {
            BmsGaugeType.AssistEasy => BmsLamp.LightAssistClear,
            BmsGaugeType.Easy => BmsLamp.EasyClear,
            BmsGaugeType.Hard => BmsLamp.HardClear,
            BmsGaugeType.ExHard => BmsLamp.ExHardClear,
            _ => BmsLamp.Clear,
        };

        bool hasOnly(params HitResult[] allowed)
        {
            // BMS Miss stores EMPTY_POOR, which does not consume a scoring opportunity.
            var played = score.Statistics.Where(pair => pair.Value > 0 && pair.Key is
                HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh).ToArray();
            return played.Length > 0 && played.All(pair => allowed.Contains(pair.Key));
        }
    }
}
