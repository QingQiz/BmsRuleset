using System.Collections.Immutable;

namespace RhythmEngine.Profiles.Bms;

public static class BmsGaugeProfiles
{
    private static readonly ImmutableArray<BmsGaugeGutsRule> hard_guts =
    [
        new(0.10, 0.4),
        new(0.20, 0.5),
        new(0.30, 0.6),
        new(0.40, 0.7),
        new(0.50, 0.8),
    ];

    private static readonly ImmutableArray<BmsGaugeGutsRule> class_guts =
    [
        new(0.05, 0.4),
        new(0.10, 0.5),
        new(0.15, 0.6),
        new(0.20, 0.7),
        new(0.25, 0.8),
    ];

    private static readonly ImmutableArray<BmsGaugeGutsRule> lr2_guts =
    [
        new(0.30, 0.6),
    ];

    public static BmsGaugeProfile Create(BmsGauge type, BmsGaugeProfileFamily family = BmsGaugeProfileFamily.SevenKeys) => family switch
    {
        BmsGaugeProfileFamily.FiveKeys => fiveKeys(type),
        BmsGaugeProfileFamily.SevenKeys => sevenKeys(type),
        BmsGaugeProfileFamily.Pms => pms(type),
        BmsGaugeProfileFamily.Keyboard => keyboard(type),
        BmsGaugeProfileFamily.Lr2 => lr2(type),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
    };

    private static BmsGaugeProfile fiveKeys(BmsGauge type) => type switch
    {
        BmsGauge.AssistEasy => total(0.2, 1, 0.5, 1, 1, 0.5, -0.015, -0.03, -0.005),
        BmsGauge.Easy => total(0.2, 1, 0.75, 1, 1, 0.5, -0.015, -0.045, -0.01),
        BmsGauge.Normal => total(0.2, 1, 0.75, 1, 1, 0.5, -0.03, -0.06, -0.02),
        BmsGauge.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, 0, 0, 0, -0.05, -0.10, -0.05, []),
        BmsGauge.ExHard => survival(BmsGaugeAlgorithm.ModifyDamage, 0, 0, 0, -0.10, -0.20, -0.10, []),
        BmsGauge.Hazard => survival(BmsGaugeAlgorithm.Fixed, 0, 0, 0, -1, -1, -1, []),
        BmsGauge.Grade => survival(BmsGaugeAlgorithm.Fixed, 0.0001, 0.0001, 0, -0.005, -0.01, -0.005, []),
        BmsGauge.GradeHard => survival(BmsGaugeAlgorithm.Fixed, 0.0001, 0.0001, 0, -0.01, -0.02, -0.01, []),
        BmsGauge.GradeExHard => survival(BmsGaugeAlgorithm.Fixed, 0.0001, 0.0001, 0, -0.025, -0.05, -0.025, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile sevenKeys(BmsGauge type) => type switch
    {
        BmsGauge.AssistEasy => total(0.2, 1, 0.6, 1, 1, 0.5, -0.015, -0.03, -0.005),
        BmsGauge.Easy => total(0.2, 1, 0.8, 1, 1, 0.5, -0.015, -0.045, -0.01),
        BmsGauge.Normal => total(0.2, 1, 0.8, 1, 1, 0.5, -0.03, -0.06, -0.02),
        BmsGauge.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.0015, 0.0012, 0.0003, -0.05, -0.10, -0.05, hard_guts),
        BmsGauge.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.0015, 0.0006, 0, -0.08, -0.16, -0.08, []),
        BmsGauge.Hazard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0006, 0, -1, -1, -0.10, []),
        BmsGauge.Grade => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0012, 0.0006, -0.015, -0.03, -0.015, class_guts),
        BmsGauge.GradeHard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0012, 0.0003, -0.03, -0.06, -0.03, []),
        BmsGauge.GradeExHard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0006, 0, -0.05, -0.10, -0.05, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile pms(BmsGauge type) => type switch
    {
        BmsGauge.AssistEasy => total(0.3, 1.2, 0.65, 1, 1, 0.5, -0.01, -0.02, -0.02),
        BmsGauge.Easy => total(0.3, 1.2, 0.85, 1, 1, 0.5, -0.01, -0.03, -0.03),
        BmsGauge.Normal => total(0.3, 1.2, 0.85, 1, 1, 0.5, -0.02, -0.06, -0.06),
        BmsGauge.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.0015, 0.0012, 0.0003, -0.05, -0.10, -0.10, hard_guts),
        BmsGauge.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.0015, 0.0006, 0, -0.10, -0.15, -0.15, []),
        BmsGauge.Hazard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0006, 0, -1, -1, -1, []),
        BmsGauge.Grade => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0012, 0.0006, -0.015, -0.03, -0.03, class_guts),
        BmsGauge.GradeHard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0012, 0.0003, -0.03, -0.06, -0.06, []),
        BmsGauge.GradeExHard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0006, 0, -0.05, -0.10, -0.10, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile keyboard(BmsGauge type) => type switch
    {
        BmsGauge.AssistEasy => total(0.3, 1, 0.5, 1, 1, 0.5, -0.01, -0.02, -0.01),
        BmsGauge.Easy => total(0.2, 1, 0.7, 1, 1, 0.5, -0.01, -0.03, -0.01),
        BmsGauge.Normal => total(0.2, 1, 0.7, 1, 1, 0.5, -0.02, -0.04, -0.02),
        BmsGauge.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.002, 0.002, 0.001, -0.04, -0.08, -0.04, hard_guts),
        BmsGauge.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, 0.002, 0.001, 0, -0.06, -0.12, -0.06, []),
        BmsGauge.Hazard => survival(BmsGaugeAlgorithm.Fixed, 0.002, 0.001, 0, -1, -1, -1, []),
        BmsGauge.Grade => survival(BmsGaugeAlgorithm.Fixed, 0.002, 0.002, 0.001, -0.015, -0.03, -0.015, class_guts),
        BmsGauge.GradeHard => survival(BmsGaugeAlgorithm.Fixed, 0.002, 0.002, 0.001, -0.03, -0.06, -0.03, []),
        BmsGauge.GradeExHard => survival(BmsGaugeAlgorithm.Fixed, 0.002, 0.001, 0, -0.05, -0.10, -0.05, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile lr2(BmsGauge type) => type switch
    {
        BmsGauge.AssistEasy => total(0.2, 1, 0.6, 1.2, 1.2, 0.6, -0.032, -0.048, -0.016),
        BmsGauge.Easy => total(0.2, 1, 0.8, 1.2, 1.2, 0.6, -0.032, -0.048, -0.016),
        BmsGauge.Normal => total(0.2, 1, 0.8, 1, 1, 0.5, -0.04, -0.06, -0.02),
        BmsGauge.Hard => survival(BmsGaugeAlgorithm.ModifyDamage, 0.001, 0.001, 0.0005, -0.06, -0.10, -0.02, lr2_guts),
        BmsGauge.ExHard => survival(BmsGaugeAlgorithm.ModifyDamage, 0.001, 0.001, 0.0005, -0.12, -0.20, -0.02, []),
        BmsGauge.Hazard => survival(BmsGaugeAlgorithm.Fixed, 0.0015, 0.0006, 0, -1, -1, -0.10, []),
        BmsGauge.Grade => survival(BmsGaugeAlgorithm.Fixed, 0.001, 0.001, 0.0005, -0.02, -0.03, -0.02, lr2_guts),
        BmsGauge.GradeHard => survival(BmsGaugeAlgorithm.Fixed, 0.001, 0.001, 0.0005, -0.06, -0.10, -0.02, lr2_guts),
        BmsGauge.GradeExHard => survival(BmsGaugeAlgorithm.Fixed, 0.001, 0.001, 0.0005, -0.12, -0.20, -0.02, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile total(
        double initialHealth,
        double maxHealth,
        double clearThreshold,
        double perfectGain,
        double greatGain,
        double goodGain,
        double badDelta,
        double poorDelta,
        double emptyPoorDelta) => new(
        BmsGaugeAlgorithm.Total,
        initialHealth,
        maxHealth,
        clearThreshold,
        perfectGain,
        greatGain,
        goodGain,
        badDelta,
        poorDelta,
        emptyPoorDelta,
        []);

    private static BmsGaugeProfile survival(
        BmsGaugeAlgorithm algorithm,
        double perfectGain,
        double greatGain,
        double goodGain,
        double badDelta,
        double poorDelta,
        double emptyPoorDelta,
        ImmutableArray<BmsGaugeGutsRule> gutsRules) => new(
        algorithm,
        1,
        1,
        0,
        perfectGain,
        greatGain,
        goodGain,
        badDelta,
        poorDelta,
        emptyPoorDelta,
        gutsRules);

    public static BmsGaugeProfile CreateLr2Oraja(BmsGauge type)
    {
        var lr2 = Create(type, BmsGaugeProfileFamily.Lr2);
        var survival = lr2.Algorithm != BmsGaugeAlgorithm.Total;
        lr2 = lr2 with
        {
            DefaultTotal = calculateDefaultTotal,
            DamageScale = calculateDamageScale,
            FailureThreshold = survival ? 0.02 : 0,
            GutsRules = lr2.GutsRules.Length > 0 ? [new BmsGaugeGutsRule(0.32, 0.6)] : [],
        };

        // LR2's DEATH gauge counts BAD/missed notes, while excess presses preserve it.
        return type == BmsGauge.Hazard
            ? lr2 with { PerfectGain = 0, GreatGain = 0, GoodGain = 0, EmptyPoorDelta = 0 }
            : lr2;
    }

    private static double calculateDefaultTotal(int notes)
        => 160 + (Math.Max(1, notes) + Math.Clamp(notes - 400, 0, 200)) * 0.16;

    private static double calculateDamageScale(double total, int notes)
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
}
