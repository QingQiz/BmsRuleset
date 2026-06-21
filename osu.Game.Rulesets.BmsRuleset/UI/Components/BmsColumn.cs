using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

[Cached]
public partial class BmsColumn : Playfield, IBmsColumn
{
    public const float COLUMN_WIDTH = 42;
    public const float SCRATCH_COLUMN_WIDTH = 50;

    public readonly int Index;

    public int ColumnIndex => Index;

    public bool IsScratch { get; }

    public Container HitExplosionArea { get; }

    /// <summary>
    ///     When <c>true</c>, this column is hidden from layout — zero width, zero
    ///     alpha, and <see cref="updateFromSkin"/> will not restore visual properties.
    /// </summary>
    public bool Hidden
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;

            if (value)
            {
                Width = 0;
                Alpha = 0;
                Margin = new MarginPadding();
            }
        }
    }

    protected BmsPlayfield ParentPlayfield { get; }

    private readonly BmsLayoutVariant layoutVariant;
    private readonly SkinnableDrawable hitTarget;

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsColumn(int index, BmsPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        layoutVariant = playfield.LayoutVariant;
        IsScratch = BmsLayout.IsScratchColumn(index, layoutVariant);

        RelativeSizeAxes = Axes.Y;
        Width = defaultColumnWidth(index, layoutVariant);
        Masking = true;
        BorderThickness = 0;

        InternalChildren =
        [
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, index), _ => new DefaultBmsColumnBackground(index, IsScratch))
            {
                RelativeSizeAxes = Axes.Both,
            },
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, layoutVariant, index), _ => new DefaultBmsKeyArea(index, layoutVariant, IsScratch))
            {
                RelativeSizeAxes = Axes.Both,
                CentreComponent = false,
            },
            HitExplosionArea = new Container { RelativeSizeAxes = Axes.Both },
            hitTarget = new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, layoutVariant, index), _ => new DefaultBmsHitTarget(IsScratch))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.Centre,
                CentreComponent = false,
            },
        ];
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        if (skin.IsNotNull())
            skin.SourceChanged -= updateFromSkin;
    }

    #endregion

    public static BmsColumn Create(int index, BmsPlayfield playfield)
    {
        var providerType = BmsColumnFactory.GetColumnProviderType(index);
        var genericType = typeof(BmsColumnGeneric<>).MakeGenericType(providerType);
        return (BmsColumn)Activator.CreateInstance(genericType, index, playfield)!;
    }

    protected override HitObjectContainer CreateHitObjectContainer()
        => new BmsColumnHitObjectContainer(ParentPlayfield);

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
        => new BmsHitObjectLifetimeEntry(hitObject, ParentPlayfield);

    private static float defaultColumnWidth(int index, BmsLayoutVariant layoutVariant) =>
        BmsLayout.IsScratchColumn(index, layoutVariant) ? SCRATCH_COLUMN_WIDTH : COLUMN_WIDTH;

    [BackgroundDependencyLoader]
    private void load()
    {
        skin.SourceChanged += updateFromSkin;
        updateFromSkin();
    }

    private void updateFromSkin()
    {
        if (Hidden)
            return;

        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, Index);
        Width = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup))?.Value
                ?? defaultColumnWidth(Index, layoutVariant);

        // For 2P/scratch-on-right, ColumnSpacing indices must be remapped to follow
        // visual column order [keys…, scratch] rather than BMS index order.
        int? spacingLeftCol;
        int? spacingRightCol;

        if (BmsLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var totalCols = BmsLayout.GetTotalColumns(layoutVariant);
            (spacingLeftCol, spacingRightCol) = BmsLayout.RemapColum2PGapIdx(colIdx, totalCols);
        }
        else
        {
            spacingLeftCol = lookup.ColumnIndex;
            spacingRightCol = lookup.ColumnIndex;
        }

        var spacingLookupLeft = spacingLeftCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, spacingLeftCol.Value)
            : null;

        var spacingLookupRight = spacingRightCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, spacingRightCol.Value)
            : null;

        Margin = new MarginPadding
        {
            Left = spacingLookupLeft != null
                ? skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LeftColumnSpacing, spacingLookupLeft))?.Value ?? 0
                : 0,
            Right = spacingLookupRight != null
                ? skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, spacingLookupRight))?.Value ?? 0
                : 0,
        };

        hitTarget.Y = -(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
                        ?? BmsStage.HIT_TARGET_POSITION);
    }

    #region components

    private partial class DefaultBmsColumnBackground(int index, bool isScratch) : CompositeDrawable
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = isScratch
                    ? Color4.DarkSlateBlue.Opacity(0.26f)
                    : index % 2 == 0
                        ? Color4.Black.Opacity(0.28f)
                        : Color4.White.Opacity(0.05f),
            };
        }
    }

    private sealed partial class DefaultBmsHitTarget : CompositeDrawable
    {
        private readonly bool isScratch;

        public DefaultBmsHitTarget(bool isScratch)
        {
            this.isScratch = isScratch;
            Height = isScratch ? 5 : 3;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White.Opacity(isScratch ? 0.85f : 0.65f),
            };
        }
    }

    /// <inheritdoc cref="CompositeDrawable" />
    /// <summary>
    ///     Default code-drawn key area that shows at the bottom of each column and
    ///     brightens briefly when the bound key is pressed.
    /// </summary>
    private sealed partial class DefaultBmsKeyArea(int columnIndex, BmsLayoutVariant layoutVariant, bool isScratch)
        : CompositeDrawable, IKeyBindingHandler<BmsAction>
    {

        private Box light = null!;

        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(e.Action, layoutVariant) != columnIndex)
                return false;

            light.FadeIn(10);
            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(e.Action, layoutVariant) != columnIndex)
                return;

            light.FadeOut(120);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;

            InternalChildren =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 60,
                    Colour = isScratch ? Color4.DarkSlateBlue.Opacity(0.55f) : Color4.White.Opacity(0.08f),
                },
                light = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 60,
                    Colour = Color4.White.Opacity(0.45f),
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            ];
        }
    }

    #endregion

}
