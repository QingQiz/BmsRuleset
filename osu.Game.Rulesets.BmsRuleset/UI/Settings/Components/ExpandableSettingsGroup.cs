using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;

public sealed partial class ExpandableSettingsGroup : FillFlowContainer, IFilterable
{
    private const float transition_duration = 300;

    private readonly SettingsItemV2 item;
    private readonly SettingsItemV2 toggle;
    private readonly IBindable<bool> expanded;
    private bool matchingFilter = true;

    internal Container ExpandableContent { get; }

    public ExpandableSettingsGroup(SettingsItemV2 toggle, SettingsItemV2 item, IBindable<bool> expanded)
    {
        this.toggle = toggle;
        this.item = item;
        this.expanded = expanded.GetBoundCopy();

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        // Keep the row spacing inside the clipped content so it collapses with the slider.
        InternalChildren =
        [
            toggle,
            ExpandableContent = new Container
            {
                RelativeSizeAxes = Axes.X,
                Masking = true,
                Padding = new MarginPadding { Top = SettingsSection.ITEM_SPACING_V2 },
                Child = item,
            },
        ];
        item.CanBeShown.Value = expanded.Value;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        expanded.BindValueChanged(state => updateExpandedState(state.NewValue == state.OldValue ? 0 : transition_duration), true);
    }

    private void updateExpandedState(float duration)
    {
        ExpandableContent.ClearTransforms();

        if (expanded.Value)
        {
            item.CanBeShown.Value = true;
            ExpandableContent.AutoSizeDuration = duration;
            ExpandableContent.AutoSizeEasing = Easing.OutQuint;
            ExpandableContent.AutoSizeAxes = Axes.Y;
        }
        else
        {
            ExpandableContent.AutoSizeAxes = Axes.None;
            ExpandableContent.ResizeHeightTo(0, duration, Easing.OutQuint)
                .OnComplete(_ => item.CanBeShown.Value = false);
        }
    }

    public IEnumerable<LocalisableString> FilterTerms => [];

    public bool FilteringActive { get; set; }

    public bool MatchingFilter
    {
        set
        {
            if (matchingFilter != value)
            {
                matchingFilter = value;
                Invalidate(Invalidation.Presence);
            }

            // Search filtering must remove the clipped region along with its unmatched child.
            ExpandableContent.Alpha = item.MatchingFilter ? 1 : 0;
            ExpandableContent.Padding = new MarginPadding { Top = toggle.MatchingFilter ? SettingsSection.ITEM_SPACING_V2 : 0 };
        }
    }

    public override bool IsPresent => base.IsPresent && matchingFilter;
}
