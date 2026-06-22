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

    public static BmsGaugeProfile Create(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => total(type, 0.2, 1, 0.6, 1, 1, 0.5, -0.015, -0.03, -0.005),
        BmsGaugeType.Easy => total(type, 0.2, 1, 0.8, 1, 1, 0.5, -0.015, -0.045, -0.01),
        BmsGaugeType.Normal => total(type, 0.2, 1, 0.8, 1, 1, 0.5, -0.03, -0.06, -0.02),
        BmsGaugeType.Hard => survival(type, BmsGaugeAlgorithm.LimitIncrement, red(220, 55, 50), 0.0015, 0.0012, 0.0003, -0.05, -0.10, -0.05, hard_guts),
        BmsGaugeType.ExHard => survival(type, BmsGaugeAlgorithm.LimitIncrement, purple(195, 55, 210), 0.0015, 0.0006, 0, -0.08, -0.16, -0.08, []),
        BmsGaugeType.Hazard => survival(type, BmsGaugeAlgorithm.Fixed, gold(255, 215, 0), 0.0015, 0.0006, 0, -1, -1, -0.10, []),
        BmsGaugeType.Class => survival(type, BmsGaugeAlgorithm.Fixed, orange(230, 95, 55), 0.0015, 0.0012, 0.0006, -0.015, -0.03, -0.015, class_guts),
        BmsGaugeType.ExClass => survival(type, BmsGaugeAlgorithm.Fixed, orange(230, 95, 55), 0.0015, 0.0012, 0.0003, -0.03, -0.06, -0.03, class_guts),
        BmsGaugeType.ExHardClass => survival(type, BmsGaugeAlgorithm.Fixed, purple(195, 55, 210), 0.0015, 0.0006, 0, -0.05, -0.10, -0.05, []),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static BmsGaugeProfile total(
        BmsGaugeType type,
        double initialHealth,
        double maxHealth,
        double clearThreshold,
        double perfectGain,
        double greatGain,
        double goodGain,
        double badDelta,
        double poorDelta,
        double emptyPoorDelta) => new(
        type,
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
        new BmsGaugeDisplayProfile(clearThreshold, true, BmsGaugeColourMode.GrooveDynamic, green(45, 225, 80), initialHealth),
        []
    );

    private static BmsGaugeProfile survival(
        BmsGaugeType type,
        BmsGaugeAlgorithm algorithm,
        Color4 fillColour,
        double perfectGain,
        double greatGain,
        double goodGain,
        double badDelta,
        double poorDelta,
        double emptyPoorDelta,
        IReadOnlyList<BmsGaugeGutsRule> gutsRules) => new(
        type,
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
        gutsRules
    );

    private static Color4 red(byte r, byte g, byte b) => new(r, g, b, 255);

    private static Color4 purple(byte r, byte g, byte b) => new(r, g, b, 255);

    private static Color4 gold(byte r, byte g, byte b) => new(r, g, b, 255);

    private static Color4 orange(byte r, byte g, byte b) => new(r, g, b, 255);

    private static Color4 green(byte r, byte g, byte b) => new(r, g, b, 255);
}
