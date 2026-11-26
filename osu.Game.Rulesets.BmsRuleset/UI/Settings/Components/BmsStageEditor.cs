using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Screens.Edit;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;

internal sealed partial class BmsStageEditor : FillFlowContainer, IHasCurrentValue<Dictionary<int, BmsStageStyle>>
{
    private readonly BindableWithCurrent<Dictionary<int, BmsStageStyle>> current = new();

    public Bindable<Dictionary<int, BmsStageStyle>> Current
    {
        get => current.Current;
        set => current.Current = value;
    }

    private BmsStageHud stage = null!;
    private BmsLayoutVariant layout => stage.LayoutVariant;
    private readonly Bindable<int> column = new();
    private readonly Bindable<int> gapBoundary = new();
    private readonly Bindable<bool> showLaneImages = new();
    private FillFlowContainer laneSettings = null!;
    private bool writingStyle;
    private FillFlowContainer gapSettings = null!;

    [Resolved]
    private SkinManager skins { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    private IEditorChangeHandler? changeHandler { get; set; }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, SettingsSection.ITEM_SPACING);
        buildSections();

        column.BindValueChanged(_ => rebuildLaneFields());
        showLaneImages.BindValueChanged(_ => rebuildLaneFields());
        gapBoundary.BindValueChanged(_ => rebuildBoundaryFields());
        current.BindValueChanged(_ =>
        {
            // Undo/redo can reuse the selected HUD. Own edits must not replace the control being dragged or opened.
            if (!writingStyle)
                Scheduler.AddOnce(buildSections);
        });
    }

    internal void SetStage(BmsStageHud value) => stage = value;

    private void buildSections()
    {
        Clear();
        var count = BmsLayout.GetTotalColumns(layout);
        var style = stage.GetStageStyle(layout);
        var files = imageFiles();

        Add(new StageSubsection(BmsStrings.StageGlobalSettings)
        {
            Children =
            [
                new SettingsCheckbox
                {
                    LabelText = BmsStrings.StageKeepTotalWidth,
                    TooltipText = BmsStrings.StageKeepTotalWidthDescription,
                    Current = settingValue(true, style.KeepTotalWidth, (currentStyle, value) => currentStyle.KeepTotalWidth = value ?? true),
                },
                boundSlider(BmsStrings.NoteHeightScale, BmsStrings.NoteHeightScaleDescription, stage.NoteHeightScale),
            ],
        });

        Add(new StageSubsection(BmsStrings.StageColumnSettings)
        {
            Children =
            [
                new InlineTabs<int>(columnLabel, i => BmsStrings.StageColumn(columnLabel(i)))
                {
                    Items = Enumerable.Range(0, count).ToArray(),
                    Current = column,
                },
                new InlineTabs<bool>(images => images ? BmsStrings.StageLaneImages : BmsStrings.StageLaneGeometry)
                {
                    Items = [false, true],
                    Current = showLaneImages,
                },
                laneSettings = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, SettingsSection.ITEM_SPACING_V2),
                },
            ],
        });

        if (count > 1)
        {
            var baseSeparatorColour = (Colour4)(baseConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour)?.Value ?? Color4.White);
            var separatorColour = settingValue(baseSeparatorColour, style.GetSeparatorColour(), (currentStyle, value) => currentStyle.SeparatorColour = value?.ToHex(true));
            Add(new StageSubsection(BmsStrings.StageBoundarySettings)
            {
                Children =
                [
                    new SettingsColour { LabelText = BmsStrings.StageSeparatorColour, Current = separatorColour },
                    new InlineTabs<int>(i => BmsStrings.StageGapNumber(columnLabel(i), columnLabel(i + 1)), i => BmsStrings.StageGapBoundary(columnLabel(i), columnLabel(i + 1)))
                    {
                        Items = Enumerable.Range(0, count - 1).ToArray(),
                        Current = gapBoundary,
                    },
                    gapSettings = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, SettingsSection.ITEM_SPACING_V2),
                    },
                ],
            });
            rebuildBoundaryFields();
        }

        var baseColour = (Colour4)(baseConfig<Color4>(LegacyManiaSkinConfigurationLookups.BarLineColour)?.Value ?? BmsStage.DEFAULT_BAR_LINE_COLOUR);
        var colour = settingValue(baseColour, style.GetBarColour(), (currentStyle, value) => currentStyle.BarColour = value?.ToHex(true));
        var showLines = settingValue(true, style.ShowMeasureLines, (currentStyle, value) => currentStyle.ShowMeasureLines = value);
        var baseBarHeight = baseConfig<float>(LegacyManiaSkinConfigurationLookups.BarLineHeight)?.Value ?? BmsStage.DEFAULT_BAR_LINE_HEIGHT;

        AddRange([
            new StageSubsection(BmsStrings.StageJudgementLineSettings)
            {
                Children =
                [
                    imageFileSelector(BmsStageImage.HitTarget, null, style, files, BmsStrings.StageHitTarget),
                    slider(BmsStrings.StageHitTargetHeight, 1, style.HitTargetHeightScale, 0.1f, BmsStageStyle.MAX_HIT_TARGET_HEIGHT_SCALE,
                        (currentStyle, value) => currentStyle.HitTargetHeightScale = value),
                    boundSlider(BmsStrings.JudgementLineOffset, BmsStrings.JudgementLineOffsetDescription, stage.JudgementLineOffset),
                    boundSlider(BmsStrings.LightPositionOffset, BmsStrings.LightPositionOffsetDescription, stage.LightPositionOffset),
                ],
            },
            new StageSubsection(BmsStrings.StageMeasureLineSettings)
            {
                Children =
                [
                    new SettingsCheckbox { LabelText = BmsStrings.StageShowMeasureLines, Current = showLines },
                    slider(BmsStrings.StageBarHeight, baseBarHeight, style.BarHeight, 0, BmsStageStyle.MAX_LINE_WIDTH,
                        (currentStyle, value) => currentStyle.BarHeight = value),
                    new SettingsColour { LabelText = BmsStrings.StageBarColour, Current = colour },
                ],
            },
            new StageSubsection(BmsStrings.StageBorderSettings)
            {
                Children =
                [
                    imageFileSelector(BmsStageImage.LeftBorder, null, style, files, BmsStrings.StageLeftBorder),
                    imageFileSelector(BmsStageImage.RightBorder, null, style, files, BmsStrings.StageRightBorder),
                    imageFileSelector(BmsStageImage.BottomBorder, null, style, files, BmsStrings.StageBottomBorder),
                ],
            },
        ]);

        rebuildLaneFields();
    }

    private SettingsSlider<float> boundSlider(LocalisableString label, LocalisableString tooltip, BindableFloat value)
    {
        var control = new SettingsSlider<float>
        {
            LabelText = label,
            TooltipText = tooltip,
            Current = value,
            KeyboardStep = value.Precision,
        };
        // The toolbox listens to top-level settings only; these sliders still edit the original HUD bindables.
        if (changeHandler != null)
            control.SettingChanged += changeHandler.SaveState;
        return control;
    }

    private void rebuildLaneFields()
    {
        var style = stage.GetStageStyle(layout);
        var count = BmsLayout.GetTotalColumns(layout);
        var index = columnAt(column.Value);
        laneSettings.Clear();

        if (showLaneImages.Value)
        {
            var files = imageFiles();
            foreach (var image in Enum.GetValues<BmsStageImage>().Where(image => !image.IsStageWide()))
            {
                var row = imageFileSelector(image, index, style, files, imageLabel(image));
                // Open file menus must draw in front of the image rows below them.
                row.Depth = laneSettings.Count;
                laneSettings.Insert(laneSettings.Count, row);
            }

            return;
        }

        var background = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layout, index);
        var baseWidth = baseConfig<float>(LegacyManiaSkinConfigurationLookups.ColumnWidth, background)?.Value ?? BmsStageColumnLayout.DefaultWidth(index, layout);
        var note = new BmsSkinComponentLookup(BmsSkinComponents.Note, layout, index);
        var noteReference = baseConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, note)?.Value;
        var width = laneSlider(BmsStrings.StageColumnWidth, baseWidth, 1, BmsStageStyle.MAX_COLUMN_WIDTH, s => s.Widths);
        var noteWidth = laneSlider(BmsStrings.StageNoteWidthForHeightScale, noteReference ?? width.Current.Value, 1, BmsStageStyle.MAX_COLUMN_WIDTH,
            s => s.NoteWidthForHeightScales);

        if (noteReference == null)
        {
            // Without a fixed reference in the skin, notes scale with the current lane width.
            width.Current.BindValueChanged(change =>
            {
                var noteValue = noteWidth.Current;
                noteValue.Default = change.NewValue;
                if (!stage.GetStageStyle(layout).NoteWidthForHeightScales.ContainsKey(index))
                    noteValue.SetDefault();
            });
        }

        laneSettings.AddRange([
            batchSlider(width, s => s.Widths, count, BmsStrings.StageApplyToAllColumns),
            batchSlider(noteWidth, s => s.NoteWidthForHeightScales, count, BmsStrings.StageApplyToAllColumns),
            lightSlider(BmsStrings.StageHitLightScale, false, s => s.HitLightScales),
            lightSlider(BmsStrings.StageHoldLightScale, true, s => s.HoldLightScales),
        ]);

        Drawable lightSlider(LocalisableString label, bool hold, Func<BmsStageStyle, Dictionary<int, float>> values)
        {
            var key = hold ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale;
            var explosion = new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, layout, index, hold);
            var control = laneSlider(label, baseConfig<float>(key, explosion)?.Value ?? 1, 0, BmsStageStyle.MAX_LIGHT_SCALE, values);
            return batchSlider(control, values, count, BmsStrings.StageApplyToAllColumns);
        }

        SettingsSlider<float> laneSlider(LocalisableString label, float defaultValue, float min, float max, Func<BmsStageStyle, Dictionary<int, float>> values) =>
            indexedSlider(label, defaultValue, min, max, index, values);
    }

    private IBindable<T>? baseConfig<T>(LegacyManiaSkinConfigurationLookups key, BmsSkinComponentLookup? component = null) where T : notnull =>
        stage.GameplaySkin.GetBaseConfig<T>(new BmsSkinConfigurationLookup(key, component));

    private LocalisableString columnLabel(int visualIndex)
    {
        var index = columnAt(visualIndex);
        if (BmsLayout.IsScratchColumn(index, layout))
            return BmsStrings.StageScratchColumn;

        var keysPerPlayer = BmsLayout.Is2P(layout) ? BmsLayout.GetManiaKeyCount(layout) / 2 : BmsLayout.GetManiaKeyCount(layout);
        var key = BmsLayout.MapToManiaColumn(index, layout) % keysPerPlayer + 1;
        return BmsStrings.StageColumnNumber(key);
    }

    private int columnAt(int visualIndex) => BmsStageColumnLayout.ColumnAtVisualIndex(layout, visualIndex, BmsLayout.GetTotalColumns(layout));

    private void rebuildBoundaryFields()
    {
        var index = gapBoundary.Value;
        var skin = stage.GameplaySkin;
        var left = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layout, columnAt(index));
        var right = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layout, columnAt(index + 1));
        var baseGap = BmsStageColumnLayout.GetLegacyGap(skin, left, right);
        var count = BmsLayout.GetTotalColumns(layout) - 1;
        var gap = indexedSlider(BmsStrings.StageGap, baseGap, 0, BmsStageStyle.MAX_GAP, index, s => s.Gaps);
        var separator = indexedSlider(BmsStrings.StageSeparator, defaultSeparatorWidth(index), 0, BmsStageStyle.MAX_LINE_WIDTH, index, s => s.SeparatorWidths);

        gapSettings.Children =
        [
            batchSlider(gap, s => s.Gaps, count, BmsStrings.StageApplyToAllGaps),
            batchSlider(separator, s => s.SeparatorWidths, count, BmsStrings.StageApplyToAllSeparators, defaultSeparatorWidth),
        ];

        float defaultSeparatorWidth(int boundary) => BmsStageColumnLayout.GetLegacyLineWidth(skin,
            new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layout, columnAt(boundary + 1)), true);
    }

    private SettingsSlider<float> indexedSlider(LocalisableString label, float defaultValue, float min, float max, int index,
                                                Func<BmsStageStyle, Dictionary<int, float>> values)
    {
        var entries = values(stage.GetStageStyle(layout));
        return slider(label, defaultValue, entries.TryGetValue(index, out var value) ? value : null, min, max, (style, number) =>
        {
            if (number is { } actual)
                values(style)[index] = actual;
            else
                values(style).Remove(index);
        });
    }

    private Drawable batchSlider(SettingsSlider<float> control, Func<BmsStageStyle, Dictionary<int, float>> values, int count, LocalisableString label,
                                 Func<int, float>? defaultValue = null)
    {
        if (count == 1)
            return control;

        return batchRow(control, label, () => updateStyle(style =>
        {
            var entries = values(style);
            var value = control.Current.Value;
            for (var i = 0; i < count; i++)
            {
                if (defaultValue?.Invoke(i) == value)
                    entries.Remove(i);
                else
                    entries[i] = value;
            }
        }));
    }

    private Drawable imageFileSelector(BmsStageImage slot, int? selectedColumn, BmsStageStyle style, IReadOnlyDictionary<string, string> files, LocalisableString label)
    {
        var imageName = new Bindable<string>(style.GetImage(slot, selectedColumn) ?? string.Empty);
        imageName.BindValueChanged(change => updateStyle(currentStyle => currentStyle.SetImage(slot, selectedColumn, change.NewValue)));
        var original = baseImageName(slot, selectedColumn);
        var defaultLabel = original == null ? BmsStrings.StageUseSkin : BmsStrings.StageUseSkinImage(original);
        var control = new LabelledDropdown<string>(name => string.IsNullOrEmpty(name) ? defaultLabel : files.GetValueOrDefault(name, name))
        {
            LabelText = label,
            AlwaysShowSearchBar = true,
            Items = files.Keys.Prepend(string.Empty).Append(imageName.Value).Distinct(StringComparer.Ordinal),
            Current = imageName,
        };
        var count = BmsLayout.GetTotalColumns(layout);
        if (selectedColumn == null || count == 1)
            return control;

        return batchRow(control, BmsStrings.StageApplyToAllColumns, () => updateStyle(currentStyle =>
        {
            currentStyle.SetImage(slot, null, null);
            for (var i = 0; i < count; i++)
                currentStyle.SetImage(slot, i, null);
            currentStyle.SetImage(slot, null, imageName.Value);
        }));
    }

    private string? baseImageName(BmsStageImage slot, int? selectedColumn)
    {
        var key = slot switch
        {
            BmsStageImage.HitTarget => LegacyManiaSkinConfigurationLookups.HitTargetImage,
            BmsStageImage.LeftBorder => LegacyManiaSkinConfigurationLookups.LeftStageImage,
            BmsStageImage.RightBorder => LegacyManiaSkinConfigurationLookups.RightStageImage,
            BmsStageImage.BottomBorder => LegacyManiaSkinConfigurationLookups.BottomStageImage,
            BmsStageImage.Note => LegacyManiaSkinConfigurationLookups.NoteImage,
            BmsStageImage.HoldHead => LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage,
            BmsStageImage.HoldBody => LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage,
            BmsStageImage.HoldTail => LegacyManiaSkinConfigurationLookups.HoldNoteTailImage,
            BmsStageImage.Mine => LegacyManiaSkinConfigurationLookups.Hit100,
            BmsStageImage.Key => LegacyManiaSkinConfigurationLookups.KeyImage,
            BmsStageImage.KeyDown => LegacyManiaSkinConfigurationLookups.KeyImageDown,
            BmsStageImage.Light => LegacyManiaSkinConfigurationLookups.LightImage,
            BmsStageImage.HitLight => LegacyManiaSkinConfigurationLookups.ExplosionImage,
            BmsStageImage.HoldLight => LegacyManiaSkinConfigurationLookups.HoldNoteLightImage,
            _ => (LegacyManiaSkinConfigurationLookups?)null,
        };

        if (key is not { } lookup)
            return null;

        var component = selectedColumn is int index ? new BmsSkinComponentLookup(BmsSkinComponents.Note, layout, index) : null;
        return baseConfig<string>(lookup, component)?.Value;
    }

    private IReadOnlyDictionary<string, string> imageFiles() => BmsSkinImageFiles.StageChoices(skins, stage.GameplaySkin);

    private static Drawable batchRow(Drawable control, LocalisableString label, Action apply) => new Container
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Children =
        [
            control,
            new IconButton
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Y = 15,
                Size = new Vector2(SettingsPanel.CONTENT_MARGINS),
                IconScale = new Vector2(0.68f),
                Icon = FontAwesome.Regular.Clone,
                TooltipText = label,
                Action = apply,
            },
        ],
    };

    private SettingsSlider<float> slider(LocalisableString caption, float defaultValue, float? value, float min, float max, Action<BmsStageStyle, float?> apply)
    {
        var currentValue = new BindableFloat(defaultValue)
        {
            MinValue = min,
            MaxValue = max,
            Value = value ?? defaultValue,
        };
        bindOverride(currentValue, apply);
        return new SettingsSlider<float>
        {
            LabelText = caption,
            Current = currentValue,
            KeyboardStep = 1,
        };
    }

    private Bindable<T> settingValue<T>(T defaultValue, T? value, Action<BmsStageStyle, T?> apply) where T : struct
    {
        var setting = new Bindable<T>(defaultValue) { Value = value ?? defaultValue };
        bindOverride(setting, apply);
        return setting;
    }

    private void bindOverride<T>(Bindable<T> setting, Action<BmsStageStyle, T?> apply) where T : struct =>
        setting.BindValueChanged(change => updateStyle(style => apply(style, setting.IsDefault ? null : change.NewValue)));

    private void updateStyle(Action<BmsStageStyle> update)
    {
        var style = clone(stage.GetStageStyle(layout));
        update(style);
        var updated = new Dictionary<int, BmsStageStyle>(current.Value) { [(int)layout] = style };
        var previousColumnWidth = style.KeepTotalWidth ? 0 : stage.ColumnLayoutWidth;
        var resizeStage = previousColumnWidth > 0;

        // Undo must capture the new column settings and the resulting handle width together.
        if (resizeStage)
            changeHandler?.BeginChange();

        writingStyle = true;
        try
        {
            current.Value = updated;

            if (resizeStage)
                stage.Width *= stage.ColumnLayoutWidth / previousColumnWidth;
        }
        finally
        {
            writingStyle = false;

            if (resizeStage)
                changeHandler?.EndChange();
        }
    }

    private static BmsStageStyle clone(BmsStageStyle style) => new()
    {
        KeepTotalWidth = style.KeepTotalWidth,
        Widths = new Dictionary<int, float>(style.Widths),
        SeparatorWidths = new Dictionary<int, float>(style.SeparatorWidths),
        SeparatorColour = style.SeparatorColour,
        NoteWidthForHeightScales = new Dictionary<int, float>(style.NoteWidthForHeightScales),
        HitLightScales = new Dictionary<int, float>(style.HitLightScales),
        HoldLightScales = new Dictionary<int, float>(style.HoldLightScales),
        Gaps = new Dictionary<int, float>(style.Gaps),
        Images = new Dictionary<string, string>(style.Images),
        HitTargetHeightScale = style.HitTargetHeightScale,
        BarHeight = style.BarHeight,
        BarColour = style.BarColour,
        ShowMeasureLines = style.ShowMeasureLines,
    };

    private static LocalisableString imageLabel(BmsStageImage slot) => slot switch
    {
        BmsStageImage.ColumnBackground => BmsStrings.StageColumnBackground,
        BmsStageImage.Note => BmsStrings.StageNote,
        BmsStageImage.HoldHead => BmsStrings.StageHoldHead,
        BmsStageImage.HoldBody => BmsStrings.StageHoldBody,
        BmsStageImage.HoldTail => BmsStrings.StageHoldTail,
        BmsStageImage.Mine => BmsStrings.StageMine,
        BmsStageImage.Key => BmsStrings.StageKey,
        BmsStageImage.KeyDown => BmsStrings.StageKeyDown,
        BmsStageImage.Light => BmsStrings.StageLight,
        BmsStageImage.HitLight => BmsStrings.StageHitLight,
        BmsStageImage.HoldLight => BmsStrings.StageHoldLight,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    private sealed partial class StageSubsection(LocalisableString header) : SettingsSubsection
    {
        protected override LocalisableString Header => header;

        protected override Drawable CreateHeader()
        {
            var heading = new SectionHeader(Header);
            FlowContent.Padding = new MarginPadding { Left = heading.Margin.Left };
            return heading;
        }
    }

    private sealed partial class InlineTabs<T> : OsuTabControl<T>
    {
        private readonly Func<T, LocalisableString> label;
        private readonly Func<T, LocalisableString>? tooltip;

        public InlineTabs(Func<T, LocalisableString> label, Func<T, LocalisableString>? tooltip = null)
        {
            this.label = label;
            this.tooltip = tooltip;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            TabContainer.Spacing = new Vector2(6, 3);
            SelectFirstTabByDefault = false;
        }

        protected override bool AddEnumEntriesAutomatically => false;
        protected override Dropdown<T> CreateDropdown() => null!;

        protected override TabFillFlowContainer CreateTabFlow() => new()
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            AllowMultiline = true,
            Padding = new MarginPadding { Horizontal = SettingsPanel.CONTENT_MARGINS },
        };

        protected override TabItem<T> CreateTabItem(T value) => new InlineTabItem(value, label(value), tooltip?.Invoke(value) ?? default);

        private sealed partial class InlineTabItem : OsuTabItem, IHasTooltip
        {
            public LocalisableString TooltipText { get; }

            public InlineTabItem(T value, LocalisableString caption, LocalisableString tooltipText)
                : base(value)
            {
                RelativeSizeAxes = Axes.None;
                Height = 25;
                Text.Text = caption;
                Text.Margin = new MarginPadding { Horizontal = 4, Bottom = 4 };
                TooltipText = tooltipText;
            }
        }
    }

    private sealed partial class LabelledDropdown<T>(Func<T, LocalisableString> name) : SettingsDropdown<T>
    {
        protected override OsuDropdown<T> CreateDropdown() => new LabelledDropdownControl(name);

        private sealed partial class LabelledDropdownControl(Func<T, LocalisableString> itemName) : DropdownControl
        {
            protected override LocalisableString GenerateItemText(T item) => itemName(item);
        }
    }
}
