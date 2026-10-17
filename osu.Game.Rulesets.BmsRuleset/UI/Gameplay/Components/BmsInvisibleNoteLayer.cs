using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal sealed partial class BmsInvisibleNoteLayer(BmsInvisibleNote[] notes, BmsGameplayScrollController scroll, BmsColumn column)
    : Drawable, ITexturedShaderDrawable
{
    private readonly BmsInvisibleNote[] scrollNotes = notes.OrderBy(n => n.ScrollPositionAtStartTime).ToArray();
    private readonly List<Marker> markers = [];
    private Texture texture = null!;

    public IShader TextureShader { get; private set; } = null!;

    internal readonly record struct Marker(float Y, float Alpha);

    internal IReadOnlyList<Marker> Markers => markers;

    internal float MarkerHeight => 8 * column.NoteHeightScale;

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer, ShaderManager shaders)
    {
        texture = renderer.WhitePixel;
        TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
    }

    protected override void Update()
    {
        base.Update();
        markers.Clear();
        if (Alpha == 0)
            return;

        var height = DrawHeight;
        var markerHeight = MarkerHeight;
        var frame = new BmsColumnVisualFrame(scroll.CurrentScrollPosition, scroll.ScrollCoordinateScale(height, column.HitTargetPosition),
            column.HitTargetPosition, height);
        var ordered = scroll.ConstantScrollActive ? notes : scrollNotes;
        var first = 0;
        var end = ordered.Length;
        if (frame.Scale != 0 && double.IsFinite(frame.Scale))
        {
            var top = frame.ScrollPosition + (height - frame.HitTarget) / frame.Scale;
            var bottom = frame.ScrollPosition - (markerHeight + frame.HitTarget) / frame.Scale;
            first = bound(Math.Min(top, bottom), false);
            end = bound(Math.Max(top, bottom), true);
        }

        for (var i = first; i < end; i++)
        {
            var note = ordered[i];
            if (Time.Current > note.StartTime || !scroll.IsTimelineVisible(note.StartTime))
                continue;

            var alpha = scroll.GetSoftConstantAlpha(note.StartTime);
            if (alpha <= 0)
                continue;

            var y = frame.YAt(position(note)) + height;
            if (y >= 0 && y - markerHeight <= height)
                markers.Add(new Marker(y, alpha));
        }

        // Each draw-buffer slot copies only visible markers; drawing never reads the live list.
        Invalidate(Invalidation.DrawNode);

        int bound(double value, bool upper)
        {
            var low = 0;
            var high = ordered.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var candidate = position(ordered[middle]);
                if (candidate < value || (upper && candidate == value))
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }

        double position(BmsInvisibleNote note) => scroll.ConstantScrollActive ? note.StartTime : note.ScrollPositionAtStartTime;
    }

    protected override DrawNode CreateDrawNode() => new MarkerDrawNode(this);

    private sealed class MarkerDrawNode(BmsInvisibleNoteLayer source) : TexturedShaderDrawNode(source)
    {
        private readonly List<Marker> markers = [];
        private Texture texture = null!;
        private float width;
        private float height;

        public override void ApplyState()
        {
            base.ApplyState();
            texture = source.texture;
            width = source.DrawWidth;
            height = source.MarkerHeight;
            markers.Clear();
            markers.AddRange(source.markers);
        }

        protected override void Draw(IRenderer renderer)
        {
            base.Draw(renderer);
            if (markers.Count == 0 || width <= 0 || height <= 0 || !texture.Available)
                return;

            BindTextureShader(renderer);
            var side = Math.Min(2, width / 2);
            var horizontal = height / 4;
            foreach (var marker in markers)
            {
                var y = marker.Y - height;
                var colour = DrawColourInfo.Colour.MultiplyAlpha(marker.Alpha);
                draw(new RectangleF(0, y, width, horizontal));
                draw(new RectangleF(0, marker.Y - horizontal, width, horizontal));
                draw(new RectangleF(0, y + horizontal, side, height - 2 * horizontal));
                draw(new RectangleF(width - side, y + horizontal, side, height - 2 * horizontal));

                void draw(RectangleF rectangle)
                    => renderer.DrawQuad(texture, Quad.FromRectangle(rectangle) * DrawInfo.Matrix, colour);
            }

            UnbindTextureShader(renderer);
        }
    }
}
