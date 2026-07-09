using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsStage : CompositeDrawable
{
    public const float HIT_TARGET_POSITION = 80;
    public const float COLUMN_SPACING = 0;

    public IBmsColumn[] Columns { get; }

    public BmsMeasureLineContainer MeasureLineArea { get; }

    public float HitTargetPosition => hitTargetPosition.Value;

    public float BarLineHeight => barLineHeight.Value;

    public Color4 BarLineColour => barLineColour.Value;

    private readonly BindableFloat hitTargetPosition = new(HIT_TARGET_POSITION);
    private readonly BindableFloat barLineHeight = new(1);
    private readonly Bindable<Color4> barLineColour = new(Color4.White.Opacity(0.35f));
    private readonly Drawable topBorder;
    private readonly Drawable bottomBorder;
    private readonly Drawable leftBorder;
    private readonly Drawable rightBorder;
    private readonly SkinnableDrawable hitTarget;

    private readonly FillFlowContainer keyAreaOverNotesLayer = new()
    {
        RelativeSizeAxes = Axes.Y,
        AutoSizeAxes = Axes.X,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(COLUMN_SPACING, 0),
    };

    // Stage-level flow mirroring columnFlow; holds each column's HitExplosionArea so hit explosions
    // render in front of (not behind) the stage hitTarget. Width/margin are synced to the columns
    // each frame so the flow lays the areas out exactly over their columns (no manual positioning).
    private readonly FillFlowContainer hitExplosionLayer = new()
    {
        RelativeSizeAxes = Axes.Y,
        AutoSizeAxes = Axes.X,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(COLUMN_SPACING, 0),
    };

    private readonly BmsLayoutVariant layoutVariant;

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsStage(BmsPlayfield playfield)
    {
        layoutVariant = playfield.LayoutVariant;

        RelativeSizeAxes = Axes.Y;
        AutoSizeAxes = Axes.X;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        Columns = new IBmsColumn[playfield.TotalColumns];

        var columnFlow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Y,
            AutoSizeAxes = Axes.X,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(COLUMN_SPACING, 0),
        };

        InternalChildren =
        [
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.StageBackground, layoutVariant))
            {
                RelativeSizeAxes = Axes.Both,
            },
            columnFlow,
            MeasureLineArea = new BmsMeasureLineContainer
            {
                RelativeSizeAxes = Axes.Both,
            },
            hitTarget = new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, layoutVariant), _ => Empty())
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.Centre,
                CentreComponent = false,
            },
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.StageForeground, layoutVariant))
            {
                RelativeSizeAxes = Axes.Both,
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    topBorder = new Box(),
                    bottomBorder = new Box { Anchor = Anchor.BottomLeft },
                    leftBorder = new Box(),
                    rightBorder = new Box { Anchor = Anchor.TopRight, Origin = Anchor.TopRight },
                ],
            },
            keyAreaOverNotesLayer,
            // Drawn last so hit explosions sit above the judgement line, bar lines and stage foreground.
            hitExplosionLayer,
        ];

        for (var i = 0; i < playfield.TotalColumns; i++)
        {
            Columns[i] = BmsColumn.Create(i, playfield);
        }

        addColumnsInVisualOrder(columnFlow, column => (Drawable)column);
        addColumnsInVisualOrder(keyAreaOverNotesLayer, column => column.KeyArea);

        // Reparent each column's explosion container into the stage-level flow (in the same visual
        // order as the columns above) so it renders above the judgement line. The flow mirrors
        // columnFlow; width/margin are synced in Update() so each area overlays its column.
        addColumnsInVisualOrder(hitExplosionLayer, column => column.HitExplosionArea);
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (skin.IsNotNull())
            skin.SourceChanged -= updateFromSkin;
    }

    #endregion

    protected override void Update()
    {
        base.Update();

        // Column draw positions are only valid after layout, and the playfield rescales
        // the stage every frame. Recompute the centring here so the non-scratch columns
        // stay centred regardless of layout timing or current scale (important for the
        // Hide Scratch mod, where computing this once during skin load left the columns
        // off-centre by half a column width).
        updateStageCentre();
        positionKeyAreas();
        positionHitExplosionAreas();
    }

    private void positionKeyAreas()
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            var col = (Drawable)Columns[i];
            var area = Columns[i].KeyArea;
            area.Width = col.DrawWidth;
            area.Margin = area.Parent == Columns[i].KeyAreaUnderNotesLayer ? new MarginPadding() : col.Margin;
            area.Alpha = col.Alpha;
        }
    }

    private void positionHitExplosionAreas()
    {
        // The explosion areas live in a stage-level FillFlow mirroring columnFlow, so keeping each
        // area's width and margin equal to its column's is enough for the flow to lay them out
        // exactly over the columns — including layout changes, Hide Scratch, and 2P reordering.
        for (var i = 0; i < Columns.Length; i++)
        {
            var col = (Drawable)Columns[i];
            var area = Columns[i].HitExplosionArea;
            area.Width = col.DrawWidth;
            area.Margin = col.Margin;
        }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        skin.SourceChanged += updateFromSkin;
        updateFromSkin();
    }

    private void updateFromSkin()
    {
        hitTargetPosition.Value = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
                                  ?? HIT_TARGET_POSITION;
        hitTarget.Y = -hitTargetPosition.Value;
        barLineHeight.Value = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.BarLineHeight))?.Value
                              ?? 1;
        barLineColour.Value = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.BarLineColour))?.Value
                              ?? Color4.White.Opacity(0.35f);

        var lineColour = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnLineColour))?.Value
                         ?? Color4.White.Opacity(0.25f);

        updateKeyAreaLayer(skin.GetConfig<BmsSkinConfigurationLookup, bool>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.KeysUnderNotes))?.Value ?? false);

        var leftLineWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LeftLineWidth,
                new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, 0)))?.Value ?? 1;
        var rightLineWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(
            new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.RightLineWidth,
                new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, Columns.Length - 1)))?.Value ?? 1;

        foreach (var border in new[] { topBorder, bottomBorder, leftBorder, rightBorder })
            border.Colour = lineColour;

        topBorder.Height = bottomBorder.Height = Math.Max(leftLineWidth, rightLineWidth);
        leftBorder.Width = leftLineWidth;
        rightBorder.Width = rightLineWidth;
        leftBorder.Alpha = leftLineWidth > 0 ? 1 : 0;
        rightBorder.Alpha = rightLineWidth > 0 ? 1 : 0;
        topBorder.Alpha = bottomBorder.Alpha = Math.Max(leftLineWidth, rightLineWidth) > 0 ? 1 : 0;

        Padding = new MarginPadding
        {
            Top = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.StagePaddingTop))?.Value ?? 0,
            Bottom = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.StagePaddingBottom))?.Value ?? 0,
        };

        topBorder.Width = bottomBorder.Width = DrawWidth;
        leftBorder.Height = rightBorder.Height = DrawHeight;

        updateStageCentre();
    }

    private void updateKeyAreaLayer(bool keysUnderNotes)
    {
        keyAreaOverNotesLayer.Clear(false);

        foreach (var column in Columns)
            column.KeyAreaUnderNotesLayer.Clear(false);

        if (keysUnderNotes)
        {
            foreach (var column in Columns)
                column.KeyAreaUnderNotesLayer.Add(column.KeyArea);
        }
        else
            addColumnsInVisualOrder(keyAreaOverNotesLayer, column => column.KeyArea);
    }

    private void addColumnsInVisualOrder(FillFlowContainer target, Func<IBmsColumn, Drawable> selector)
    {
        if (BmsLayout.Is2P(layoutVariant))
        {
            for (var i = 1; i < Columns.Length; i++)
                target.Add(selector(Columns[i]));

            target.Add(selector(Columns[0]));
        }
        else
        {
            for (var i = 0; i < Columns.Length; i++)
                target.Add(selector(Columns[i]));
        }
    }

    private void updateStageCentre()
    {
        var nonScratchCentre = getNonScratchCentreX();
        X = (DrawWidth / 2 - nonScratchCentre) * Scale.X;
    }

    private float getNonScratchCentreX()
    {
        var min = float.MaxValue;
        var max = float.MinValue;

        foreach (var column in Columns)
        {
            if (BmsLayout.IsScratchColumn(column.ColumnIndex, layoutVariant))
                continue;

            var d = (Drawable)column;
            min = Math.Min(min, d.DrawPosition.X);
            max = Math.Max(max, d.DrawPosition.X + d.DrawWidth);
        }

        return min == float.MaxValue ? DrawWidth / 2 : (min + max) / 2;
    }
}
