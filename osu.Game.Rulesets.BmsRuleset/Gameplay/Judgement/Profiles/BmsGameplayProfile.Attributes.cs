using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Profiles.Bms;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal sealed partial class BmsGameplayProfile
{
    private const float exrank_display_max = 200;

    internal override IEnumerable<RulesetBeatmapAttribute> BeatmapAttributes(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods, BeatmapDifficulty adjustedDifficulty)
    {
        var original = Mania7KBeatmapConverter.CanConvert(beatmapInfo)
            ? Mania7KBeatmapConverter.GetConvertedDifficultyInfo(beatmapInfo)
            : BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);
        var adjusted = BmsDifficultyInfo.FromOsuDifficulty(adjustedDifficulty);
        var colours = new OsuColour();
        var layout = adjusted.LayoutVariant;
        var rules = BmsEngineRules.ForLayout(layout, modifiers);

        if (adjusted.ExRank is { } exRank)
        {
            var rate = rules.ResolveRate(adjusted.Rank, exRank);
            yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("exrank"), BmsStrings.DifficultyAttributeAcronym("exrank").ToString(), (float)original.ExRank.GetValueOrDefault(exRank), (float)exRank, exrank_display_max)
            {
                Description = BmsStrings.ExRankDescription(exRank),
                AdditionalMetrics = createRankMetrics(rate, layout, modifiers),
            };
        }
        else
        {
            var rate = rules.ResolveRate(adjusted.Rank, null);
            yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("rank"), BmsStrings.DifficultyAttributeAcronym("rank").ToString(), original.Rank, adjusted.Rank, 4)
            {
                Description = BmsStrings.RankDescription(adjusted.Rank),
                AdditionalMetrics = createRankMetrics(rate, layout, modifiers),
            };
        }

        if (original.LockedLongNoteMode != BmsLongNoteMode.Undefined)
        {
            yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("lnmode"), BmsStrings.DifficultyAttributeAcronym("lnmode").ToString(), (float)original.LockedLongNoteMode, (float)adjusted.LockedLongNoteMode, 3)
            {
                Description = BmsStrings.LockedLongNoteMode(formatLongNoteMode(adjusted.LockedLongNoteMode)),
                AdditionalMetrics =
                [
                    new(BmsStrings.LockedMode, formatLongNoteMode(adjusted.LockedLongNoteMode), colours.Gray4),
                ],
            };
        }

        var (gaugeType, calculator) = createDisplayGaugeCalculator(beatmapInfo, adjusted, mods, layout);
        yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("total"), BmsStrings.DifficultyAttributeAcronym("total").ToString(), (float)original.Total.GetValueOrDefault(), (float)adjusted.Total.GetValueOrDefault(), 300)
        {
            Description = createTotalDescription(adjusted, gaugeType, calculator),
            AdditionalMetrics = createTotalMetrics(calculator),
        };
    }

    private (BmsGaugeType Type, BmsGaugeCalculator Calculator) createDisplayGaugeCalculator(
        IBeatmapInfo beatmapInfo, BmsDifficultyInfo difficulty, IReadOnlyCollection<Mod> mods, BmsLayoutVariant layout)
    {
        var gaugeType = mods.OfType<BmsModGauge>().FirstOrDefault()?.GaugeType ?? BmsGaugeType.Normal;
        var family = BmsGaugeProfileFamilyProvider.FromLayout(layout);
        var profile = CreateGaugeProfile(gaugeType, family);
        return (gaugeType, new BmsGaugeCalculator(profile, difficulty.Total, Math.Max(1, beatmapInfo.TotalObjectCount), family));
    }

    private static LocalisableString createTotalDescription(BmsDifficultyInfo difficulty, BmsGaugeType gaugeType, BmsGaugeCalculator calculator)
    {
        var gaugeName = BmsGaugeProfileFactory.NameFor(gaugeType);
        var total = BmsStrings.TotalValue(calculator.Total, !difficulty.Total.HasValue);
        return calculator.Profile.GutsRules.Length > 0
            ? BmsStrings.GaugeSummaryWithGuts(gaugeName, calculator.NoteCount, total)
            : BmsStrings.GaugeSummary(gaugeName, calculator.NoteCount, total);
    }

    private static RulesetBeatmapAttribute.AdditionalMetric[] createRankMetrics(double rate, BmsLayoutVariant layout, BmsJudgementModifiers modifiers)
    {
        var metrics = new List<RulesetBeatmapAttribute.AdditionalMetric>();

        addHeadMetrics(BmsStrings.TimingMetricGroup("note"), BmsEngineRules.GetTiming(layout, column: 1, judgementRate: rate, tail: false, modifiers: modifiers));

        if (tryGetScratchColumn(layout, out var scratchColumn))
            addHeadMetrics(BmsStrings.Scratch, BmsEngineRules.GetTiming(layout, scratchColumn, rate, tail: false, modifiers: modifiers));

        addJudgementMetrics(BmsStrings.TimingMetricGroup("tail"), BmsEngineRules.GetTiming(layout, column: 1, judgementRate: rate, tail: true, modifiers: modifiers));

        if (tryGetScratchColumn(layout, out scratchColumn))
            addJudgementMetrics(BmsStrings.TimingMetricGroup("scratch_tail"), BmsEngineRules.GetTiming(layout, scratchColumn, rate, tail: true, modifiers: modifiers));

        return metrics.ToArray();

        void addHeadMetrics(LocalisableString prefix, BmsTimingDefinition table)
        {
            addJudgementMetrics(prefix, table);
            metrics.Add(new RulesetBeatmapAttribute.AdditionalMetric(BmsStrings.TimingMetricLabel(prefix, BmsStrings.EmptyInputFastZone),
                BmsStrings.TimingWindow(-table.FastWindowFor(HitResult.Miss), -table.FastWindowFor(HitResult.Ok)), BmsHitResultColours.ForHitResult(HitResult.Miss)));
        }

        void addJudgementMetrics(LocalisableString prefix, BmsTimingDefinition table)
        {
            foreach (var result in new[] { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok })
            {
                metrics.Add(new RulesetBeatmapAttribute.AdditionalMetric(BmsStrings.TimingMetricLabel(prefix, BmsStrings.ResultJudgement(result)),
                    BmsStrings.TimingWindow(-table.FastWindowFor(result), table.SlowWindowFor(result)), BmsHitResultColours.ForHitResult(result)));
            }
        }

        static bool tryGetScratchColumn(BmsLayoutVariant layout, out int scratchColumn)
        {
            for (var column = 0; column < BmsLayout.GetTotalColumns(layout); column++)
            {
                if (!BmsLayout.IsScratchColumn(column, layout))
                    continue;

                scratchColumn = column;
                return true;
            }

            scratchColumn = -1;
            return false;
        }
    }

    private static RulesetBeatmapAttribute.AdditionalMetric[] createTotalMetrics(BmsGaugeCalculator calculator)
    {
        var referenceHealth = calculator.Profile.InitialHealth;

        return
        [
            new(BmsStrings.GaugeBestJudgements, BmsStrings.GaugeDelta(calculator.GetDeltaFor(BmsResults.PGREAT, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Perfect)),
            new(BmsStrings.ResultJudgement(HitResult.Good), BmsStrings.GaugeDelta(calculator.GetDeltaFor(BmsResults.GOOD, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Good)),
            new(BmsStrings.ResultJudgement(HitResult.Ok), BmsStrings.GaugeDelta(calculator.GetDeltaFor(BmsResults.BAD, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Ok)),
            new(BmsStrings.ResultJudgement(HitResult.Meh), BmsStrings.GaugeDelta(calculator.GetDeltaFor(BmsResults.POOR, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Meh)),
            new(BmsStrings.ResultJudgement(HitResult.Miss), BmsStrings.GaugeDelta(calculator.GetDeltaFor(BmsResults.EMPTY_POOR, referenceHealth)), BmsHitResultColours.ForHitResult(HitResult.Miss)),
        ];
    }

    private static LocalisableString formatLongNoteMode(BmsLongNoteMode mode)
        => BmsStrings.LongNoteModeLabel(mode.ToString().ToLowerInvariant());
}
