using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public sealed partial class DifficultyTableAutocomplete : CompositeDrawable
{
    public Action<string>? OnImport { get; init; }

    /// <summary>
    /// Fired when the user clicks the delete button on a history item in the dropdown.
    /// </summary>
    public Action<ImportOption>? OnHistoryDelete { get; init; }

    public new MarginPadding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    private readonly FocusedTextBox textBox;
    private readonly DropdownContainer dropdown;

    private List<ImportOption> allPresets = [];
    private List<ImportOption> allHistory = [];
    private bool hasFocus;
    private bool suppressAutoHide;

    public DifficultyTableAutocomplete()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        textBox = new FocusedTextBox
        {
            RelativeSizeAxes = Axes.X,
            Height = 35,
            PlaceholderText = BmsStrings.DifficultyTablePlaceholder,
        };

        dropdown = new DropdownContainer();
        dropdown.ItemSelected = item =>
        {
            textBox.Current.Value = item.Url;
            dropdown.Hide();
        };
        dropdown.ItemDeleteRequested = item => OnHistoryDelete?.Invoke(item);

        var importButton = new RoundedButton
        {
            Text = BmsStrings.Import,
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Action = () => OnImport?.Invoke(textBox.Current.Value),
        };

        InternalChildren =
        [
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children =
                [
                    textBox,
                    dropdown,
                    importButton,
                ],
            },
        ];

        textBox.OnCommit += (_, _) => OnImport?.Invoke(textBox.Current.Value);
        textBox.Current.BindValueChanged(_ => updateFilter());
    }

    /// <summary>
    /// Set the preset and history items shown in the dropdown.
    /// </summary>
    public void SetItems(IEnumerable<ImportOption> presets, IEnumerable<ImportOption> history)
    {
        allPresets = presets.ToList();
        allHistory = history.ToList();

        if (hasFocus)
            updateFilter();
    }

    /// <summary>
    /// Force the dropdown to reapply the current filter (e.g. after an item deletion)
    /// without waiting for a focus change.
    /// </summary>
    public void RefreshFilter() => updateFilter();

    /// <summary>
    /// Prevents the dropdown from auto-hiding on the next focus-loss detection,
    /// so a delete-button click (which steals focus from the textbox) doesn't
    /// collapse the dropdown before the item list can be rebuilt.
    /// </summary>
    public void SuppressAutoHide()
    {
        suppressAutoHide = true;
    }

    protected override void Update()
    {
        base.Update();

        var focused = textBox.HasFocus;
        if (focused != hasFocus)
        {
            hasFocus = focused;
            if (hasFocus)
                // User refocused the textbox — back to normal auto-hide behaviour.
                suppressAutoHide = false;
            updateFilter();
        }
    }

    private void updateFilter()
    {
        var filter = textBox.Current.Value?.Trim().ToLowerInvariant() ?? string.Empty;
        var hasFilter = !string.IsNullOrEmpty(filter);

        Func<ImportOption, bool> match = hasFilter
            ? i => i.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   i.Url.Contains(filter, StringComparison.OrdinalIgnoreCase)
            : _ => true;

        var matchedPresets = allPresets.Where(match).ToList();
        var matchedHistory = allHistory.Where(match).ToList();

        if ((hasFocus || suppressAutoHide) && (matchedPresets.Count > 0 || matchedHistory.Count > 0))
            dropdown.Show(matchedPresets, matchedHistory);
        else
        {
            dropdown.Hide();
            suppressAutoHide = false;
        }
    }

    private sealed partial class DropdownContainer : CompositeDrawable
    {
        public Action<ImportOption>? ItemSelected { get; set; }

        public Action<ImportOption>? ItemDeleteRequested { get; set; }

        private readonly FillFlowContainer list;

        public DropdownContainer()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = list = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
            };
        }

        public void Show(List<ImportOption> presets, List<ImportOption> history)
        {
            list.Clear();

            if (presets.Count > 0)
            {
                list.Add(new SectionHeader(BmsStrings.Presets));
                foreach (var item in presets)
                    list.Add(new DropdownItemRow(item, () => ItemSelected?.Invoke(item)));
            }

            if (history.Count > 0)
            {
                list.Add(new SectionHeader(BmsStrings.History));
                foreach (var item in history)
                {
                    var captured = item;
                    list.Add(new DropdownItemRow(item, () => ItemSelected?.Invoke(captured),
                        showDelete: true, onDelete: () => ItemDeleteRequested?.Invoke(captured)));
                }
            }

            this.FadeIn();
        }

        public new void Hide()
        {
            list.Clear();
            this.FadeOut();
        }
    }

    private sealed partial class SectionHeader : OsuSpriteText
    {
        public SectionHeader(LocalisableString text)
        {
            Text = text;
            Font = OsuFont.Default.With(size: 10, weight: FontWeight.Bold);
            Colour = Color4.Gray;
            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };
            RelativeSizeAxes = Axes.X;
        }
    }

    private partial class DropdownItemRow : Container
    {

        public override bool HandlePositionalInput => true;

        public sealed override Axes RelativeSizeAxes
        {
            get => base.RelativeSizeAxes;
            set => base.RelativeSizeAxes = value;
        }

        private readonly Action onClick;
        private readonly Box background;

        public DropdownItemRow(ImportOption item, Action onClick, bool showDelete = false, Action? onDelete = null)
        {
            this.onClick = onClick;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };

            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Transparent,
            };

            var content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(2),
                Padding = new MarginPadding { Right = 24 },
                Children =
                [
                    new OsuSpriteText
                    {
                        Text = item.Display,
                        Font = OsuFont.Default.With(size: 14),
                        Colour = Color4.White,
                    },
                    new TruncatingSpriteText
                    {
                        Text = item.Url,
                        Font = OsuFont.Default.With(size: 11),
                        Colour = Color4.Gray,
                    },
                ],
            };

            InternalChildren = showDelete && onDelete != null
                ? new Drawable[] { background, content, new DeleteButton(onDelete) }
                : new Drawable[] { background, content };
        }

        protected override bool OnClick(ClickEvent e)
        {
            onClick();
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(Color4.DimGray, 100);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(Color4.Transparent, 100);
        }
    }

    private sealed partial class DeleteButton : Container
    {

        public override bool HandlePositionalInput => true;

        private readonly Action onDelete;
        private readonly SpriteText text;

        public DeleteButton(Action onDelete)
        {
            this.onDelete = onDelete;

            Anchor = Anchor.CentreRight;
            Origin = Anchor.CentreRight;
            Width = 28;
            Height = 28;

            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Transparent,
                },
                text = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "×",
                    Font = OsuFont.Default.With(size: 18),
                    Colour = Color4.Gray,
                },
            ];
        }

        protected override bool OnClick(ClickEvent e)
        {
            onDelete();
            return true; // stop propagation — don't select the item
        }

        protected override bool OnHover(HoverEvent e)
        {
            text.FadeColour(Color4.White, 100);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            text.FadeColour(Color4.Gray, 100);
        }
    }
}
