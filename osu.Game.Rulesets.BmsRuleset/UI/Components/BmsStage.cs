using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsStage : CompositeDrawable
{
    public const float HIT_TARGET_POSITION = 80;
    public const float COLUMN_SPACING = 0;

    public BmsColumn[] Columns { get; }

    public Container JudgementArea { get; }

    public Container MeasureLineArea { get; }

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
    private readonly BmsLayoutVariant layoutVariant;

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsStage(int totalColumns, BmsLayoutVariant layoutVariant)
    {
        this.layoutVariant = layoutVariant;

        RelativeSizeAxes = Axes.Y;
        AutoSizeAxes = Axes.X;
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;

        Columns = new BmsColumn[totalColumns];

        var columnFlow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.Y,
            AutoSizeAxes = Axes.X,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(COLUMN_SPACING, 0),
        };

        InternalChildren =
        [
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.StageBackground, layoutVariant), _ => new DefaultBmsStageBackground())
            {
                RelativeSizeAxes = Axes.Both,
            },
            columnFlow,
            MeasureLineArea = new Container
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
            JudgementArea = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
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
        ];

        for (var i = 0; i < totalColumns; i++)
        {
            Columns[i] = new BmsColumn(i, layoutVariant);
        }

        // For 2P variants, render scratch column last so visual order becomes
        // [keys…, scratch] while ColumnWidth[0] still defines the scratch lane width.
        var addOrder = layoutVariant is BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P
            ? Enumerable.Range(0, totalColumns).OrderBy(col => Columns[col].IsScratch ? 1 : 0).ThenBy(col => col)
            : Enumerable.Range(0, totalColumns);

        foreach (var i in addOrder)
            columnFlow.Add(Columns[i]);
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
        JudgementArea.Y = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ScorePosition))?.Value
                          ?? 300 * 1.6f;
        barLineHeight.Value = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.BarLineHeight))?.Value
                              ?? 1;
        barLineColour.Value = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.BarLineColour))?.Value
                              ?? Color4.White.Opacity(0.35f);

        var lineColour = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnLineColour))?.Value
                         ?? Color4.White.Opacity(0.25f);
        // Use BmsSkinComponentLookup as the component context so that ManiaColumnIndex is
        // computed from the BMS column index.  Passing a raw BMS column index (e.g. 7 for
        // BME 7K) directly to the native mania skin would cause ColumnLineWidth[7+1] to go
        // out of bounds on an 8-element array (keys+1 == 8, valid indices 0–7).
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

    private void updateStageCentre()
    {
        var nonScratchCentre = getNonScratchCentreX();
        X = (DrawWidth / 2 - nonScratchCentre) * Scale.X;

        // Keep the judgement area centred over the non-scratch columns, not the
        // stage's geometric centre (which includes the scratch lane).
        JudgementArea.X = nonScratchCentre - DrawWidth / 2;
    }

    private float getNonScratchCentreX()
    {
        var min = float.MaxValue;
        var max = float.MinValue;

        foreach (var column in Columns)
        {
            if (BmsLayout.IsScratchColumn(column.Index, layoutVariant))
                continue;

            min = Math.Min(min, column.DrawPosition.X);
            max = Math.Max(max, column.DrawPosition.X + column.DrawWidth);
        }

        return min == float.MaxValue ? DrawWidth / 2 : (min + max) / 2;
    }

    private partial class DefaultBmsStageBackground : CompositeDrawable
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.Black,
                Alpha = 0.35f,
            };
        }
    }
}
