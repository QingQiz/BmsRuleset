using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

/// <summary>
///     BMS-style groove gauge display that replaces osu!'s native health bar UI.
/// </summary>
public sealed partial class BmsHealthDisplay : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    private const float clear_threshold = 0.8f;

    private readonly Box normalFill;
    private readonly Box clearLine;
    private readonly Box gaugeSegment;

    private BindableNumber<double>? health;
    private double displayedHealth;

    private readonly Bindable<BmsGaugeDisplayProfile> displayProfile =
        new(BmsGaugeProfileFactory.Create(BmsGaugeType.Normal).Display);

    [Resolved]
    private HealthProcessor healthProcessor { get; set; } = null!;

    public BmsHealthDisplay()
    {
        Size = new Vector2(30, 500);

        InternalChildren =
        [
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.5f, 1f),
                Children =
                [
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(8, 10, 14, 255),
                    },
                    gaugeSegment = new GaugeSegment
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Colour = new Color4(16, 42, 52, 255),
                    },
                    normalFill = new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.Both,
                        Width = 1,
                    },
                    clearLine = new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        RelativePositionAxes = Axes.Y,
                        Y = -clear_threshold,
                        Height = 2,
                        Colour = new Color4(255, 240, 120, 255),
                    },
                ],
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        health = healthProcessor.Health.GetBoundCopy();

        if (healthProcessor is BmsHealthProcessor bmsHealthProcessor)
            displayProfile.BindTo(bmsHealthProcessor.DisplayProfile);

        displayedHealth = health.Value;
        updateDisplay();
    }

    protected override void Update()
    {
        base.Update();

        if (health == null)
            return;

        displayedHealth = Interpolation.DampContinuously(displayedHealth, health.Value, 35, Time.Elapsed);
        updateDisplay();
    }

    private void updateDisplay()
    {
        var clampedHealth = double.IsFinite(displayedHealth) ? Math.Clamp(displayedHealth, 0, 1) : 0;
        var profile = displayProfile.Value;

        normalFill.Height = (float)clampedHealth;
        normalFill.Colour = profile.ColourMode == BmsGaugeColourMode.Fixed
            ? profile.FillColour
            : grooveColour(clampedHealth, profile.ClearThreshold ?? clear_threshold, profile.RedZoneThreshold);

        if (profile.ClearThreshold is double threshold)
        {
            gaugeSegment.Height = (float)threshold;
            gaugeSegment.Alpha = 1;
        }
        else
        {
            gaugeSegment.Alpha = 0;
        }

        if (profile.ShowClearLine && profile.ClearThreshold is double clearThreshold)
        {
            clearLine.Y = -(float)clearThreshold;
            clearLine.Alpha = clampedHealth >= clearThreshold ? 0.85f : 1;
        }
        else
        {
            clearLine.Alpha = 0;
        }
    }

    private static Color4 grooveColour(double clampedHealth, double clearThreshold, double redZoneThreshold) => clampedHealth switch
    {
        var value when value < redZoneThreshold => new Color4(255, 45, 40, 255),
        var value when value < clearThreshold => new Color4(255, 160, 30, 255),
        _ => new Color4(45, 225, 80, 255),
    };

    private sealed partial class GaugeSegment : Box
    {
        public GaugeSegment()
        {
            RelativeSizeAxes = Axes.Both;
            Width = 1;
        }
    }
}
