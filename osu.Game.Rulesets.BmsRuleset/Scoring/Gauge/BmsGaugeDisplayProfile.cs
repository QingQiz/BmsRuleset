using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public readonly record struct BmsGaugeDisplayProfile(
    double? ClearThreshold,
    bool ShowClearLine,
    BmsGaugeColourMode ColourMode,
    Color4 FillColour,
    double RedZoneThreshold = 0.2
);
