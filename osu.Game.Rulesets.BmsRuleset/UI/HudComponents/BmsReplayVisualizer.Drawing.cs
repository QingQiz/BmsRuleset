using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsReplayVisualizer
{
    internal enum PlotElementKind
    {
        Note,
        Input,
        Connection,
        Judgement,
    }

    internal readonly record struct PlotRectangle(RectangleF Bounds, Colour4 Colour, bool Outline = false, float Opacity = 1,
                                                 PlotElementKind Kind = PlotElementKind.Note);

    internal sealed partial class PlotLayer : Drawable, ITexturedShaderDrawable
    {
        private readonly List<PlotRectangle> rectangles = [];
        private Texture texture = null!;
        private int quadCount;

        internal IReadOnlyList<PlotRectangle> Rectangles => rectangles;

        internal long GeometryVersion { get; private set; }

        public IShader TextureShader { get; private set; } = null!;

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture = renderer.WhitePixel;
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
        }

        public void Clear()
        {
            rectangles.Clear();
            quadCount = 0;
            Invalidate(Invalidation.DrawNode);
        }

        public void Add(PlotRectangle rectangle)
        {
            rectangles.Add(rectangle);
            quadCount += rectangle.Outline ? 5 : 1;
        }

        public void Commit()
        {
            GeometryVersion++;
            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new PlotDrawNode(this);

        internal sealed class PlotDrawNode(PlotLayer source) : TexturedShaderDrawNode(source)
        {
            internal readonly record struct RectangleSnapshot(Quad Quad, SRGBColour Colour);

            internal readonly List<RectangleSnapshot> Rectangles = [];
            private readonly List<ColourInfo> gradients = [];
            private Texture texture = null!;
            private IVertexBatch<TexturedVertex2D>? quadBatch;

            public override void ApplyState()
            {
                base.ApplyState();
                texture = source.texture;
                Rectangles.Clear();
                gradients.Clear();
                if (source.DrawWidth <= 0 || source.DrawHeight <= 0)
                    return;

                Rectangles.EnsureCapacity(source.quadCount);
                if (!DrawColourInfo.Colour.HasSingleColour)
                    gradients.EnsureCapacity(source.quadCount);
                foreach (var rectangle in source.rectangles)
                {
                    var bounds = rectangle.Bounds;
                    if (!rectangle.Outline)
                    {
                        addQuad(bounds, rectangle.Colour, rectangle.Opacity);
                        continue;
                    }

                    var border = Math.Min(1, Math.Min(bounds.Width, bounds.Height) / 2);
                    addQuad(new RectangleF(bounds.X + border, bounds.Y + border, bounds.Width - border * 2, bounds.Height - border * 2),
                        rectangle.Colour, rectangle.Opacity * 0.08f);
                    addQuad(new RectangleF(bounds.X, bounds.Y, bounds.Width, border), rectangle.Colour, rectangle.Opacity);
                    addQuad(new RectangleF(bounds.X, bounds.Bottom - border, bounds.Width, border), rectangle.Colour, rectangle.Opacity);
                    addQuad(new RectangleF(bounds.X, bounds.Y + border, border, bounds.Height - border * 2), rectangle.Colour, rectangle.Opacity);
                    addQuad(new RectangleF(bounds.Right - border, bounds.Y + border, border, bounds.Height - border * 2), rectangle.Colour, rectangle.Opacity);
                }
            }

            private void addQuad(RectangleF bounds, Colour4 tint, float opacity)
            {
                if (bounds.Width <= 0 || bounds.Height <= 0 || opacity <= 0)
                    return;

                var colour = DrawColourInfo.Colour;
                var hasGradient = !colour.HasSingleColour;
                if (hasGradient)
                    colour = colour.Interpolate(Quad.FromRectangle(new RectangleF(bounds.X / source.DrawWidth, bounds.Y / source.DrawHeight,
                        bounds.Width / source.DrawWidth, bounds.Height / source.DrawHeight)));
                colour.ApplyChild(ColourInfo.SingleColour(tint).MultiplyAlpha(opacity));
                Rectangles.Add(new RectangleSnapshot(Quad.FromRectangle(bounds) * DrawInfo.Matrix, colour.TopLeft));
                if (hasGradient)
                    gradients.Add(colour);
            }

            internal ColourInfo GetRectangleColour(int index)
                => gradients.Count == 0 ? ColourInfo.SingleColour(Rectangles[index].Colour) : gradients[index];

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (Rectangles.Count == 0 || !texture.Available)
                    return;

                // One layer keeps dense charts out of the scene graph and avoids a mask per note.
                if (Rectangles.Count > 100)
                    quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(2048, 2);

                BindTextureShader(renderer);
                for (var i = 0; i < Rectangles.Count; i++)
                    renderer.DrawQuad(texture, Rectangles[i].Quad, GetRectangleColour(i), vertexAction: quadBatch?.AddAction);
                UnbindTextureShader(renderer);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                quadBatch?.Dispose();
            }
        }
    }
}
