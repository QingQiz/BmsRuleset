using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal readonly record struct BmsResultLane(int KeyNumber)
{
    internal bool IsScratch => KeyNumber == 0;

    internal LocalisableString Label => IsScratch ? BmsStrings.Scratch : BmsStrings.Key(KeyNumber);

    internal static BmsResultLane[] ForBeatmap(IBeatmap beatmap, IEnumerable<int?> columns)
    {
        var variant = beatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
        var count = beatmap is BmsBeatmap { TotalColumns: > 0 } withColumns
            ? withColumns.TotalColumns
            : Math.Max(BmsLayout.GetTotalColumns(variant), (columns.Max() ?? -1) + 1);

        var lanes = new BmsResultLane[count];
        var key = 0;
        for (var column = 0; column < count; column++)
            lanes[column] = new BmsResultLane(BmsLayout.IsScratchColumn(column, variant) ? 0 : ++key);

        return lanes;
    }

    internal static BmsResultLane[] Combine(IEnumerable<IReadOnlyList<BmsResultLane>> layouts)
    {
        var lanes = new List<BmsResultLane>();
        foreach (var layout in layouts)
            for (var column = lanes.Count; column < layout.Count; column++)
                lanes.Add(layout[column]);

        return lanes.ToArray();
    }
}

internal sealed partial class BmsResultLaneFilter : CompositeDrawable
{
    private const float button_size = 23;
    private const float group_gap = 5;
    private readonly Action selectionChanged;
    private readonly OsuAnimatedButton selectAll;
    private readonly OsuScrollContainer scroll;
    private readonly FillFlowContainer<OsuAnimatedButton> buttons;
    private IReadOnlyList<BmsResultLane> lanes = [];
    private readonly HashSet<int> hidden = [];

    internal bool AllSelected => hidden.Count == 0;

    internal LocalisableString SelectionLabel
    {
        get
        {
            if (AllSelected)
                return BmsStrings.Overall;

            var selectedCount = lanes.Count - hidden.Count;
            return selectedCount == 1
                ? lanes[Enumerable.Range(0, lanes.Count).First(column => !hidden.Contains(column))].Label
                : BmsStrings.ResultSelectedLaneCount(selectedCount);
        }
    }

    internal BmsResultLaneFilter(IReadOnlyList<BmsResultLane> lanes, Action selectionChanged)
    {
        this.selectionChanged = selectionChanged;
        RelativeSizeAxes = Axes.X;
        Height = button_size + 4;
        InternalChildren =
        [
            scroll = new OsuScrollContainer(Direction.Horizontal)
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                RelativeSizeAxes = Axes.X,
                Height = Height,
                Padding = new MarginPadding { Right = button_size + group_gap },
                ScrollbarVisible = false,
                ClampExtension = 0,
                Child = buttons = new FillFlowContainer<OsuAnimatedButton>
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(1, 0),
                },
            },
            selectAll = new IconButton
            {
                Name = "Select all lanes",
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Size = new Vector2(button_size),
                Icon = FontAwesome.Solid.ThLarge,
                IconScale = new Vector2(0.7f),
                TooltipText = BmsStrings.ResultClearLanes,
                Action = () =>
                {
                    if (AllSelected)
                        hidden.UnionWith(Enumerable.Range(0, this.lanes.Count));
                    else
                        hidden.Clear();
                    applySelection();
                    selectionChanged();
                },
            },
        ];
        scroll.ScrollContent.Anchor = scroll.ScrollContent.Origin = Anchor.TopRight;
        SetLanes(lanes);
    }

    // Older timing events without a recorded lane are attributable only to the overall view.
    internal bool Includes(int? column) => AllSelected || (column is { } lane && !hidden.Contains(lane));

    internal void SetLanes(IReadOnlyList<BmsResultLane> layout)
    {
        if (lanes.SequenceEqual(layout))
            return;

        lanes = layout;
        hidden.Clear();
        buttons.Children = lanes.Select((lane, column) => new OsuAnimatedButton
        {
            Name = $"Lane {column}",
            Size = new Vector2(button_size),
            Action = () => toggle(column),
            Child = lane.IsScratch
                ? new SpriteIcon { Icon = BmsIcons.Scratch, Size = new Vector2(14), Anchor = Anchor.Centre, Origin = Anchor.Centre }
                : new OsuSpriteText
                {
                    Text = BmsStrings.ResultNumber(lane.KeyNumber),
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                },
        }).ToArray();
        scroll.ScrollToStart(false);
        applySelection();
    }

    private void toggle(int column)
    {
        if (!hidden.Add(column))
            hidden.Remove(column);

        applySelection();
        selectionChanged();
    }

    private void applySelection()
    {
        selectAll.Alpha = AllSelected ? 1 : 0.45f;
        selectAll.TooltipText = AllSelected ? BmsStrings.ResultClearLanes : BmsStrings.ResultAllLanes;
        for (var column = 0; column < buttons.Count; column++)
        {
            var shown = Includes(column);
            buttons[column].Alpha = shown ? 1 : 0.45f;
            buttons[column].TooltipText = shown ? BmsStrings.ResultHideFilter(lanes[column].Label) : BmsStrings.ResultShowFilter(lanes[column].Label);
        }
    }
}
