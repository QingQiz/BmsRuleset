using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsStageHud : BmsHudComponent, ISerialisableDrawable
{
    [SettingSource(null, SettingControlType = typeof(BmsHiddenSetting<float>))]
    public BindableFloat JudgementLineOffset { get; } = new()
    {
        MinValue = -768,
        MaxValue = 768,
        Precision = 1,
    };

    [SettingSource(null, SettingControlType = typeof(BmsHiddenSetting<float>))]
    public BindableFloat LightPositionOffset { get; } = new()
    {
        MinValue = -768,
        MaxValue = 768,
        Precision = 1,
    };

    [SettingSource(null, SettingControlType = typeof(BmsHiddenSetting<float>))]
    public BindableFloat NoteHeightScale { get; } = new(1)
    {
        MinValue = 0.01f,
        MaxValue = 5,
        Precision = 0.01f,
    };

    // osu! saves the HUD target by ruleset. Keep its single editable Stage handle and store
    // the other layouts in its settings so a save or undo includes all of them.
    [SettingSource(null, SettingControlType = typeof(BmsHiddenSetting<Dictionary<int, StageLayout>>))]
    // ReSharper disable once MemberCanBePrivate.Global
    public Bindable<Dictionary<int, StageLayout>> Layouts { get; }

    private const int legacy_layout = 0;

    [SettingSource(null, SettingControlType = typeof(BmsStageEditorSetting))]
    public Bindable<Dictionary<int, BmsStageStyle>> StageStyles { get; } = new(new Dictionary<int, BmsStageStyle>());

    private Dictionary<int, StageLayout> savedLayouts = [];
    private BmsLayoutVariant? activeLayout;
    internal BmsLayoutVariant LayoutVariant => activeLayout ?? throw new InvalidOperationException("Stage settings require an active gameplay layout.");
    internal BmsEmbeddedSkinSource GameplaySkin => ((BmsPlayfield)drawableRuleset.Playfield).GameplaySkin;

    // Use the column geometry directly so the sidebar can resize the handle before the next autosize pass.
    internal float ColumnLayoutWidth => ((BmsPlayfield)drawableRuleset.Playfield).Stage.Columns.Sum(column => ((Drawable)column).LayoutSize.X);

    private StageLayout? inheritedLayout;
    private readonly Container editHandle;
    private BmsStageHudController? controller;

    [Resolved]
    private DrawableRuleset drawableRuleset { get; set; } = null!;

    public BmsStageHud()
    {
        Layouts = new LayoutSnapshots(this);
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        RelativeSizeAxes = Axes.Y;
        Size = Vector2.Zero;
        AlwaysPresent = true;
        Alpha = 0;

        InternalChild = editHandle = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            BorderThickness = 2,
            BorderColour = new Color4(70, 210, 255, 255),
            Alpha = 0,
            Children =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(20, 40, 55, 255),
                    Alpha = 0.45f,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.X,
                    Height = 2,
                    Colour = Color4.White,
                },
                new Box
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Width = 2,
                    Colour = Color4.White,
                },
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (drawableRuleset is BmsDrawableRuleset bmsDrawableRuleset)
        {
            controller = bmsDrawableRuleset.StageHudController;
            controller.Register(this, this.FindClosestParent<ISerialisableDrawableContainer>());
            StageStyles.ValueChanged += onStageStylesChanged;
            JudgementLineOffset.ValueChanged += onJudgementLineOffsetChanged;
            LightPositionOffset.ValueChanged += onLightPositionOffsetChanged;
            NoteHeightScale.ValueChanged += onNoteHeightScaleChanged;
        }

        if (SkinEditor != null)
            SkinEditor.State.BindValueChanged(_ => updateEditModeVisibility(), true);
        else
            applyEditModeVisibility(false);
    }

    protected override void Update()
    {
        var position = Position;
        base.Update();

        // Fitting a borrowed position to the preview bounds should not create a 2P override.
        if (inheritedLayout is { } inherited && inherited.Position == position && Position != position)
            inheritedLayout = inherited with { Position = Position };
    }

    protected override void Dispose(bool isDisposing)
    {
        StageStyles.ValueChanged -= onStageStylesChanged;
        JudgementLineOffset.ValueChanged -= onJudgementLineOffsetChanged;
        LightPositionOffset.ValueChanged -= onLightPositionOffsetChanged;
        NoteHeightScale.ValueChanged -= onNoteHeightScaleChanged;
        controller?.Unregister(this);
        controller = null;

        base.Dispose(isDisposing);
    }

    internal void UseLayout(BmsLayoutVariant layout)
    {
        // Each gameplay preview owns a fresh handle; undo restores values in that same layout.
        if (activeLayout != null)
            return;

        if (savedLayouts.Count == 0)
            savedLayouts[legacy_layout] = captureLayout();

        activeLayout = layout;
        restoreLayout();
    }

    internal BmsStageStyle GetStageStyle(BmsLayoutVariant layout)
    {
        var layouts = StageStyles.Value;
        if (layouts.TryGetValue((int)layout, out var style))
            return style;

        var firstPlayerVariant = BmsLayout.Is2P(layout) ? BmsLayout.FirstPlayerVariant(layout) : layout;
        return layouts.GetValueOrDefault((int)firstPlayerVariant) ?? empty_style;
    }

    private static readonly BmsStageStyle empty_style = new();

    internal BmsStageHud CreateResetForLayout(BmsLayoutVariant layout)
    {
        var replacement = new BmsStageHud();
        replacement.savedLayouts = new Dictionary<int, StageLayout>(savedLayouts);
        var styles = new Dictionary<int, BmsStageStyle>(StageStyles.Value);
        styles.Remove((int)layout);
        replacement.StageStyles.Value = styles;
        if (!BmsLayout.Is2P(layout))
            replacement.savedLayouts[(int)layout] = replacement.captureLayout();
        return replacement;
    }

    void ISerialisableDrawable.CopyAdjustedSetting(IBindable target, object source)
    {
        if (target != Layouts)
        {
            if (target == StageStyles)
            {
                var styles = source is JObject styleJson ? deserializeStyles(styleJson) : (Dictionary<int, BmsStageStyle>)source;
                foreach (var layout in styles.Keys.ToArray())
                {
                    if (styles[layout] == null)
                        styles.Remove(layout);
                    else
                        styles[layout].Normalize((BmsLayoutVariant)layout);
                }

                StageStyles.Value = new Dictionary<int, BmsStageStyle>(styles);
                return;
            }

            ((IParseable)target).Parse(source, CultureInfo.InvariantCulture);
            return;
        }

        var layouts = source is JObject json ? json.ToObject<Dictionary<int, StageLayout>>()! : (Dictionary<int, StageLayout>)source;
        savedLayouts = new Dictionary<int, StageLayout>(layouts);

        // Undo can apply serialised info to an already loaded handle, after the ordinary
        // drawable properties and legacy settings have been restored.
        restoreLayout();
    }

    private static Dictionary<int, BmsStageStyle> deserializeStyles(JObject json)
    {
        var styles = json.ToObject<Dictionary<int, BmsStageStyle>>()!;
        foreach (var (layout, style) in styles)
        {
            // Older saves used a single width for all interior boundaries.
            if (json[layout.ToString(CultureInfo.InvariantCulture)] is not JObject values
                || values[nameof(BmsStageStyle.SeparatorWidths)] != null
                || values["SeparatorWidth"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } width)
                continue;

            var count = BmsLayout.GetTotalColumns((BmsLayoutVariant)layout) - 1;
            var sharedWidth = width.Value<float>();
            for (var i = 0; i < count; i++)
                style.SeparatorWidths[i] = sharedWidth;
        }

        return styles;
    }

    private void restoreLayout()
    {
        if (activeLayout is not { } variant)
            return;

        var layout1P = BmsLayout.FirstPlayerVariant(variant);
        var hasOverride = savedLayouts.Remove((int)variant, out var layout);
        var inherits = !hasOverride && layout1P != variant;
        if (inherits)
            savedLayouts.TryGetValue((int)layout1P, out layout);

        layout ??= savedLayouts.GetValueOrDefault(legacy_layout);
        inheritedLayout = null;
        if (layout == null)
            return;

        Anchor = layout.Anchor;
        Origin = layout.Origin;
        UsesFixedAnchor = layout.UsesFixedAnchor;
        Position = layout.Position;
        Rotation = layout.Rotation;
        Scale = layout.Scale;
        Size = layout.Size;
        JudgementLineOffset.Value = layout.JudgementLineOffset;
        LightPositionOffset.Value = layout.LightPositionOffset;
        NoteHeightScale.Value = layout.NoteHeightScale;

        // Preview and undo can serialise without editing the Stage. Only save a 2P override
        // when the live settings differ from what the left-scratch layout supplied.
        if (inherits)
            inheritedLayout = captureLayout();
    }

    internal void ApplyAutomaticSize(Vector2 size)
    {
        if (inheritedLayout is { } inherited)
        {
            // Derived dimensions are not edits, even if the other dimension was already resized.
            var inheritedSize = new Vector2(inherited.Size.X == Width ? size.X : inherited.Size.X,
                inherited.Size.Y == Height ? size.Y : inherited.Size.Y);
            if (inheritedSize != inherited.Size)
                inheritedLayout = inherited with { Size = inheritedSize };
        }

        Size = size;
    }

    private StageLayout captureLayout() => new(Anchor, Origin, UsesFixedAnchor, Position, Rotation, Scale, Size,
        JudgementLineOffset.Value, LightPositionOffset.Value, NoteHeightScale.Value);

    public sealed record StageLayout(Anchor Anchor, Anchor Origin, bool UsesFixedAnchor, Vector2 Position, float Rotation,
        Vector2 Scale, Vector2 Size, float JudgementLineOffset, float LightPositionOffset, float NoteHeightScale);

    private sealed class LayoutSnapshots(BmsStageHud hud) : Bindable<Dictionary<int, StageLayout>>
    {
        public override Dictionary<int, StageLayout> Value
        {
            get
            {
                var snapshot = new Dictionary<int, StageLayout>(hud.savedLayouts);
                if (hud.activeLayout is { } variant)
                {
                    var current = hud.captureLayout();
                    if (current != hud.inheritedLayout)
                        snapshot[(int)variant] = current;
                }

                return snapshot;
            }
        }
    }

    private void onStageStylesChanged(ValueChangedEvent<Dictionary<int, BmsStageStyle>> _) => controller?.RefreshStageStyle();

    private void onJudgementLineOffsetChanged(ValueChangedEvent<float> offset) => controller?.SetHitTargetPositionOffset(offset.NewValue);

    private void onLightPositionOffsetChanged(ValueChangedEvent<float> offset) => controller?.SetLightPositionOffset(offset.NewValue);

    private void onNoteHeightScaleChanged(ValueChangedEvent<float> scale) => controller?.SetNoteHeightScale(scale.NewValue);

    internal void SetJudgementLineOffsetRange(float minimum, float maximum)
    {
        var value = JudgementLineOffset.Value;
        if (JudgementLineOffset.MinValue != minimum)
            JudgementLineOffset.MinValue = minimum;
        if (JudgementLineOffset.MaxValue != maximum)
            JudgementLineOffset.MaxValue = maximum;

        if (inheritedLayout is { } inherited && inherited.JudgementLineOffset == value && JudgementLineOffset.Value != value)
            inheritedLayout = inherited with { JudgementLineOffset = JudgementLineOffset.Value };
    }

    internal void SetLightPositionOffsetRange(float minimum, float maximum)
    {
        var value = LightPositionOffset.Value;
        if (LightPositionOffset.MinValue != minimum)
            LightPositionOffset.MinValue = minimum;
        if (LightPositionOffset.MaxValue != maximum)
            LightPositionOffset.MaxValue = maximum;

        if (inheritedLayout is { } inherited && inherited.LightPositionOffset == value && LightPositionOffset.Value != value)
            inheritedLayout = inherited with { LightPositionOffset = LightPositionOffset.Value };
    }

    private void updateEditModeVisibility() => applyEditModeVisibility(SkinEditor?.State.Value == Visibility.Visible);

    private void applyEditModeVisibility(bool isEditing)
    {
        ClearTransforms();

        // Runtime rendering belongs to BmsStage; this shell only exposes a stable skin-editor handle.
        Alpha = isEditing ? 1 : 0;
        editHandle.Alpha = isEditing ? 1 : 0;
    }
}
