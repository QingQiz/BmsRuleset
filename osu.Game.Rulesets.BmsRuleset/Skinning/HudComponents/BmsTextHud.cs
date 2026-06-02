using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;

public sealed partial class BmsTextHud : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    private SpriteText mainText = null!;
    private SpriteText arrowText = null!;

    public BmsTextHud()
    {
        Anchor = Anchor.TopCentre;
        Origin = Anchor.TopCentre;
        Y = 36;
        AutoSizeAxes = Axes.Both;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        BmsEventBus.TextEvent += showText;
        BmsEventBus.ScrollSpeedChangeEvent += x => showScrollSpeed(x, BmsPlayerShared.ConfiguredScrollSpeed);
        mainText.Text = "Game Start";
        this.Delay(1000).FadeOut(1000);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black,
                Alpha = 0.55f,
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Horizontal = 10, Vertical = 4 },
                Children =
                [
                    arrowText = new SpriteText
                    {
                        Font = OsuFont.Default.With(size: 24, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                    mainText = new SpriteText
                    {
                        Font = OsuFont.Default.With(size: 24, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                ],
            },
        ];
    }

    private void showScrollSpeed(double speed, double configured)
    {
        var delta = speed - configured;
        var colour = delta > 0 ? new Color4(255, 200, 0, 255)
            : delta < 0 ? new Color4(100, 180, 255, 255)
            : Color4.White;

        arrowText.Text = delta > 0 ? ">>" : delta < 0 ? "<<" : string.Empty;
        arrowText.Colour = colour;
        mainText.Text = $"{speed:0.0}";
        mainText.Colour = colour;
        animateShow(1000);
    }

    private void showText(string text)
    {
        arrowText.Text = string.Empty;
        mainText.Text = text;
        mainText.Colour = Color4.White;
        animateShow(1500);
    }

    private void animateShow(double displayDurationMs)
    {
        ClearTransforms();
        this.FadeIn(80).Delay(displayDurationMs).FadeOut(300);
    }
}
