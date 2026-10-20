using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

/// <summary>
/// A BMS-style vertical song progress indicator.
/// </summary>
public sealed partial class BmsSongProgress : SongProgress
{
    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.SongProgressColour), nameof(BmsStrings.SongProgressColourDescription))]
    public BindableColour4 IndicatorColour { get; } = new(new Color4(255, 45, 45, 255));

    private readonly Container marker;

    [Resolved]
    private Player? player { get; set; }

    private double? dragProgress;
    private bool handleClick;

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
        marker.Y = CalculateMarkerPosition(dragProgress ?? (isIntro ? 0 : progress), DrawHeight - marker.DrawHeight);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Interactive.BindValueChanged(value =>
        {
            if (!value.NewValue)
            {
                dragProgress = null;
                handleClick = false;
            }
        });
    }

    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        => DrawRectangle.Inflate(new MarginPadding { Horizontal = 10 }).Contains(ToLocalSpace(screenSpacePos));

    protected override bool OnMouseDown(MouseDownEvent e)
        => handleClick = Interactive.Value && e.Button == MouseButton.Left;

    protected override bool OnClick(ClickEvent e)
    {
        if (!handleClick || !Interactive.Value || e.Button != MouseButton.Left)
            return false;

        handleClick = false;
        seek(progressAt(e.ScreenSpaceMousePosition));
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e)
    {
        if (!Interactive.Value || e.Button != MouseButton.Left)
            return false;

        handleClick = false;
        dragProgress = progressAt(e.ScreenSpaceMousePosition);
        return true;
    }

    protected override void OnDrag(DragEvent e)
    {
        if (dragProgress.HasValue)
            dragProgress = progressAt(e.ScreenSpaceMousePosition);
    }

    protected override void OnDragEnd(DragEndEvent e)
    {
        var commit = dragProgress.HasValue && Interactive.Value;
        dragProgress = null;

        // Replay seeking rebuilds gameplay state, so commit only after the drag preview ends.
        if (commit)
            seek(progressAt(e.ScreenSpaceMousePosition));
    }

    private double progressAt(Vector2 screenSpacePosition)
    {
        var travelDistance = DrawHeight - marker.DrawHeight;
        return travelDistance <= 0 ? 0 : Math.Clamp((ToLocalSpace(screenSpacePosition).Y - marker.DrawHeight / 2) / travelDistance, 0, 1);
    }

    private void seek(double progress) => player?.Seek(FirstHitTime + (LastHitTime - FirstHitTime) * progress);

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
