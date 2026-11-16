using System;
using RhythmEngine.Profiles.Bms;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

public static class BmsGaugeProfileFactory
{
    internal static LocalisableString NameFor(BmsGaugeType type) => BmsStrings.GaugeName(type.ToString().ToLowerInvariant());

    internal static Color4 HistoryColourFor(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => new Color4(75, 210, 235, 255),
        BmsGaugeType.Easy => new Color4(75, 150, 255, 255),
        _ => CreateDisplay(type, Create(type)).FillColour,
    };
    public static BmsGaugeProfile Create(BmsGaugeType type, BmsGaugeProfileFamily family = BmsGaugeProfileFamily.SevenKeys)
        => BmsGaugeProfiles.Create(ToEngineGauge(type), family);

    internal static BmsGauge ToEngineGauge(BmsGaugeType type) => type switch
    {
        BmsGaugeType.AssistEasy => BmsGauge.AssistEasy,
        BmsGaugeType.Easy => BmsGauge.Easy,
        BmsGaugeType.Normal => BmsGauge.Normal,
        BmsGaugeType.Hard => BmsGauge.Hard,
        BmsGaugeType.ExHard => BmsGauge.ExHard,
        BmsGaugeType.Hazard => BmsGauge.Hazard,
        BmsGaugeType.Class => BmsGauge.Grade,
        BmsGaugeType.ExClass => BmsGauge.GradeHard,
        BmsGaugeType.ExHardClass => BmsGauge.GradeExHard,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static BmsGaugeDisplayProfile CreateDisplay(BmsGaugeType type, BmsGaugeProfile profile)
        => profile.Algorithm == BmsGaugeAlgorithm.Total
            ? new BmsGaugeDisplayProfile(profile.ClearThreshold, true, BmsGaugeColourMode.GrooveDynamic, new Color4(45, 225, 80, 255), profile.InitialHealth)
            : new BmsGaugeDisplayProfile(null, false, BmsGaugeColourMode.Fixed, type switch
            {
                BmsGaugeType.Hard or BmsGaugeType.Class => new Color4(220, 55, 50, 255),
                BmsGaugeType.ExHard or BmsGaugeType.ExClass => new Color4(195, 55, 210, 255),
                BmsGaugeType.Hazard or BmsGaugeType.ExHardClass => new Color4(255, 215, 0, 255),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
            }, profile.InitialHealth);
}
