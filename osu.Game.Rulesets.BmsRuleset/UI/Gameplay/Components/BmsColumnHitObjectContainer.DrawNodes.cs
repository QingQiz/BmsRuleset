using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer
{
    private bool spriteBatchHasMultiplePieces = true;

    protected override bool CanBeFlattened => !BatchMinesActive && !Masking && !HasProxy && !ForceLocalVertexBatch
                                              && (spriteBatchHasMultiplePieces || !firstPresentChildAllowsBatch());

    private bool firstPresentChildAllowsBatch()
    {
        // Wrapped and animated columns keep their original flattened path before an
        // extra composite node is generated. The full capture still validates every
        // child, so the first child only permits attempting a batch, never proves one.
        // The public list hides its struct enumerator behind IEnumerable. Indexing
        // keeps both this fallback check and snapshot collection allocation-free.
        var children = AliveInternalChildren;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (!child.IsLoaded || !child.IsPresent) continue;

            return !child.IsProxy && !child.HasProxy && child is DrawableBmsHitObject note
                   && note.SingleSpriteForBatch is { } sprite && !sprite.HasProxy && !sprite.IsProxy
                   && sprite.EdgeSmoothness == Vector2.Zero;
        }

        return false;
    }

    protected override DrawNode CreateDrawNode() => new SpriteBatchDrawNode(this);

    // Each framework draw-buffer slot owns its list. No mutable drawable state is
    // read while drawing, even when a pooled note has since been rebound or freed.
    internal readonly record struct SpriteSnapshot(
        Drawables.BmsResolvedNoteSprite Sprite,
        Texture? Texture,
        IShader Shader,
        Quad Quad,
        DrawColourInfo Colour,
        RectangleF Rectangle,
        RectangleF TextureCoords);

    private bool captureSpriteBatch(List<SpriteSnapshot> target)
    {
        target.Clear();
        if (Masking || HasProxy || ForceLocalVertexBatch)
            return false;

        var children = AliveInternalChildren;
        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            if (child.IsProxy) return false;

            if (!child.IsLoaded || !child.IsPresent) continue;

            if (child.HasProxy || child is not DrawableBmsHitObject note || note.SingleSpriteForBatch is not { } sprite
                || sprite.HasProxy || sprite.IsProxy || sprite.EdgeSmoothness != Vector2.Zero)
                return false;

            // Ancestor masking can deliberately skip the leaf's update. Reuse the
            // result recorded by the standard leaf callback rather than recomputing it.
            if (!sprite.IsPresent || sprite.IsMaskedForBatch) continue;

            var colour = sprite.DrawColourInfo;
            var texture = sprite.Texture;
            // The standard node owns per-leaf opaque depth and conservative bounds.
            // Keep that path when an opaque interior can participate in the pre-pass.
            if (texture?.Opacity == Opacity.Opaque && colour.Colour.MinAlpha == 1
                                                   && colour.Blending == BlendingParameters.Mixture && colour.Colour.HasSingleColour)
                return false;

            var quad = sprite.ScreenSpaceDrawQuad;
            var rectangle = sprite.DrawRectangle;
            var coords = rectangle.RelativeIn(sprite.DrawTextureRectangle);
            if (texture != null) coords *= new Vector2(texture.DisplayWidth, texture.DisplayHeight);
            target.Add(new SpriteSnapshot(sprite, texture, sprite.TextureShader, quad, colour, rectangle, coords));
        }

        return true;
    }

    private sealed class SpriteBatchDrawNode(BmsColumnHitObjectContainer source) : CompositeDrawableDrawNode(source)
    {
        private readonly List<SpriteSnapshot> sprites = [];
        internal readonly List<MineSnapshot> Mines = [];
        private bool batching;
        private bool interleavedMines;
        private readonly List<DrawNode> mineChildren = [];
        private readonly List<MineRunDrawNode> mineRuns = [];

        public override bool AddChildDrawNodes
        {
            get
            {
                interleavedMines = source.BatchMinesActive;
                if (interleavedMines)
                {
                    source.captureMines(Mines, out var template);
                    // Snapshot runs on the update thread, before the renderer merges
                    // them with the framework-generated children in this buffer.
                    while (mineRuns.Count < source.AliveEntries.Count + 1)
                        mineRuns.Add(new MineRunDrawNode(source, Mines));
                    foreach (var run in mineRuns)
                    {
                        run.Sprite = template;
                        run.ApplyState();
                    }

                    batching = false;
                    sprites.Clear();
                    return true;
                }

                Mines.Clear();
                // Child position invalidations need not dirty this container's own node.
                // The framework calls this boundary for every generated buffer snapshot.
                batching = source.captureSpriteBatch(sprites);
                if (!batching)
                {
                    sprites.Clear();
                    return true;
                }

                Children?.Clear();
                return false;
            }
        }

        protected override bool CanDrawOpaqueInterior => !batching && base.CanDrawOpaqueInterior;

        protected override void DrawOpaqueInterior(IRenderer renderer)
        {
            if (!interleavedMines)
            {
                base.DrawOpaqueInterior(renderer);
                return;
            }

            prepareMineChildren();
            var children = Children;
            Children = mineChildren;
            base.DrawOpaqueInterior(renderer);
            Children = children;
        }

        protected override void Draw(IRenderer renderer)
        {
            if (interleavedMines)
            {
                drawInterleaved(renderer);
                return;
            }

            base.Draw(renderer);
            if (!batching) return;

            IShader? boundShader = null;
            foreach (var sprite in sprites)
            {
                if (sprite.Texture?.Available != true || sprite.Rectangle.Width == 0 || sprite.Rectangle.Height == 0) continue;

                if (boundShader != sprite.Shader)
                {
                    boundShader?.Unbind();
                    boundShader = sprite.Shader;
                    boundShader.Bind();
                }

                renderer.SetBlend(sprite.Colour.Blending);
                renderer.DrawQuad(sprite.Texture, sprite.Quad, sprite.Colour.Colour, textureCoords: sprite.TextureCoords);
            }

            boundShader?.Unbind();
        }

        private void drawInterleaved(IRenderer renderer)
        {
            // Each mine run participates in the framework's depth traversal between
            // native notes. Skipping that pass would leave their descendants at stale depths.
            prepareMineChildren();
            var children = Children;
            Children = mineChildren;
            base.Draw(renderer);
            Children = children;
        }

        private void prepareMineChildren()
        {
            mineChildren.Clear();
            var mine = 0;
            var run = 0;
            if (Children != null)
            {
                foreach (var child in Children)
                {
                    if (child is IBmsHitObjectDrawNode note)
                        addUntil(note.StartTime);
                    mineChildren.Add(child);
                }
            }

            addUntil(double.NegativeInfinity);

            void addUntil(double time)
            {
                var first = mine;
                while (mine < Mines.Count && Mines[mine].StartTime > time)
                    mine++;
                if (mine == first) return;

                var node = mineRuns[run++];
                node.First = first;
                node.End = mine;
                mineChildren.Add(node);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            foreach (var node in mineRuns) node.Dispose();
            base.Dispose(isDisposing);
        }

        private sealed class MineRunDrawNode(BmsColumnHitObjectContainer owner, List<MineSnapshot> mines) : DrawNode(owner)
        {
            internal readonly List<MineSnapshot> Mines = mines;
            internal SpriteSnapshot Sprite;
            internal int First;
            internal int End;

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                var sprite = Sprite;
                if (First == End || sprite.Texture?.Available != true || sprite.Rectangle.Width == 0 || sprite.Rectangle.Height == 0) return;

                sprite.Shader.Bind();
                renderer.SetBlend(sprite.Colour.Blending);
                for (var i = First; i < End; i++)
                {
                    var mine = Mines[i];
                    var quad = new Quad(sprite.Quad.TopLeft + mine.Offset, sprite.Quad.TopRight + mine.Offset,
                        sprite.Quad.BottomLeft + mine.Offset, sprite.Quad.BottomRight + mine.Offset);
                    renderer.DrawQuad(sprite.Texture, quad, sprite.Colour.Colour.MultiplyAlpha(mine.Alpha), textureCoords: sprite.TextureCoords);
                }

                sprite.Shader.Unbind();
            }
        }
    }
}
