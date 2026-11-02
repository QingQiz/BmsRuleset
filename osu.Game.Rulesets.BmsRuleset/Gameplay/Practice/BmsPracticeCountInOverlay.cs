using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal partial class BmsPracticeCountInOverlay : VisibilityContainer
{
    private readonly OsuSpriteText number;
    private readonly OsuSpriteText message;
    private readonly CircularProgress progress;
    private double total;

    internal BmsPracticeCountInOverlay()
    {
        Name = "Practice playfield countdown";
        RelativeSizeAxes = Axes.Both;
        var colours = new OverlayColourProvider(OverlayColourScheme.Purple);
        InternalChildren =
        [
            new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(200), Colour = colours.Background6 },
            new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(150), Colour = colours.Background4 },
            progress = new CircularProgress
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(154),
                InnerRadius = 7f / 154,
                RoundedCaps = true,
                Colour = colours.Highlight1,
            },
            number = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Torus.With(size: 70, weight: FontWeight.Light),
                UseFullGlyphHeight = false,
            },
            message = new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Y = 110,
                Font = OsuFont.GetFont(size: 22),
            },
        ];
    }

    internal void ShowPreparation()
    {
        Show();
        number.Text = default;
        message.Text = BmsStrings.PracticePreparing;
        progress.Progress = 0;
    }

    internal void Display(BmsPracticeCountdown countdown)
    {
        if (!IsPresent)
            total = countdown.RemainingMilliseconds;
        total = System.Math.Max(total, countdown.RemainingMilliseconds);
        if (countdown.RemainingMilliseconds <= 0 && !countdown.IsPaused)
        {
            Hide();
            return;
        }
        Show();
        number.Text = BmsStrings.PracticeCountdownNumber(countdown.SecondsRemaining);
        message.Text = countdown.IsPaused ? BmsStrings.PracticePaused : default;
        progress.Progress = total <= 0 ? 1 : 1 - countdown.RemainingMilliseconds / total;
    }

    protected override void PopIn() => this.FadeIn();

    protected override void PopOut() => this.FadeOut();
}
