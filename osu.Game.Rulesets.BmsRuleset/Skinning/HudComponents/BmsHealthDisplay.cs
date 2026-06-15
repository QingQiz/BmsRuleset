using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Utils;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;

/// <summary>
///     BMS-style groove gauge display that replaces osu!'s native health bar UI.
/// </summary>
public sealed partial class BmsHealthDisplay : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    private const float bar_width = 30;
    private const float bar_height = 400;
    private const float clear_border = 0.8f;

    private readonly Box normalFill;
    private readonly Box clearLine;

    private BindableNumber<double>? health;
    private double displayedHealth;

    [Resolved]
    private HealthProcessor healthProcessor { get; set; } = null!;

    public BmsHealthDisplay()
    {
        Size = new Vector2(58, bar_height);

        InternalChildren =
        [
            new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Size = new Vector2(bar_width, bar_height),
                Children =
                [
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(8, 10, 14, 255),
                    },
                    new GaugeSegment
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Height = clear_border,
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
                        Y = -bar_height * clear_border,
                        Height = 2,
                        Colour = new Color4(255, 240, 120, 255),
                    },
                    new GaugeTicks(),
                    new GaugeBorder(),
                ],
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        health = healthProcessor.Health.GetBoundCopy();
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
        normalFill.Height = (float)clampedHealth;

        var fillColour = clampedHealth switch
        {
            < 0.2 => new Color4(255, 45, 40, 255),
            < clear_border => new Color4(255, 190, 45, 255),
            _ => new Color4(45, 225, 255, 255),
        };

        normalFill.Colour = fillColour;
        clearLine.Alpha = clampedHealth >= clear_border ? 0.85f : 1;
    }

    private sealed partial class GaugeSegment : Box
    {
        public GaugeSegment()
        {
            RelativeSizeAxes = Axes.Both;
            Width = 1;
        }
    }

    private sealed partial class GaugeTicks : CompositeDrawable
    {
        public GaugeTicks()
        {
            RelativeSizeAxes = Axes.Both;

            var ticks = new Drawable[9];

            for (var i = 0; i < ticks.Length; i++)
            {
                ticks[i] = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Width = i % 2 == 0 ? 1 : 0.55f,
                    Height = 1,
                    Y = -bar_height * (i + 1) / 10,
                    Alpha = i == 7 ? 0 : 0.35f,
                    Colour = Color4.White,
                };
            }

            InternalChildren = ticks;
        }
    }

    private sealed partial class GaugeBorder : CompositeDrawable
    {
        public GaugeBorder()
        {
            RelativeSizeAxes = Axes.Both;
            InternalChildren =
            [
                new Box { RelativeSizeAxes = Axes.X, Height = 2, Colour = Color4.White },
                new Box { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, RelativeSizeAxes = Axes.X, Height = 2, Colour = Color4.White },
                new Box { RelativeSizeAxes = Axes.Y, Width = 2, Colour = Color4.White },
                new Box { Anchor = Anchor.TopRight, Origin = Anchor.TopRight, RelativeSizeAxes = Axes.Y, Width = 2, Colour = Color4.White },
            ];
        }
    }
}
