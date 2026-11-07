using System;
using System.Collections.Concurrent;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public class BmsModLr2 : Mod, IApplicableToJudgement,
                         IApplicableToGaugeProfile, IApplicableAfterBeatmapConversion, IApplicableToScoreSelection, IApplicableToScoreProcessor,
                         IApplicableToDifficulty, IApplicableToBeatmapConverter
{
    private readonly ConcurrentDictionary<(double Rate, bool Tail), BmsJudgementWindowTable> windows = new();

    public override string Name => BmsStrings.ModLr2Name.ToString();

    public override string Acronym => "R2";

    public override LocalisableString Description => BmsStrings.ModLr2;

    public override ModType Type => ModType.Fun;

    public override IconUsage? Icon => BmsIcons.Lr2;

    public override Type[] IncompatibleMods => [typeof(BmsModChargeNote), typeof(BmsModHellChargeNote)];

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModLr2>.EXACT;

    public bool ReplacesBaseWindows => true;

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table, in BmsJudgementWindowContext context)
        => windows.GetOrAdd((context.Rate, context.Tail), createWindows);

    public Func<BmsLayoutVariant, int, double?, double> JudgementRateCalculator => calculateJudgementRate;

    private static double calculateJudgementRate(BmsLayoutVariant layout, int rank, double? exRank)
        => exRank is { } value
            ? value > 0 ? Math.Truncate(Math.Truncate(value) * 0.75) / 100 : 0.75
            : rank switch { 0 => 0.25, 1 => 0.5, 3 => 1, _ => 0.75 };

    public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
    {
        if (beatmapConverter is BmsBeatmapConverter bmsConverter)
            bmsConverter.JudgementRateCalculator = JudgementRateCalculator;
    }

    public BmsJudgementBehaviour ApplyToJudgement(BmsJudgementBehaviour behaviour) => new()
    {
        SearchWindow = 1000,
        Algorithm = BmsJudgementAlgorithm.Combo,
        LongNoteBadIsImmediate = true,
    };

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bmsBeatmap)
            bmsBeatmap.LockedLongNoteMode = BmsLongNoteMode.LongNote;
    }

    public void ApplyToDifficulty(BeatmapDifficulty difficulty) => difficulty.DrainRate = (float)BmsLongNoteMode.LongNote;

    public void ApplyToScoreProcessor(ScoreProcessor scoreProcessor)
    {
        if (scoreProcessor is BmsScoreProcessor score)
            score.EmptyPoorBreaksCombo = false;
    }

    public ScoreRank AdjustRank(ScoreRank rank, double accuracy) => rank;

    public BmsGaugeProfile ApplyToGaugeProfile(BmsGaugeType type, BmsGaugeProfile profile)
    {
        var lr2 = BmsGaugeProfileFactory.Create(type, BmsGaugeProfileFamily.Lr2);
        var survival = lr2.Algorithm != BmsGaugeAlgorithm.Total;
        lr2 = lr2 with
        {
            DefaultTotal = CalculateDefaultTotal,
            DamageScale = CalculateDamageScale,
            FailureThreshold = survival ? 0.02 : 0,
            GutsRules = lr2.GutsRules.Count > 0 ? [new BmsGaugeGutsRule(0.32, 0.6)] : [],
        };

        // LR2's DEATH gauge counts BAD/missed notes, while excess presses preserve it.
        return type == BmsGaugeType.Hazard
            ? lr2 with { PerfectGain = 0, GreatGain = 0, GoodGain = 0, EmptyPoorDelta = 0 }
            : lr2;
    }

    internal static double CalculateDefaultTotal(int notes)
        => 160 + (Math.Max(1, notes) + Math.Clamp(notes - 400, 0, 200)) * 0.16;

    internal static double CalculateDamageScale(double total, int notes)
    {
        // Match LR2oraja's LR2 correction rather than beatoraja's approximate TOTAL bands.
        var totalScale = 10 / Math.Clamp(Math.Floor(total / 16) - 5, 1, 10);
        var noteScale = notes switch
        {
            <= 20 => 10,
            < 30 => 8 + 0.2 * (30 - notes),
            < 60 => 5 + (60 - notes) / 15.0,
            < 125 => 4 + (125 - notes) / 65.0,
            < 250 => 3 + 0.008 * (250 - notes),
            < 500 => 2 + 0.004 * (500 - notes),
            < 1000 => 1 + 0.002 * (1000 - notes),
            _ => 1,
        };
        return Math.Max(totalScale, noteScale);
    }

    private static BmsJudgementWindowTable createWindows((double Rate, bool Tail) key)
    {
        var (rate, tail) = key;
        var perfect = interpolate(rate, [0, 8, 15, 18, 21]);
        var great = interpolate(rate, [0, 24, 30, 40, 60]);
        var good = interpolate(rate, [0, 40, 60, 100, 120]);
        if (tail)
            perfect = great = good;

        BmsJudgementWindow[] rows =
        [
            new(HitResult.Perfect, -perfect, perfect),
            new(HitResult.Great, -great, great),
            new(HitResult.Good, -good, good),
            new(HitResult.Ok, -200, 200),
        ];
        return tail
            ? new BmsJudgementWindowTable(rows)
            : new BmsJudgementWindowTable([.. rows, new BmsJudgementWindow(HitResult.Miss, 0, 1000)]) { LongNoteHeadPoorOffset = good };
    }

    private static double interpolate(double rate, ReadOnlySpan<double> values)
    {
        rate = Math.Max(0, rate);
        if (rate >= 1) return Math.Min(200, Math.Truncate(values[4] * rate * 1000) / 1000);

        var index = (int)(rate * 4);
        return Math.Truncate((values[index] + (values[index + 1] - values[index]) * (rate * 4 - index)) * 1000) / 1000;
    }
}
