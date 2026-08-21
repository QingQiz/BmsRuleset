using System;
using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public static class BmsGaugeProfileFactory
{
    private static readonly IReadOnlyList<BmsGaugeGutsRule> hard_guts =
    [
        new(0.10, 0.4),
        new(0.20, 0.5),
        new(0.30, 0.6),
        new(0.40, 0.7),
        new(0.50, 0.8),
    ];

    private static readonly IReadOnlyList<BmsGaugeGutsRule> class_guts =
    [
        new(0.05, 0.4),
        new(0.10, 0.5),
        new(0.15, 0.6),
        new(0.20, 0.7),
        new(0.25, 0.8),
    ];

    private static readonly IReadOnlyList<BmsGaugeGutsRule> lr2_guts =
    [
        new(0.30, 0.6),
    ];

    public static BmsGaugeProfile Create(BmsGaugeType type, BmsGaugeProfileFamily family = BmsGaugeProfileFamily.SevenKeys) => family switch
    {
        BmsGaugeProfileFamily.FiveKeys => fiveKeys(type),
        BmsGaugeProfileFamily.SevenKeys => sevenKeys(type),
        BmsGaugeProfileFamily.Pms => pms(type),
        BmsGaugeProfileFamily.Keyboard => keyboard(type),
        BmsGaugeProfileFamily.Lr2 => lr2(type),
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
    };

    private static BmsGaugeProfile fiveKeys(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(0.2, 1, 0.5, 1, 1, 0.5, -0.015, -0.03, -0.005),
        BmsGaugeType.Easy => total(0.2, 1, 0.75, 1, 1, 0.5, -0.015, -0.045, -0.01),
        BmsGaugeType.Normal => total(0.2, 1, 0.75, 1, 1, 0.5, -0.03, -0.06, -0.02),
        BmsGaugeType.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, red, 0, 0, 0, -0.05, -0.10, -0.05, []),
        BmsGaugeType.ExHard => survival(BmsGaugeAlgorithm.ModifyDamage, purple, 0, 0, 0, -0.10, -0.20, -0.10, []),
        BmsGaugeType.Hazard => survival(BmsGaugeAlgorithm.Fixed, gold, 0, 0, 0, -1, -1, -1, []),
        BmsGaugeType.Class => survival(BmsGaugeAlgorithm.Fixed, red, 0.0001, 0.0001, 0, -0.005, -0.01, -0.005, []),
        BmsGaugeType.ExClass => survival(BmsGaugeAlgorithm.Fixed, purple, 0.0001, 0.0001, 0, -0.01, -0.02, -0.01, []),
        BmsGaugeType.ExHardClass => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0001, 0.0001, 0, -0.025, -0.05, -0.025, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile sevenKeys(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(0.2, 1, 0.6, 1, 1, 0.5, -0.015, -0.03, -0.005),
        BmsGaugeType.Easy => total(0.2, 1, 0.8, 1, 1, 0.5, -0.015, -0.045, -0.01),
        BmsGaugeType.Normal => total(0.2, 1, 0.8, 1, 1, 0.5, -0.03, -0.06, -0.02),
        BmsGaugeType.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, red, 0.0015, 0.0012, 0.0003, -0.05, -0.10, -0.05, hard_guts),
        BmsGaugeType.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, purple, 0.0015, 0.0006, 0, -0.08, -0.16, -0.08, []),
        BmsGaugeType.Hazard => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0015, 0.0006, 0, -1, -1, -0.10, []),
        BmsGaugeType.Class => survival(BmsGaugeAlgorithm.Fixed, red, 0.0015, 0.0012, 0.0006, -0.015, -0.03, -0.015, class_guts),
        BmsGaugeType.ExClass => survival(BmsGaugeAlgorithm.Fixed, purple, 0.0015, 0.0012, 0.0003, -0.03, -0.06, -0.03, []),
        BmsGaugeType.ExHardClass => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0015, 0.0006, 0, -0.05, -0.10, -0.05, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile pms(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(0.3, 1.2, 0.65, 1, 1, 0.5, -0.01, -0.02, -0.02),
        BmsGaugeType.Easy => total(0.3, 1.2, 0.85, 1, 1, 0.5, -0.01, -0.03, -0.03),
        BmsGaugeType.Normal => total(0.3, 1.2, 0.85, 1, 1, 0.5, -0.02, -0.06, -0.06),
        BmsGaugeType.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, red, 0.0015, 0.0012, 0.0003, -0.05, -0.10, -0.10, hard_guts),
        BmsGaugeType.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, purple, 0.0015, 0.0006, 0, -0.10, -0.15, -0.15, []),
        BmsGaugeType.Hazard => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0015, 0.0006, 0, -1, -1, -1, []),
        BmsGaugeType.Class => survival(BmsGaugeAlgorithm.Fixed, red, 0.0015, 0.0012, 0.0006, -0.015, -0.03, -0.03, class_guts),
        BmsGaugeType.ExClass => survival(BmsGaugeAlgorithm.Fixed, purple, 0.0015, 0.0012, 0.0003, -0.03, -0.06, -0.06, []),
        BmsGaugeType.ExHardClass => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0015, 0.0006, 0, -0.05, -0.10, -0.10, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile keyboard(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(0.3, 1, 0.5, 1, 1, 0.5, -0.01, -0.02, -0.01),
        BmsGaugeType.Easy => total(0.2, 1, 0.7, 1, 1, 0.5, -0.01, -0.03, -0.01),
        BmsGaugeType.Normal => total(0.2, 1, 0.7, 1, 1, 0.5, -0.02, -0.04, -0.02),
        BmsGaugeType.Hard => survival(BmsGaugeAlgorithm.LimitIncrement, red, 0.002, 0.002, 0.001, -0.04, -0.08, -0.04, hard_guts),
        BmsGaugeType.ExHard => survival(BmsGaugeAlgorithm.LimitIncrement, purple, 0.002, 0.001, 0, -0.06, -0.12, -0.06, []),
        BmsGaugeType.Hazard => survival(BmsGaugeAlgorithm.Fixed, gold, 0.002, 0.001, 0, -1, -1, -1, []),
        BmsGaugeType.Class => survival(BmsGaugeAlgorithm.Fixed, red, 0.002, 0.002, 0.001, -0.015, -0.03, -0.015, class_guts),
        BmsGaugeType.ExClass => survival(BmsGaugeAlgorithm.Fixed, purple, 0.002, 0.002, 0.001, -0.03, -0.06, -0.03, []),
        BmsGaugeType.ExHardClass => survival(BmsGaugeAlgorithm.Fixed, gold, 0.002, 0.001, 0, -0.05, -0.10, -0.05, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile lr2(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(0.2, 1, 0.6, 1.2, 1.2, 0.6, -0.032, -0.048, -0.016),
        BmsGaugeType.Easy => total(0.2, 1, 0.8, 1.2, 1.2, 0.6, -0.032, -0.048, -0.016),
        BmsGaugeType.Normal => total(0.2, 1, 0.8, 1, 1, 0.5, -0.04, -0.06, -0.02),
        BmsGaugeType.Hard => survival(BmsGaugeAlgorithm.ModifyDamage, red, 0.001, 0.001, 0.0005, -0.06, -0.10, -0.02, lr2_guts),
        BmsGaugeType.ExHard => survival(BmsGaugeAlgorithm.ModifyDamage, purple, 0.001, 0.001, 0.0005, -0.12, -0.20, -0.02, []),
        BmsGaugeType.Hazard => survival(BmsGaugeAlgorithm.Fixed, gold, 0.0015, 0.0006, 0, -1, -1, -0.10, []),
        BmsGaugeType.Class => survival(BmsGaugeAlgorithm.Fixed, red, 0.001, 0.001, 0.0005, -0.02, -0.03, -0.02, lr2_guts),
        BmsGaugeType.ExClass => survival(BmsGaugeAlgorithm.Fixed, purple, 0.001, 0.001, 0.0005, -0.06, -0.10, -0.02, lr2_guts),
        BmsGaugeType.ExHardClass => survival(BmsGaugeAlgorithm.Fixed, gold, 0.001, 0.001, 0.0005, -0.12, -0.20, -0.02, []),
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
        new BmsGaugeDisplayProfile(clearThreshold, true, BmsGaugeColourMode.GrooveDynamic, green, initialHealth),
        []);

    private static BmsGaugeProfile survival(
        BmsGaugeAlgorithm algorithm,
        Color4 fillColour,
        double perfectGain,
        double greatGain,
        double goodGain,
        double badDelta,
        double poorDelta,
        double emptyPoorDelta,
        IReadOnlyList<BmsGaugeGutsRule> gutsRules) => new(
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
        new BmsGaugeDisplayProfile(null, false, BmsGaugeColourMode.Fixed, fillColour, 1),
        gutsRules);

    private static Color4 red => new(220, 55, 50, 255);

    private static Color4 purple => new(195, 55, 210, 255);

    private static Color4 gold => new(255, 215, 0, 255);

    private static Color4 green => new(45, 225, 80, 255);
}
