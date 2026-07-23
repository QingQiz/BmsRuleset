using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Rulesets.UI;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsTextHud : BmsHudComponent
{
    private readonly SpriteText mainText;
    private readonly SpriteText arrowText;

    private IBmsGameplayEvents? gameplayEvents;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    // Null in tests / non-OsuGame hosts. When the skin editor is open the HUD is forced visible so the
    // (otherwise alpha=0) text box can be positioned and sized.
    public BmsTextHud()
    {
        Anchor = Anchor.TopCentre;
        Origin = Anchor.TopCentre;
        Y = 36;
        AutoSizeAxes = Axes.Both;

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

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayEvents != null)
        {
            gameplayEvents.Text -= showText;
            gameplayEvents.ScrollSpeedChanged -= showScrollSpeed;
        }

        base.Dispose(isDisposing);
    }

    #endregion

    protected override void LoadComplete()
    {
        base.LoadComplete();
        gameplayEvents = (drawableRuleset as BmsDrawableRuleset)?.GameplayEvents;

        if (gameplayEvents != null)
        {
            gameplayEvents.Text += showText;
            gameplayEvents.ScrollSpeedChanged += showScrollSpeed;
        }

        if (SkinEditor != null)
            SkinEditor.State.BindValueChanged(_ => updateEditModeVisibility(), true);
        else
            applyEditModeVisibility(false);
    }

    private void updateEditModeVisibility() => applyEditModeVisibility(SkinEditor?.State.Value == Visibility.Visible);

    private void applyEditModeVisibility(bool isEditing)
    {
        ClearTransforms();

        if (isEditing)
        {
            // Skin editor: stable, fully-visible placeholder so the box can be dragged/sized.
            arrowText.Text = string.Empty;
            arrowText.Colour = Color4.White;
            mainText.Text = "Sample Text Event";
            mainText.Colour = Color4.White;
            Alpha = 1;
            return;
        }

        mainText.Text = "Game Start";

        // FadeOut runs on the transform clock; only schedule once loaded (no-op pre-load / in unit tests).
        if (LoadState == LoadState.Loaded)
            this.Delay(1000).FadeOut(1000);
    }

    private void showScrollSpeed(double multiplier)
    {
        var delta = multiplier - 1;
        var colour = delta > 0 ? new Color4(255, 200, 0, 255)
            : delta < 0 ? new Color4(100, 180, 255, 255)
            : Color4.White;

        arrowText.Text = delta > 0 ? ">>" : delta < 0 ? "<<" : string.Empty;
        arrowText.Colour = colour;
        mainText.Text = $"{multiplier:0.0}x";
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
        // Keep the edit-mode placeholder stable; don't fade out from gameplay text events while editing.
        if (SkinEditor?.State.Value == Visibility.Visible)
            return;

        ClearTransforms();
        this.FadeIn(80).Delay(displayDurationMs).FadeOut(300);
    }
}
