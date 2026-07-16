using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Play.HUD;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

/// <summary>
/// A BMS-style vertical song progress indicator.
/// </summary>
public sealed partial class BmsSongProgress : SongProgress
{
    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.SongProgressColour), nameof(BmsStrings.SongProgressColourDescription))]
    public BindableColour4 IndicatorColour { get; } = new(new Color4(255, 45, 45, 255));

    private readonly Container marker;

    public BmsSongProgress()
    {
        Container sharpIndicator1;
        BufferedContainer blurredIndicator1;
        RelativeSizeAxes = Axes.Y;
        Width = 4;
        Height = 1;

        Child = marker = new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Size = new Vector2(4, 12),
            Children =
            [
                blurredIndicator1 = new BufferedContainer(cachedFrameBuffer: true)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(72, 88),
                    BlurSigma = new Vector2(6),
                    DrawOriginal = false,
                    EffectBlending = BlendingParameters.Additive,
                    Child = createIndicator(withGlow: true),
                },
                sharpIndicator1 = createIndicator(withGlow: true),
            ],
        };

        IndicatorColour.BindValueChanged(colour =>
        {
            blurredIndicator1.EffectColour = colour.NewValue;
            sharpIndicator1.Colour = colour.NewValue;
            sharpIndicator1.EdgeEffect = sharpIndicator1.EdgeEffect with { Colour = colour.NewValue };
        }, true);
    }

    protected override void UpdateProgress(double progress, bool isIntro)
    {
        marker.Y = CalculateMarkerPosition(isIntro ? 0 : progress, DrawHeight - marker.DrawHeight);
    }

    internal static float CalculateMarkerPosition(double progress, float travelDistance)
        => (float)Math.Clamp(progress, 0, 1) * Math.Max(0, travelDistance);

    private static Container createIndicator(float alpha = 1, bool withGlow = false) => new()
    {
        Anchor = Anchor.Centre,
        Origin = Anchor.Centre,
        Size = new Vector2(8, 24),
        Masking = true,
        CornerRadius = 3,
        Alpha = alpha,
        EdgeEffect = withGlow
            ? new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = Color4.White,
                Radius = 12,
                Roundness = 3,
            }
            : new EdgeEffectParameters(),
        Child = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = Color4.White,
        },
    };
}
