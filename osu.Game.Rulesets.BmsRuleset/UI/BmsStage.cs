using System;
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

namespace osu.Game.Rulesets.BmsRuleset.UI;

public sealed partial class BmsStage : CompositeDrawable
{
    public const float HIT_TARGET_POSITION = 80;
    public const float COLUMN_SPACING = 0;

    public BmsColumn[] Columns { get; }

    public Container JudgementArea { get; }

    public float HitTargetPosition => hitTargetPosition.Value;

    private readonly BindableFloat hitTargetPosition = new(HIT_TARGET_POSITION);
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
            var column = Columns[i] = new BmsColumn(i, layoutVariant);
            columnFlow.Add(column);
        }
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

        topBorder.Width = bottomBorder.Width = DrawWidth;
        leftBorder.Height = rightBorder.Height = DrawHeight;

        var nonScratchCentre = getNonScratchCentreX();
        X = (DrawWidth / 2 - nonScratchCentre) * Scale.X;
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

        var lineColour = skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnLineColour))?.Value
                         ?? Color4.White.Opacity(0.25f);
        var leftLineWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LeftLineWidth, columnIndex: 0))?.Value ?? 1;
        var rightLineWidth = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.RightLineWidth, columnIndex: Columns.Length - 1))?.Value ?? 1;

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
    }

    private float getNonScratchCentreX()
    {
        var min = float.MaxValue;
        var max = float.MinValue;

        foreach (var column in Columns)
        {
            if (BmsSkinComponentLookup.IsScratchColumn(column.Index, layoutVariant))
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
