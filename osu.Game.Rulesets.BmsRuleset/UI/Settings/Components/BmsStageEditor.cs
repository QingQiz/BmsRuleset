using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
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
    private OsuColour colours { get; set; } = null!;

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
                withCaptionActions(new FormCheckBox
                {
                    Caption = BmsStrings.StageKeepTotalWidth,
                    HintText = BmsStrings.StageKeepTotalWidthDescription,
                    Current = settingValue(true, style.KeepTotalWidth, (currentStyle, value) => currentStyle.KeepTotalWidth = value ?? true),
                }),
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
                    withCaptionActions(slider(BmsStrings.StageHitTargetHeight, 1, style.HitTargetHeightScale, 0.1f, BmsStageStyle.MAX_HIT_TARGET_HEIGHT_SCALE,
                        (currentStyle, value) => currentStyle.HitTargetHeightScale = value)),
                    boundSlider(BmsStrings.JudgementLineOffset, BmsStrings.JudgementLineOffsetDescription, stage.JudgementLineOffset),
                    boundSlider(BmsStrings.LightPositionOffset, BmsStrings.LightPositionOffsetDescription, stage.LightPositionOffset),
                ],
            },
            new StageSubsection(BmsStrings.StageMeasureLineSettings)
            {
                Children =
                [
                    withCaptionActions(new FormCheckBox { Caption = BmsStrings.StageShowMeasureLines, Current = showLines }),
                    withCaptionActions(slider(BmsStrings.StageBarHeight, baseBarHeight, style.BarHeight, 0, BmsStageStyle.MAX_LINE_WIDTH,
                        (currentStyle, value) => currentStyle.BarHeight = value)),
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

    private Drawable boundSlider(LocalisableString label, LocalisableString tooltip, BindableFloat value)
    {
        var control = new FormSliderBar<float>
        {
            Caption = label,
            HintText = tooltip,
            Current = value,
            KeyboardStep = value.Precision,
        };
        // The toolbox listens to top-level settings only; these sliders still edit the original HUD bindables.
        if (changeHandler != null)
            control.ValueChanged += changeHandler.SaveState;
        return withCaptionActions(control);
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

        FormSliderBar<float> laneSlider(LocalisableString label, float defaultValue, float min, float max, Func<BmsStageStyle, Dictionary<int, float>> values) =>
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

    private FormSliderBar<float> indexedSlider(LocalisableString label, float defaultValue, float min, float max, int index,
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

    private Drawable batchSlider(FormSliderBar<float> control, Func<BmsStageStyle, Dictionary<int, float>> values, int count, LocalisableString label,
                                 Func<int, float>? defaultValue = null)
    {
        if (count == 1)
            return withCaptionActions(control);

        return withCaptionActions(control, label, () => updateStyle(style =>
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
        var imageName = new Bindable<string>(string.Empty) { Value = style.GetImage(slot, selectedColumn) ?? string.Empty };
        imageName.BindValueChanged(change => updateStyle(currentStyle => currentStyle.SetImage(slot, selectedColumn, change.NewValue)));
        var original = baseImageName(slot, selectedColumn);
        var defaultLabel = original == null ? BmsStrings.StageUseSkin : BmsStrings.StageUseSkinImage(original);
        var control = new StageImageDropdown(name => string.IsNullOrEmpty(name) ? defaultLabel : files.GetValueOrDefault(name, name))
        {
            Caption = label,
            AlwaysShowSearchBar = true,
            Items = files.Keys.Prepend(string.Empty).Append(imageName.Value).Distinct(StringComparer.Ordinal),
            Current = imageName,
        };
        var count = BmsLayout.GetTotalColumns(layout);
        if (selectedColumn == null || count == 1)
            return withCaptionActions(control);

        return withCaptionActions(control, BmsStrings.StageApplyToAllColumns, () => updateStyle(currentStyle =>
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

    private Drawable withCaptionActions<T>(IHasCurrentValue<T> control, LocalisableString applyLabel = default, Action? apply = null)
    {
        var drawable = (Drawable)control;
        var value = control.Current;
        drawable.OnLoadComplete += _ =>
        {
            var caption = findCaption(drawable)!;
            caption.OnLoadComplete += _ =>
            {
                var text = (OsuTextFlowContainer)getInternalChildren(caption).Single();
                var resetButton = new CaptionActionButton
                {
                    Size = new Vector2(16),
                    // Match the question mark inside the help circle while keeping the full hit target.
                    IconScale = new Vector2(0.34f),
                    Icon = FontAwesome.Solid.Undo,
                    IconColour = colours.BlueLight,
                    TooltipText = BmsStrings.StageRestoreDefault,
                    Action = () =>
                    {
                        // Commit a focused number field before resetting it.
                        drawable.FindClosestParent<IFocusManager>()!.ChangeFocus(null);
                        value.SetDefault();
                    },
                };

                resetButton.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(11),
                    Depth = 1,
                    Colour = colours.Gray4,
                });

                var actions = new CaptionActions
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Children = [resetButton],
                };

                if (apply != null)
                {
                    actions.Add(new CaptionActionButton
                    {
                        Size = new Vector2(16),
                        IconScale = new Vector2(0.58f),
                        Icon = FontAwesome.Regular.Clone,
                        TooltipText = applyLabel,
                        Action = () =>
                        {
                            drawable.FindClosestParent<IFocusManager>()!.ChangeFocus(null);
                            apply();
                        },
                    });
                }

                updateReset();
                value.ValueChanged += _ => updateReset();
                value.DefaultChanged += _ => updateReset();
                value.DisabledChanged += _ => updateReset();

                // Use the caption's text flow so the actions follow the help icon even when a translation wraps.
                text.AddArbitraryDrawable(actions);

                void updateReset()
                {
                    var canReset = !value.IsDefault && !value.Disabled;
                    resetButton.Alpha = canReset ? 1 : 0;
                    resetButton.Enabled.Value = canReset;
                }
            };
        };
        return drawable;
    }

    private sealed partial class CaptionActions : FillFlowContainer<IconButton>, IHasLineBaseHeight
    {
        // FormFieldCaption draws its 10-unit help icon one unit below the baseline.
        public float LineBaseHeight => DrawHeight > 0 ? DrawHeight / 2 + 4 : 0;
    }

    private sealed partial class CaptionActionButton : IconButton
    {
        public override bool HandlePositionalInput => Enabled.Value && base.HandlePositionalInput;
    }

    private static FormFieldCaption? findCaption(Drawable drawable)
    {
        if (drawable is FormFieldCaption caption)
            return caption;

        if (drawable is CompositeDrawable composite)
        {
            foreach (var child in getInternalChildren(composite))
            {
                if (findCaption(child) is { } found)
                    return found;
            }
        }

        return null;
    }

    // Form controls construct their captions privately and expose no caption accessory slot.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InternalChildren")]
    private static extern IReadOnlyList<Drawable> getInternalChildren(CompositeDrawable drawable);

    private FormSliderBar<float> slider(LocalisableString caption, float defaultValue, float? value, float min, float max, Action<BmsStageStyle, float?> apply)
    {
        var currentValue = new BindableFloat(defaultValue)
        {
            MinValue = min,
            MaxValue = max,
            Value = value ?? defaultValue,
        };
        bindOverride(currentValue, apply);
        return new FormSliderBar<float>
        {
            Caption = caption,
            RelativeSizeAxes = Axes.X,
            Current = currentValue,
            // The display rounds long decimals. Only committed input should become a skin override.
            TransferValueOnCommit = true,
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
            TabContainer.Anchor = Anchor.TopLeft;
            TabContainer.Origin = Anchor.TopLeft;
            SelectFirstTabByDefault = false;
        }

        protected override bool AddEnumEntriesAutomatically => false;
        protected override Dropdown<T> CreateDropdown() => null!;

        protected override TabFillFlowContainer CreateTabFlow() => new()
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            AllowMultiline = true,
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
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

    private sealed partial class StageImageDropdown(Func<string, LocalisableString> name) : FormDropdown<string>
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The header's last child is its full-area click handler. Give the caption buttons priority.
            changeInternalChildDepth(Header, getInternalChildren(Header).Last(), 1);
        }

        protected override LocalisableString GenerateItemText(string item) => name(item);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ChangeInternalChildDepth")]
        private static extern void changeInternalChildDepth(CompositeDrawable parent, Drawable child, float depth);
    }
}
