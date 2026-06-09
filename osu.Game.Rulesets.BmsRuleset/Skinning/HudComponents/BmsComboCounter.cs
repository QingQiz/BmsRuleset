using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.HudComponents;

public sealed partial class BmsComboCounter : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    public Bindable<int> Current { get; } = new BindableInt { MinValue = 0 };

    public int DisplayedCount
    {
        get => displayedCount;
        private set
        {
            if (displayedCount.Equals(value))
                return;

            displayedCountText.Text = value.ToString(CultureInfo.InvariantCulture);
            counterContainer.Size = displayedCountText.Size;
            displayedCount = value;
        }
    }

    private int displayedCount;
    private int previousValue;

    private const double fade_out_duration = 100;
    private const double rolling_duration = 20;

    private Container counterContainer = null!;
    private LegacySpriteText popOutCountText = null!;
    private LegacySpriteText displayedCountText = null!;

    private Color4 breakColour = Color4.Red;

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, ScoreProcessor scoreProcessor)
    {
        Anchor = Anchor.TopCentre;
        Origin = Anchor.Centre;

        Y = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ComboPosition)
        )?.Value ?? 300;

        breakColour = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ComboBreakColour)
        )?.Value ?? Color4.Red;

        AutoSizeAxes = Axes.Both;

        InternalChildren =
        [
            counterContainer = new Container
            {
                AlwaysPresent = true,
                Children =
                [
                    popOutCountText = new LegacySpriteText(LegacyFont.Combo)
                    {
                        Alpha = 0,
                        Blending = BlendingParameters.Additive,
                        BypassAutoSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                    displayedCountText = new LegacySpriteText(LegacyFont.Combo)
                    {
                        Alpha = 0,
                        AlwaysPresent = true,
                        BypassAutoSizeAxes = Axes.Both,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                ],
            },
        ];

        Current.BindTo(scoreProcessor.Combo);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        displayedCountText.Text = Current.Value.ToString(CultureInfo.InvariantCulture);
        popOutCountText.Text = Current.Value.ToString(CultureInfo.InvariantCulture);

        Current.BindValueChanged(combo => updateCount(combo.NewValue == 0), true);

        counterContainer.Size = displayedCountText.Size;
    }

    private void updateCount(bool rolling)
    {
        int prev = previousValue;
        previousValue = Current.Value;

        if (!IsLoaded)
            return;

        if (!rolling)
        {
            FinishTransforms(false, nameof(DisplayedCount));

            if (prev + 1 == Current.Value)
                onCountIncrement();
            else
                onCountChange();
        }
        else
            onCountRolling();
    }

    private void onCountIncrement()
    {
        popOutCountText.Hide();

        DisplayedCount = Current.Value;
        displayedCountText.ScaleTo(new Vector2(1f, 1.4f))
                          .ScaleTo(new Vector2(1f), 300, Easing.Out)
                          .FadeIn(120);
    }

    private void onCountChange()
    {
        popOutCountText.Hide();

        if (Current.Value == 0)
        {
            displayedCountText.FadeOut();
            displayedCountText.FlashColour(breakColour, 2000, Easing.OutQuint);
        }

        DisplayedCount = Current.Value;

        displayedCountText.ScaleTo(1f);
    }

    private void onCountRolling()
    {
        if (DisplayedCount > 0)
        {
            popOutCountText.Text = DisplayedCount.ToString(CultureInfo.InvariantCulture);
            popOutCountText.FadeTo(0.8f).FadeOut(200)
                           .ScaleTo(1f).ScaleTo(4f, 200);

            displayedCountText.FadeTo(0.5f, 300);

            if (Current.Value == 0)
                displayedCountText.FlashColour(breakColour, 2000, Easing.OutQuint);
        }

        if (DisplayedCount == 0 && Current.Value == 0)
            displayedCountText.FadeOut(fade_out_duration);

        this.TransformTo(nameof(DisplayedCount), Current.Value, getProportionalDuration(DisplayedCount, Current.Value));
    }

    private double getProportionalDuration(int currentValue, int newValue)
    {
        double difference = currentValue > newValue ? currentValue - newValue : newValue - currentValue;
        return difference * rolling_duration;
    }
}
