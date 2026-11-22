using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultTimingFilterButton : IconButton
{
    internal bool Selected { get; private set; } = true;

    internal BmsResultTimingFilterButton(bool release, Action selectionChanged)
    {
        Name = release ? "Release timing filter" : "Press timing filter";
        Size = new Vector2(26);
        Icon = release ? FontAwesome.Solid.HandPaper : FontAwesome.Solid.HandPointer;
        IconScale = new Vector2(0.75f);
        TooltipText = release ? BmsStrings.ResultTimingRelease : BmsStrings.ResultTimingPress;
        Action = () =>
        {
            Selected = !Selected;
            IconColour = Selected ? Color4.White : Color4.White.Opacity(0.35f);
            selectionChanged();
        };
    }
}

internal partial class BmsResultTimingFilter : FillFlowContainer<BmsResultTimingFilterButton>
{
    private readonly BmsResultTimingFilterButton press;
    private readonly BmsResultTimingFilterButton release;

    internal bool ShowPresses => press.Selected;
    internal bool ShowReleases => release.Selected;

    internal BmsResultTimingFilter(Action selectionChanged)
    {
        Anchor = Origin = Anchor.CentreRight;
        AutoSizeAxes = Axes.Both;
        Direction = FillDirection.Horizontal;
        Spacing = new Vector2(2, 0);
        Children =
        [
            press = new BmsResultTimingFilterButton(false, selectionChanged),
            release = new BmsResultTimingFilterButton(true, selectionChanged),
        ];
    }
}