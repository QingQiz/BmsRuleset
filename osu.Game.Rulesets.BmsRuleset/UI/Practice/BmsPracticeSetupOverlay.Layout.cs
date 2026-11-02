using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Containers;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Practice;

public partial class BmsPracticeSetupOverlay
{
    private const float outer_padding = 24;
    private const float panel_padding = 20;
    private const float column_spacing = 24;
    private const float section_spacing = 20;
    private const float control_spacing = 12;
    private const float controls_width = 292;
    private const float settings_width = controls_width + panel_padding * 2;
    private const float scrollbar_gutter = OsuScrollContainer.SCROLL_BAR_WIDTH + OsuScrollContainer.SCROLL_BAR_PADDING * 2;

    protected override void Update()
    {
        base.Update();

        panel.Width = Math.Min(1280, DrawWidth * 0.94f);
        var width = panel.ChildSize.X;
        var sideBySide = width >= 900;
        var threeColumns = width >= 1180;

        var scrollViewControls = sideBySide ? !threeColumns : panel.ChildSize.Y < 600;
        var viewParent = scrollViewControls ? settingsContent : (Container<Drawable>)panel;
        if (viewPanel.Parent != viewParent)
        {
            ((Container<Drawable>)viewPanel.Parent!).Remove(viewPanel, false);
            viewParent.Add(viewPanel);
            if (viewParent == settingsContent)
                settingsContent.SetLayoutPosition(viewPanel, -1);
        }

        viewPanel.Position = viewParent == settingsContent ? Vector2.Zero : new Vector2(outer_padding);
        settingsPanel.Width = sideBySide ? settings_width : width - outer_padding * 2;
        var controlWidth = Math.Max(1, settingsPanel.Width - panel_padding * 2);
        viewPanel.Width = scrollViewControls ? controlWidth : sideBySide ? controls_width : width - outer_padding * 2;
        heading.MaxWidth = subtitle.MaxWidth = Math.Max(1, viewPanel.Width);

        toolbar.Direction = sideBySide ? FillDirection.Vertical : FillDirection.Full;
        var viewColumns = sideBySide ? 1 : viewPanel.Width >= 600 ? 3 : 2;
        var viewControlWidth = (viewPanel.Width - control_spacing * (viewColumns - 1)) / viewColumns;
        viewControls.Width = fitButtons.Width = snapRow.Width = viewControlWidth;
        zoomInput.Width = Math.Max(1, viewControlWidth - 84);
        foreach (var fitButton in fitButtons.Children)
            fitButton.Width = (viewControlWidth - fitButtons.Spacing.X) / 2;

        settingsPanel.X = sideBySide ? width - outer_padding - settings_width : outer_padding;

        // Keep the scrollbar in its own gutter so overflowing fields and fixed actions share both edges.
        settingsScroll.Width = controlWidth + scrollbar_gutter;
        settingsContent.Width = controlWidth;
        rangeInputs.Direction = repeatInputs.Direction = sideBySide ? FillDirection.Vertical : FillDirection.Full;
        startTimeRow.Width = endTimeRow.Width = sideBySide ? controlWidth : (controlWidth - rangeInputs.Spacing.X) / 2;
        startBoundaryButton.Width = startTimeRow.Width - 44;
        endBoundaryButton.Width = endTimeRow.Width - 44;
        skipResultsInput.Width = sideBySide ? controlWidth : (controlWidth - repeatInputs.Spacing.X) * 0.4f;
        countInGroup.Width = sideBySide ? controlWidth : controlWidth - skipResultsInput.Width - repeatInputs.Spacing.X;
        countInGroup.Padding = new MarginPadding { Top = sideBySide ? 16 : 0 };
        foreach (var countInButton in countInButtons)
            countInButton.Width = (countInGroup.Width - countInChoices.Spacing.X * (countInButtons.Length - 1)) / countInButtons.Length;

        startButton.Width = controlWidth;
        summary.MaxWidth = status.MaxWidth = controlWidth;
    }

    private void updateContentLayout()
    {
        var width = panel.ChildSize.X;
        var height = panel.ChildSize.Y;
        var sideBySide = width >= 900;
        var threeColumns = width >= 1180;

        if (sideBySide)
        {
            settingsPanel.Y = outer_padding;
            settingsPanel.Height = Math.Max(1, height - outer_padding * 2);
            chartArea.Padding = new MarginPadding
            {
                Left = threeColumns ? outer_padding + controls_width + column_spacing : outer_padding,
                Right = outer_padding + settings_width + column_spacing,
                Top = outer_padding,
                Bottom = outer_padding,
            };
        }
        else
        {
            // High UI scales leave little height; browsing controls can share the settings scroll.
            var viewHeight = viewPanel.Parent == settingsContent ? 0 : viewPanel.DrawHeight + column_spacing;
            var minimumChartHeight = Math.Clamp(height * 0.18f, 120, 160);
            var maximumSettingsHeight = Math.Max(startButton.DrawHeight + panel_padding * 2 + 64,
                height - outer_padding * 2 - viewHeight - column_spacing - minimumChartHeight);
            var preferredSettingsHeight = settingsContent.DrawHeight + startButton.DrawHeight + section_spacing + panel_padding * 2;
            settingsPanel.Height = Math.Min(preferredSettingsHeight, maximumSettingsHeight);
            settingsPanel.Y = height - outer_padding - settingsPanel.Height;
            chartArea.Padding = new MarginPadding
            {
                Horizontal = outer_padding,
                Top = outer_padding + viewHeight,
                Bottom = outer_padding + settingsPanel.Height + column_spacing,
            };
        }

        settingsScroll.Height = Math.Max(1, settingsPanel.Height - panel_padding * 2 - startButton.DrawHeight - section_spacing);
        chart.Width = Math.Min(520, chartArea.ChildSize.X);
    }

    private partial class PracticeSettingsScroll(Action updateContentLayout) : OsuScrollContainer
    {
        private double previousExtent;

        protected override void UpdateAfterChildren()
        {
            // Measure and position the panel using the same frame as its collapsing fields.
            updateContentLayout();
            base.UpdateAfterChildren();
        }

        protected override void ApplyCurrentToContent()
        {
            // Shrinking content already animates; a second scroll rebound would reverse its visible motion.
            if (ScrollableExtent < previousExtent && Current > ScrollableExtent)
                ScrollTo(ScrollableExtent, false);
            previousExtent = ScrollableExtent;
            base.ApplyCurrentToContent();
        }
    }
}
