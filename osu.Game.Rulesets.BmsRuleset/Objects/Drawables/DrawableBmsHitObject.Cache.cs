using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public partial class DrawableBmsHitObject
{
    private readonly record struct LayoutMetrics(
        int Column,
        BmsLayoutVariant LayoutVariant,
        Drawable? ColumnContainer,
        float ParentHeight,
        float ScaledParentWidth,
        float TravelDistance,
        float HitTargetPosition,
        double ScrollSpeedMultiplier,
        BmsTimingMap? TimingMap,
        double CurrentScrollPosition,
        double ScrollRange);

    private readonly record struct LayoutReferences(
        BmsStage? Stage,
        Drawable? ColumnContainer,
        int Column,
        BmsLayoutVariant LayoutVariant)
    {
        public BmsColumn? ColumnDrawable =>
            Stage != null && Column < Stage.Columns.Length ? Stage.Columns[Column] : null;
    }

    private sealed class DrawableCache
    {

        public bool HasLayout => LayoutRef != null;

        public int CurrentColumn => LayoutRef?.Column ?? 0;

        public BmsLayoutVariant CurrentLayoutVariant => LayoutRef?.LayoutVariant ?? BmsLayoutVariant.Bme7K;

        public BmsPlayfield? Playfield;
        public LayoutReferences? LayoutRef;
        public LayoutMetrics? LatestLayout;
        public bool? ColumnHidden;
        public float ScaledParentWidth = -1;
        public float ParentWidthForTransform;
        public float ParentHeightForTransform;

        public float NoteHeightWidth = -1;
        public int NoteHeightColumn = -1;
        public BmsLayoutVariant? NoteHeightLayout;
        public BmsSkinComponents? NoteHeightComponent;

        public int SkinnedColumn = -1;
        public BmsLayoutVariant? SkinnedLayout;
        public BmsSkinComponents? SkinnedComponent;

        public void ApplyLayout(BmsStage? stage, Drawable? columnContainer, int column, BmsLayoutVariant layoutVariant)
        {
            LayoutRef = new LayoutReferences(stage, columnContainer, column, layoutVariant);
        }

        public void ComputeColumnHidden()
        {
            var col = LayoutRef?.ColumnDrawable;
            ColumnHidden = col?.Hidden == true && col.IsScratch;
        }

        public LayoutMetrics CreateLayoutMetrics(float parentHeight, float scaledParentWidth, double currentTime)
        {
            var stage = LayoutRef?.Stage;
            var col = LayoutRef?.Column ?? 0;
            var variant = LayoutRef?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
            var container = LayoutRef?.ColumnContainer;

            return new LayoutMetrics(col, variant, container,
                parentHeight,
                Math.Max(1, scaledParentWidth),
                Math.Max(1f, parentHeight - (stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION)),
                stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION,
                Playfield?.ScrollSpeedMultiplier ?? 1,
                Playfield?.TimingMap,
                Playfield?.CurrentScrollPosition ?? currentTime,
                Playfield?.ScrollRange ?? Playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(BmsRulesetConfigManager.DEFAULT_SCROLL_SPEED));
        }

        public void ApplyTransform(float parentWidth, float parentHeight, float scaledWidth)
        {
            ScaledParentWidth = scaledWidth;
            ParentWidthForTransform = parentWidth;
            ParentHeightForTransform = parentHeight;
        }

        public bool TransformChanged(float parentWidth, float parentHeight) =>
            Math.Abs(parentWidth - ParentWidthForTransform) >= 1
            || Math.Abs(parentHeight - ParentHeightForTransform) >= 1;

        public bool TryGetLayout(out LayoutReferences refs)
        {
            if (LayoutRef is { } r)
            {
                refs = r;
                return true;
            }

            refs = default;
            return false;
        }

        public bool IsNoteHeightValid(LayoutMetrics layout, BmsSkinComponents component) =>
            Math.Abs(NoteHeightWidth - layout.ScaledParentWidth) < 1
            && NoteHeightColumn == layout.Column
            && NoteHeightLayout == layout.LayoutVariant
            && NoteHeightComponent == component;

        public bool IsSkinValid(int column, BmsLayoutVariant layoutVariant, BmsSkinComponents component) =>
            SkinnedColumn == column && SkinnedLayout == layoutVariant && SkinnedComponent == component;

        public void InvalidateAll()
        {
            Playfield = null;
            LayoutRef = null;
            LatestLayout = null;
            ColumnHidden = null;
            ScaledParentWidth = -1;
            ParentWidthForTransform = 0;
            ParentHeightForTransform = 0;
            InvalidateNoteHeight();
            InvalidateSkin();
        }

        public void InvalidateNoteHeight()
        {
            NoteHeightWidth = -1;
            NoteHeightColumn = -1;
            NoteHeightLayout = null;
            NoteHeightComponent = null;
        }

        public void InvalidateSkin()
        {
            SkinnedColumn = -1;
            SkinnedLayout = null;
            SkinnedComponent = null;
        }
    }
}
