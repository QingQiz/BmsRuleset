using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings.Sections.Maintenance;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Settings.Components;

internal sealed partial class DifficultyTableRowContainer : Container
{
    private readonly Action<DT>? onDelete;
    private readonly Action<DT>? onUpdate;

    [Resolved(CanBeNull = true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved(CanBeNull = true)]
    private RealmAccess? realm { get; set; }

    public DifficultyTableRowContainer(DT table,
                                       CollectionSyncManager? syncManager, bool isSubdivided,
                                       bool isExpanded, Action<bool> onExpandedChanged,
                                       Action<DT>? onDelete = null,
                                       Action<DT>? onUpdate = null)
    {
        this.onDelete = onDelete;
        this.onUpdate = onUpdate;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 5;
        CornerExponent = FormControlBackground.CORNER_EXPONENT;

        var buttons = createButtons(
            table, isSubdivided,
            () => confirmSubdivide(table, syncManager, isSubdivided),
            () => confirmUpdate(table),
            () => confirmDelete(table));
        var expanded = new BindableBool(isExpanded);
        expanded.BindValueChanged(value => onExpandedChanged(value.NewValue));

        Children =
        [
            new FormControlBackground
            {
                RelativeSizeAxes = Axes.Both,
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children =
                [
                    new TableRowHeader(table, expanded),
                    new TableRowActions(buttons, expanded),
                ],
            },
        ];
    }

    private static Drawable[] createButtons(
        DT table, bool isSubdivided,
        Action confirmSubdivide, Action confirmUpdate, Action confirmDelete)
    {
        var buttons = new List<Drawable>
        {
            new RoundedButton
            {
                Text = isSubdivided ? BmsStrings.Unsubdivide : BmsStrings.Subdivide,
                TooltipText = isSubdivided
                    ? BmsStrings.UnsubdivideTooltip
                    : BmsStrings.SubdivideTooltip,
                RelativeSizeAxes = Axes.X,
                Height = 32,
                Action = confirmSubdivide,
            },
        };

        if (table.Source == TableSource.RemoteUrl)
        {
            buttons.Add(new RoundedButton
            {
                Text = BmsStrings.Update,
                TooltipText = BmsStrings.UpdateTableTooltip,
                RelativeSizeAxes = Axes.X,
                Height = 32,
                Action = confirmUpdate,
            });
        }

        buttons.Add(new DangerousRoundedButton
        {
            Text = BmsStrings.DeleteTableTooltip,
            TooltipText = BmsStrings.DeleteTableTooltip,
            RelativeSizeAxes = Axes.X,
            Height = 32,
            Action = confirmDelete,
        });

        return [.. buttons];
    }

    private void confirmDelete(DT table)
    {
        if (dialogOverlay != null)
            dialogOverlay.Push(new MassDeleteConfirmationDialog(
                () => Task.Run(() => onDelete?.Invoke(table)),
                BmsStrings.DeleteTableConfirmation(table.Name, table.Entries.Count)));
        else
            onDelete?.Invoke(table);
    }

    private void confirmUpdate(DT table)
    {
        if (dialogOverlay != null)
            dialogOverlay.Push(new MassDeleteConfirmationDialog(
                () => onUpdate?.Invoke(table),
                BmsStrings.UpdateTableConfirmation(table.Name)));
        else
            onUpdate?.Invoke(table);
    }

    private void confirmSubdivide(DT table,
                                  CollectionSyncManager? syncManager, bool isSubdivided)
    {
        if (dialogOverlay != null)
            dialogOverlay.Push(new MassDeleteConfirmationDialog(
                () => syncManager?.ToggleSubdivide(realm, table),
                isSubdivided
                    ? BmsStrings.MergeTableConfirmation(table.Name)
                    : BmsStrings.SplitTableConfirmation(table.Name)));
        else
            syncManager?.ToggleSubdivide(realm, table);
    }

    private sealed partial class TableRowHeader : OsuClickableContainer
    {
        public TableRowHeader(DT table, BindableBool expanded)
        {
            var chevron = new SpriteIcon
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.Centre,
                Position = new Vector2(-17, 0),
                Size = new Vector2(12),
                Icon = FontAwesome.Solid.ChevronRight,
            };

            Name = $"Difficulty table header ({table.Name})";
            TooltipText = buildTooltip(table);
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Action = expanded.Toggle;
            Children =
            [
                new TextFlowContainer(text => text.Font = OsuFont.Default.With(size: 16, weight: FontWeight.Bold))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = 12, Right = 40, Vertical = 11 },
                    Text = BmsStrings.DifficultyTableName(table.Name, table.Symbol),
                },
                chevron,
            ];

            expanded.BindValueChanged(value =>
                chevron.RotateTo(value.NewValue ? 90 : 0, 200, Easing.OutQuint), true);
        }

        private static LocalisableString buildTooltip(DT table)
        {
            var counts = new Dictionary<string, int>();

            foreach (var e in table.Entries)
            {
                counts.TryGetValue(e.Level, out var c);
                counts[e.Level] = c + 1;
            }

            var ordered = new List<string>();

            foreach (var lv in table.LevelOrder)
            {
                if (counts.TryGetValue(lv, out var c))
                {
                    ordered.Add($"{lv}: {c}");
                    counts.Remove(lv);
                }
            }

            foreach (var kv in counts.OrderBy(kv => kv.Key))
                ordered.Add($"{kv.Key}: {kv.Value}");

            return BmsStrings.DifficultyTableTooltip(table.Entries.Count, string.Join(", ", ordered));
        }
    }

    private partial class TableRowActions : Container
    {
        private const float transition_duration = 200;

        private readonly FillFlowContainer content;
        private readonly Box separator;

        public TableRowActions(Drawable[] buttons, BindableBool expanded)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            AutoSizeDuration = transition_duration;
            AutoSizeEasing = Easing.OutQuint;
            Masking = true;

            var buttonCells = buttons.Select(button => (Drawable)new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = 3 },
                Child = button,
            }).ToArray();

            InternalChild = content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children =
                [
                    separator = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 44,
                        Padding = new MarginPadding { Horizontal = 7, Vertical = 6 },
                        ColumnDimensions = buttons.Select(_ => new Dimension()).ToArray(),
                        RowDimensions = [new Dimension(GridSizeMode.Relative, 1)],
                        Content = new[] { buttonCells },
                    },
                ],
            };

            expanded.BindValueChanged(updateExpandedState, true);
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            separator.Colour = colourProvider.Background2;
        }

        private void updateExpandedState(ValueChangedEvent<bool> expanded)
        {
            ClearTransforms(true);

            if (expanded.NewValue)
            {
                AutoSizeAxes = Axes.Y;
                content.FadeIn(transition_duration, Easing.OutQuint);
            }
            else
            {
                AutoSizeAxes = Axes.None;
                this.ResizeHeightTo(0, transition_duration, Easing.OutQuint);
                content.FadeOut(transition_duration, Easing.OutQuint);
            }
        }
    }
}
