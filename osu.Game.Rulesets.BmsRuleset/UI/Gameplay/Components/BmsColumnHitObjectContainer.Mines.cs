using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer
{
    private MineSkinTemplate? mineTemplate;
    private BmsHitObjectLifetimeEntry[] mineEntries = [];
    private readonly HashSet<double> nativeMineTimes = [];
    private bool mineTimesDirty = true;

    internal bool BatchMinesActive { get; private set; }

    protected override int Compare(Drawable x, Drawable y)
    {
        // A skin can activate notes before the template is added. Keep the template
        // separate so insertion-order comparisons cannot contradict note-time ordering.
        if (x is MineSkinTemplate) return y is MineSkinTemplate ? base.Compare(x, y) : -1;
        if (y is MineSkinTemplate) return 1;

        return base.Compare(x, y);
    }

    internal void InitializeMineBatch(BmsColumn column)
    {
        mineEntries = Entries.OfType<BmsHitObjectLifetimeEntry>().Where(e => e.HitObject is BmsLandmine).ToArray();
        // Small sets already fit the ordinary pool. Dense fields amortise a shared
        // skin template and avoid binding the full framework tree for every mine.
        if (mineEntries.Length < 128) return;

        AddInternal(mineTemplate = new MineSkinTemplate(column));
    }

    private void updateMineBatch()
    {
        if (mineTemplate == null) return;

        // Resolve a queued skin change before deciding which entries need native
        // representations. A static-to-animated change must not hide mines for a frame.
        mineTemplate.Visual.PreparePendingSkin();
        var active = !Masking && !HasProxy && !ForceLocalVertexBatch && DrawColourInfo.Colour.HasSingleColour
                     && mineTemplate.Sprite is { IsLoaded: true, HasProxy: false, IsProxy: false } sprite
                     && sprite.EdgeSmoothness == Vector2.Zero;
        if (active == BatchMinesActive && !mineTimesDirty) return;

        if (mineTimesDirty)
        {
            mineEntries = Entries.OfType<BmsHitObjectLifetimeEntry>().Where(e => e.HitObject is BmsLandmine).ToArray();
            nativeMineTimes.Clear();
            foreach (var entry in Entries)
            {
                if (entry.HitObject is not BmsLandmine) nativeMineTimes.Add(entry.HitObject.StartTime);
            }
        }

        BatchMinesActive = active;
        foreach (var entry in mineEntries)
        {
            // Equal-time native children use activation order as a tie-breaker.
            // Retain those few mines so batching never changes that ordering.
            entry.SetBatchedMine(active && !nativeMineTimes.Contains(entry.HitObject.StartTime));
        }

        foreach (var drawable in AliveEntries.Values)
            drawable.Invalidate(Invalidation.DrawNode);
        mineTimesDirty = false;
        Invalidate(Invalidation.DrawNode);
    }

    internal readonly record struct MineSnapshot(BmsLandmine Note, double StartTime, Vector2 Offset, float Alpha);

    private void captureMines(List<MineSnapshot> target, out SpriteSnapshot template)
    {
        target.Clear();
        template = default;
        if (!BatchMinesActive || mineTemplate?.Sprite is not { } sprite) return;

        var texture = sprite.Texture;
        var rectangle = sprite.DrawRectangle;
        var coords = rectangle.RelativeIn(sprite.DrawTextureRectangle);
        if (texture != null) coords *= new Vector2(texture.DisplayWidth, texture.DisplayHeight);
        var quad = sprite.ScreenSpaceDrawQuad;
        template = new SpriteSnapshot(sprite, texture, sprite.TextureShader, quad, sprite.DrawColourInfo, rectangle, coords);
        var basis = DrawInfo.Matrix.Row1.Xy;
        var frame = visualFrame();
        var now = Time.Current;
        for (var i = scrollEntryFirst; i < scrollEntryEnd; i++)
        {
            var entry = scrollEntries[i];
            if (!entry.IsBatchedMine || entry.HitObject is not BmsLandmine mine || now >= mine.StartTime + BmsHitObjectLifetimeEntry.MINE_PAST_LIFETIME
                || !scrollController.IsTimelineVisible(mine.StartTime)
                || Judgements.GetLandmineState(mine).HandledTime.HasValue)
                continue;

            var position = scrollController.ConstantScrollActive ? mine.StartTime : mine.ScrollPositionAtStartTime;
            var y = frame.YAt(position);
            var padding = Math.Max(frame.Height, mineTemplate.Visual.VisualHeight * mineTemplate.Column.NoteHeightScale);
            if (y < -frame.Height - padding || y > padding) continue;

            var alpha = scrollController.SoftConstantActive ? scrollController.GetSoftConstantAlpha(mine.StartTime) : 1;
            if (alpha <= 0) continue;

            target.Add(new MineSnapshot(mine, mine.StartTime, basis * y, alpha));
        }

        target.Sort(static (a, b) => b.StartTime.CompareTo(a.StartTime));
    }

    private sealed partial class MineSkinTemplate : CompositeDrawable
    {
        internal BmsColumn Column { get; }

        internal BmsNoteVisual Visual { get; }

        internal BmsResolvedNoteSprite? Sprite => Visual.Drawable as BmsResolvedNoteSprite;

        internal MineSkinTemplate(BmsColumn column)
        {
            Column = column;
            Anchor = Origin = Anchor.BottomLeft;
            RelativeSizeAxes = Axes.X;
            Width = 1;
            InternalChild = Visual = new BmsNoteVisual(BmsSkinComponents.Mine, column.LayoutVariant, column.Index);
        }

        protected override void Update()
        {
            base.Update();
            Visual.Scale = new Vector2(1, Column.NoteHeightScale);
        }

        protected override bool CanBeFlattened => false;

        protected override DrawNode CreateDrawNode() => new TemplateDrawNode(this);

        private sealed class TemplateDrawNode(MineSkinTemplate source) : CompositeDrawableDrawNode(source)
        {
            public override bool AddChildDrawNodes => false;

            protected override bool CanDrawOpaqueInterior => false;
        }
    }
}
