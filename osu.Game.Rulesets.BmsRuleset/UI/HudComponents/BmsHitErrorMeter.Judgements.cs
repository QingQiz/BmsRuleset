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
using osu.Framework.Utils;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public partial class BmsHitErrorMeter
{
    internal readonly record struct JudgementLine(float X, SRGBColour Colour, bool? PoorIsFast, double FadeStart, double FadeEnd)
    {
        public double LifetimeEnd => FadeEnd + 1;

        public float AlphaAt(double time) => Interpolation.ValueAt(Math.Clamp(time, FadeStart, FadeEnd), 0.4f, 0, FadeStart, FadeEnd);
    }

    internal sealed partial class JudgementsLayer(BmsHitErrorMeter meter) : Drawable, ITexturedShaderDrawable
    {
        private readonly List<JudgementLine> lines = [];
        private Texture texture = null!;
        private int preparedCount;
        private double nextExpiry = double.PositiveInfinity;

        internal IReadOnlyList<JudgementLine> Lines => lines;

        private float lineThickness => meter.JudgementLineThickness.Value;

        internal double DisplayTime { get; private set; } = double.NegativeInfinity;

        public IShader TextureShader { get; private set; } = null!;

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture = renderer.WhitePixel;
            TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
            Blending = BlendingParameters.Additive;
        }

        public void AddJudgement(float x, SRGBColour colour, bool? poorIsFast)
            => lines.Add(new JudgementLine(x, colour, poorIsFast, double.NaN, double.NaN));

        public void UpdatePoorPositions(float fast, float slow)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].PoorIsFast is { } isFast) lines[i] = lines[i] with { X = isFast ? fast : slow };
            }

            Invalidate(Invalidation.DrawNode);
        }

        public void Clear()
        {
            lines.Clear();
            preparedCount = 0;
            nextExpiry = double.PositiveInfinity;
            Invalidate(Invalidation.DrawNode);
        }

        protected override void Update()
        {
            base.Update();
            if (lines.Count == 0)
                return;

            var now = Time.Current;
            var interval = Math.Min(8, meter.JudgementFadeDuration.Value * 1000 / 60);
            if (preparedCount == lines.Count && now >= DisplayTime && now - DisplayTime < interval)
                return;

            for (var i = preparedCount; i < lines.Count; i++)
            {
                var end = now + meter.JudgementFadeDuration.Value * 1000;
                lines[i] = lines[i] with { FadeStart = now, FadeEnd = end };
                nextExpiry = Math.Min(nextExpiry, end + 1);
            }

            // Fade durations can change between judgements, so expiry order need not match input order.
            if (now >= nextExpiry)
            {
                nextExpiry = double.PositiveInfinity;
                var retained = 0;
                for (var i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (now >= line.LifetimeEnd)
                        continue;

                    lines[retained++] = line;
                    nextExpiry = Math.Min(nextExpiry, line.LifetimeEnd);
                }

                lines.RemoveRange(retained, lines.Count - retained);
            }

            preparedCount = lines.Count;
            DisplayTime = now;
            Invalidate(Invalidation.DrawNode);
        }

        // Lines at either end extend half their thickness beyond the timing domain.
        protected override Quad ComputeScreenSpaceDrawQuad()
            => ToScreenSpace(new RectangleF(-meter.JudgementLineThickness.Value / 2, 0,
                DrawWidth + meter.JudgementLineThickness.Value, DrawHeight));

        protected override DrawNode CreateDrawNode() => new JudgementsDrawNode(this);

        internal sealed class JudgementsDrawNode(JudgementsLayer source) : TexturedShaderDrawNode(source)
        {
            internal readonly record struct LineSnapshot(Quad Quad, SRGBColour Colour);

            internal readonly List<LineSnapshot> Lines = [];
            private readonly List<ColourInfo> gradients = [];
            private Texture texture = null!;
            private IVertexBatch<TexturedVertex2D>? quadBatch;

            public override void ApplyState()
            {
                base.ApplyState();
                texture = source.texture;
                Lines.Clear();
                gradients.Clear();
                var width = source.DrawWidth;
                var height = source.DrawHeight;
                if (width <= 0 || height <= 0)
                    return;

                var thickness = source.lineThickness;
                var parentColour = DrawColourInfo.Colour;
                var hasGradient = !parentColour.HasSingleColour;
                // A burst can add thousands of lines between snapshots; reserve once
                // and keep the usual single colour out of the four-corner storage.
                Lines.EnsureCapacity(source.lines.Count);
                if (hasGradient)
                    gradients.EnsureCapacity(source.lines.Count);
                foreach (var line in source.lines)
                {
                    var alpha = line.AlphaAt(source.DisplayTime);
                    if (!(alpha > 0.0001f))
                        continue;

                    var rectangle = new RectangleF(line.X * width - thickness / 2, 0, thickness, height);
                    var colour = parentColour;
                    if (hasGradient)
                        colour = colour.Interpolate(Quad.FromRectangle(new RectangleF(rectangle.X / width, 0, thickness / width, 1)));
                    colour.ApplyChild(ColourInfo.SingleColour(line.Colour).MultiplyAlpha(alpha));
                    Lines.Add(new LineSnapshot(Quad.FromRectangle(rectangle) * DrawInfo.Matrix, colour.TopLeft));
                    if (hasGradient)
                        gradients.Add(colour);
                }
            }

            internal ColourInfo GetLineColour(int index)
                => gradients.Count == 0 ? ColourInfo.SingleColour(Lines[index].Colour) : gradients[index];

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);
                if (Lines.Count == 0 || !texture.Available)
                    return;

                // Dense timing bursts overflow the framework's 100-quad default batch.
                // Larger buffers reduce driver submissions without merging any lines.
                if (Lines.Count > 100)
                    quadBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(2048, 2);

                BindTextureShader(renderer);
                for (var i = 0; i < Lines.Count; i++)
                    renderer.DrawQuad(texture, Lines[i].Quad, GetLineColour(i), vertexAction: quadBatch?.AddAction);
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
