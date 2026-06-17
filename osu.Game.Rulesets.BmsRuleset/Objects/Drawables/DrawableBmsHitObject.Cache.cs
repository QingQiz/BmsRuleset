using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Components;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public partial class DrawableBmsHitObject
{
    protected readonly record struct LayoutMetrics(
        int Column,
        BmsLayoutVariant LayoutVariant,
        Drawable? ColumnContainer,
        float ParentHeight,
        float ScaledParentWidth,
        double CurrentScrollPosition);

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
            var col = LayoutRef?.Column ?? 0;
            var variant = LayoutRef?.LayoutVariant ?? BmsLayoutVariant.Bme7K;
            var container = LayoutRef?.ColumnContainer;

            return new LayoutMetrics(col, variant, container,
                parentHeight,
                Math.Max(1, scaledParentWidth),
                Playfield?.CurrentScrollPosition ?? currentTime);
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
