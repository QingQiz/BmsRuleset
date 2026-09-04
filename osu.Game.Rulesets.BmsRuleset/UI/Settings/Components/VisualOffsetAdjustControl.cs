using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;

internal partial class VisualOffsetAdjustControl : CompositeDrawable
{
    public Bindable<double> Current
    {
        get => current.Current;
        init => current.Current = value;
    }

    private readonly BindableNumberWithCurrent<double> current = new();

    private readonly IBindableList<BmsVisualOffsetSuggestionStore.DataPoint> suggestionHistory =
        new BindableList<BmsVisualOffsetSuggestionStore.DataPoint>();

    internal readonly Bindable<double?> SuggestedOffset = new();

    private Container<Circle> notchContainer = null!;
    private SettingsNote hintNote = null!;
    private RoundedButton applySuggestion = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        suggestionHistory.BindTo(BmsRulesetRuntime.VisualOffsetSuggestions.History);

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(SettingsSection.ITEM_SPACING_V2),
            Children =
            [
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = BmsStrings.VisualOffset,
                    RelativeSizeAxes = Axes.X,
                    Current = { BindTarget = Current },
                    KeyboardStep = 1,
                    LabelFormat = BmsStrings.OffsetMilliseconds,
                    TooltipFormat = BmsStrings.VisualOffsetTooltip,
                }),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children =
                    [
                        notchContainer = new Container<Circle>
                        {
                            RelativeSizeAxes = Axes.X,
                            Width = 0.5f,
                            Height = 10,
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Padding = new MarginPadding
                            {
                                Horizontal = FormSliderBar<double>.InnerSlider.NUB_WIDTH / 2,
                            },
                        },
                        hintNote = new SettingsNote { RelativeSizeAxes = Axes.X },
                    ],
                },
                applySuggestion = new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Text = BmsStrings.ApplySuggestedVisualOffset,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Action = applySuggestedOffset,
                },
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        suggestionHistory.BindCollectionChanged(updateDisplay, true);
        current.BindValueChanged(_ => updateHintText());
        SuggestedOffset.BindValueChanged(_ => updateHintText(), true);
    }

    private void applySuggestedOffset()
    {
        if (SuggestedOffset.Value.HasValue)
            current.Value = SuggestedOffset.Value.Value;

        BmsRulesetRuntime.VisualOffsetSuggestions.Clear();
    }

    private void updateDisplay(object? _, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                foreach (BmsVisualOffsetSuggestionStore.DataPoint dataPoint in e.NewItems!)
                {
                    notchContainer.ForEach(notch => notch.Alpha *= 0.95f);
                    notchContainer.Add(new Circle
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 2,
                        RelativePositionAxes = Axes.X,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = colourProvider.Light1,
                        X = getXPositionForOffset(dataPoint.SuggestedVisualOffset),
                    });
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                foreach (BmsVisualOffsetSuggestionStore.DataPoint dataPoint in e.OldItems!)
                {
                    var notch = notchContainer.FirstOrDefault(candidate =>
                        candidate.X == getXPositionForOffset(dataPoint.SuggestedVisualOffset));

                    Debug.Assert(notch != null);
                    notchContainer.Remove(notch, true);
                }

                break;

            case NotifyCollectionChangedAction.Reset:
                notchContainer.Clear();
                break;
        }

        SuggestedOffset.Value = suggestionHistory.Any()
            ? Math.Round(suggestionHistory.Average(dataPoint => dataPoint.SuggestedVisualOffset))
            : null;
    }

    private float getXPositionForOffset(double offset) =>
        (float)(Math.Clamp(offset, current.MinValue, current.MaxValue) / (2 * current.MaxValue));

    private void updateHintText()
    {
        if (SuggestedOffset.Value == null)
        {
            applySuggestion.Enabled.Value = false;
            notchContainer.Hide();
            hintNote.Current.Value = new SettingsNote.Data(BmsStrings.VisualOffsetSuggestionNote, SettingsNote.Type.Informational);
            hintNote.MoveToY(0, 200, Easing.OutQuint);
        }
        else if (Math.Abs(SuggestedOffset.Value.Value - current.Value) < 1)
        {
            applySuggestion.Enabled.Value = false;
            notchContainer.Show();
            hintNote.Current.Value = new SettingsNote.Data(
                BmsStrings.VisualOffsetSuggestionCorrect(suggestionHistory.Count),
                SettingsNote.Type.Informational);
            hintNote.MoveToY(10, 200, Easing.OutQuint);
        }
        else
        {
            applySuggestion.Enabled.Value = true;
            notchContainer.Show();
            hintNote.Current.Value = new SettingsNote.Data(
                BmsStrings.VisualOffsetSuggestionReceived(
                    suggestionHistory.Count,
                    BmsStrings.OffsetMilliseconds(SuggestedOffset.Value.Value)),
                SettingsNote.Type.Informational);
            hintNote.MoveToY(10, 200, Easing.OutQuint);
        }
    }
}
