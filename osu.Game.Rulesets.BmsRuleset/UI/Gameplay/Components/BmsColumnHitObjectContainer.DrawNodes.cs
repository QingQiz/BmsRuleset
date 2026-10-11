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

    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch
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
    internal readonly record struct SpriteSnapshot(Drawables.BmsResolvedNoteSprite Sprite, Texture? Texture, IShader Shader,
                                                    Quad Quad, DrawColourInfo Colour, RectangleF Rectangle, RectangleF TextureCoords);

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
        private bool batching;

        public override bool AddChildDrawNodes
        {
            get
            {
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

        protected override void Draw(IRenderer renderer)
        {
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
    }
}
