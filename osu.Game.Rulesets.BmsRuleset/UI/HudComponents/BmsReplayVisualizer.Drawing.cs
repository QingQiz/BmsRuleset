using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osuTK;

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

    internal readonly record struct PlotRectangle(
        RectangleF Bounds,
        Colour4 Colour,
        bool Outline = false,
        float Opacity = 1,
        PlotElementKind Kind = PlotElementKind.Note);

    internal sealed partial class PlotLayer(Action? prepareGeometry = null) : Drawable, ITexturedShaderDrawable
    {
        internal Action? InspectGeometry;
        private readonly List<PlotRectangle> rectangles = [];
        private Texture texture = null!;
        private PlotShaderManager? plotShaders;
        private int quadCount;
        private long geometryVersion;

        internal IReadOnlyList<PlotRectangle> Rectangles
        {
            get
            {
                prepareGeometry?.Invoke();
                InspectGeometry?.Invoke();
                return rectangles;
            }
        }

        internal long GeometryVersion
        {
            get
            {
                prepareGeometry?.Invoke();
                return geometryVersion;
            }
        }

        private void prepare() => prepareGeometry?.Invoke();

        public IShader TextureShader { get; private set; } = null!;

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture = renderer.WhitePixel;
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            plotShaders = new PlotShaderManager(renderer, shaders);
            timelineShader = plotShaders.Load("BmsReplayTimeline", "BmsReplayTimeline");
        }

        public void Clear(bool invalidate = true)
        {
            rectangles.Clear();
            quadCount = 0;
            if (invalidate)
                Invalidate(Invalidation.DrawNode);
        }

        public void Add(PlotRectangle rectangle)
        {
            rectangles.Add(rectangle);
            quadCount += rectangle.Outline ? 5 : 1;
        }

        public void Commit(bool invalidate = true)
        {
            geometryVersion++;
            if (invalidate)
                Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new PlotDrawNode(this);

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            plotShaders?.Dispose();
        }

        private sealed partial class PlotShaderManager(IRenderer renderer, ShaderManager frameworkShaders) : ShaderManager(renderer, new ResourceStore<byte[]>())
        {
            public override byte[]? GetRawData(string fileName) => fileName switch
            {
                "sh_BmsReplayTimeline.vs" => timeline_vertex,
                "sh_BmsReplayTimeline.fs" => timeline_fragment,
                _ => frameworkShaders.GetRawData(fileName),
            };

            public override IShaderPart? GetCachedShaderPart(string name) => base.GetCachedShaderPart(name) ?? frameworkShaders.GetCachedShaderPart(name);
        }

        internal sealed partial class PlotDrawNode(PlotLayer source) : TexturedShaderDrawNode(source)
        {
            internal readonly record struct RectangleSnapshot(RectangleF Bounds, SRGBColour Colour)
            {
                internal Quad Quad => Quad.FromRectangle(Bounds);
            }

            internal readonly List<RectangleSnapshot> Rectangles = [];
            private readonly List<ColourInfo> gradients = [];
            private Texture texture = null!;
            private IVertexBatch<TexturedVertex2D>? quadBatch;

            internal Matrix3 Matrix => DrawInfo.Matrix;

            public override void ApplyState()
            {
                source.prepare();
                base.ApplyState();
                texture = source.texture;
                Rectangles.Clear();
                gradients.Clear();
                applyTimeline(source);
                if (source.Timeline != null)
                    return;

                // Parent gradients need piecewise interpolation across the note border and fill.
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
                SRGBColour childColour = tint;
                childColour.MultiplyAlpha(opacity);
                if (!hasGradient)
                {
                    Rectangles.Add(new RectangleSnapshot(bounds, colour.TopLeft * childColour));
                    return;
                }

                colour = colour.Interpolate(Quad.FromRectangle(new RectangleF(bounds.X / source.DrawWidth, bounds.Y / source.DrawHeight,
                    bounds.Width / source.DrawWidth, bounds.Height / source.DrawHeight)));
                colour.ApplyChild(ColourInfo.SingleColour(childColour));
                Rectangles.Add(new RectangleSnapshot(bounds, colour.TopLeft));
                gradients.Add(colour);
            }

            internal ColourInfo GetRectangleColour(int index)
                => gradients.Count == 0 ? ColourInfo.SingleColour(Rectangles[index].Colour) : gradients[index];

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (hasTimeline)
                {
                    drawTimeline(renderer);
                    return;
                }

                if (Rectangles.Count == 0 || !texture.Available)
                    return;

                // One layer keeps dense charts out of the scene graph and avoids a mask per note.
                if (Rectangles.Count > 100)
                    quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(2048, 2);

                // Dense snapshots keep local bounds instead of transforming every corner on
                // the CPU. Small plots retain the shared batch without a projection change.
                if (quadBatch != null)
                    renderer.PushLocalMatrix(DrawInfo.Matrix);
                BindTextureShader(renderer);
                if (quadBatch != null)
                    AddVertices(renderer, quadBatch.AddAction);
                else
                {
                    for (var i = 0; i < Rectangles.Count; i++)
                        renderer.DrawQuad(texture, Rectangles[i].Quad * DrawInfo.Matrix, GetRectangleColour(i));
                }

                UnbindTextureShader(renderer);
                if (quadBatch != null)
                    renderer.PopLocalMatrix();
            }

            internal void AddVertices(IRenderer renderer, Action<TexturedVertex2D> addVertex)
            {
                if (!renderer.BindTexture(texture))
                    return;

                // Resolve the white atlas texel once per batch rather than once per rectangle.
                var coordinates = texture.GetTextureRect();
                var bounds = coordinates;
                const int padding = (1 << IRenderer.MAX_MIPMAP_LEVELS) / 2;
                bounds = bounds.Inflate(new Vector2(
                    renderer.CurrentWrapModeS == WrapMode.ClampToEdge ? padding / (float)texture.Width : 0,
                    renderer.CurrentWrapModeT == WrapMode.ClampToEdge ? padding / (float)texture.Height : 0));
                var vertex = new TexturedVertex2D(renderer)
                {
                    TextureRect = new Vector4(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                };
                var rectangles = CollectionsMarshal.AsSpan(Rectangles);
                for (var i = 0; i < rectangles.Length; i++)
                {
                    ref readonly var rectangle = ref rectangles[i];
                    vertex.Colour = rectangle.Colour.SRGB;
                    if (gradients.Count > 0)
                        vertex.Colour = gradients[i].BottomLeft.SRGB;
                    vertex.Position = rectangle.Bounds.BottomLeft;
                    vertex.TexturePosition = new Vector2(coordinates.Left, coordinates.Bottom);
                    addVertex(vertex);

                    if (gradients.Count > 0)
                        vertex.Colour = gradients[i].BottomRight.SRGB;
                    vertex.Position = rectangle.Bounds.BottomRight;
                    vertex.TexturePosition = new Vector2(coordinates.Right, coordinates.Bottom);
                    addVertex(vertex);

                    if (gradients.Count > 0)
                        vertex.Colour = gradients[i].TopRight.SRGB;
                    vertex.Position = rectangle.Bounds.TopRight;
                    vertex.TexturePosition = new Vector2(coordinates.Right, coordinates.Top);
                    addVertex(vertex);

                    if (gradients.Count > 0)
                        vertex.Colour = gradients[i].TopLeft.SRGB;
                    vertex.Position = rectangle.Bounds.TopLeft;
                    vertex.TexturePosition = new Vector2(coordinates.Left, coordinates.Top);
                    addVertex(vertex);
                }
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                quadBatch?.Dispose();
                timelineParameters?.Dispose();
            }
        }
    }
}
