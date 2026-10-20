using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

internal sealed partial class BmsHitExplosionLayer : Container
{
    internal readonly record struct Pulse(double StartTime, BmsHitLightStyle Style, float Offset);

    private readonly List<Pulse> pulses = [];

    internal IReadOnlyList<Pulse> Pulses => pulses;

    internal void ResetReplayPulses()
    {
        pulses.Clear();
        foreach (var child in InternalChildren)
            if (child is BmsHitExplosion) child.Expire();
        previousCount = 0;
        Invalidate(Invalidation.DrawNode);
    }

    private readonly SkinTemplate normal;
    private readonly SkinTemplate hold;
    private readonly Action<BmsHitLightStyle, float, double?> spawnNative;

    internal BmsHitExplosionLayer(BmsLayoutVariant layout, int column, Action<BmsHitLightStyle, float, double?> spawnNative)
    {
        this.spawnNative = spawnNative;
        RelativeSizeAxes = Axes.Y;
        InternalChildren =
        [
            normal = new SkinTemplate(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, layout, column)),
            hold = new SkinTemplate(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, layout, column, true)),
        ];
    }

    protected override bool CanBeFlattened => false;

    internal bool TryTrigger(BmsHitLightStyle style, float offset)
    {
        if (!canBatch())
        {
            restoreNativePulses();
            return false;
        }

        // Native pool preparation starts the envelope at its first traversal, which
        // can be one frame after the judgement callback that requested this pulse.
        pulses.Add(new Pulse(double.NaN, style, offset));
        return true;
    }

    private bool canBatch()
    {
        if (!IsLoaded) return false;

        normal.PreparePendingSkin();
        hold.PreparePendingSkin();
        return !Masking && !HasProxy && !ForceLocalVertexBatch && DrawColourInfo.Colour.HasSingleColour
               && InternalChildren.Count == 2 && normal.Sprite is { IsLoaded: true } && hold.Sprite is { IsLoaded: true };
    }

    protected override bool CheckChildrenLife()
    {
        if (pulses.Count > 0 && !canBatch()) restoreNativePulses();
        return base.CheckChildrenLife();
    }

    private void restoreNativePulses()
    {
        foreach (var pulse in pulses)
        {
            if (double.IsNaN(pulse.StartTime)) spawnNative(pulse.Style, pulse.Offset, null);
            else if (Time.Current >= pulse.StartTime && Time.Current < pulse.StartTime + 200)
                spawnNative(pulse.Style, pulse.Offset, pulse.StartTime);
        }

        pulses.Clear();
    }

    protected override void Update()
    {
        base.Update();
        var count = 0;
        for (var i = 0; i < pulses.Count; i++)
        {
            var pulse = pulses[i];
            if (double.IsNaN(pulse.StartTime)) pulse = pulse with { StartTime = Time.Current };
            if (Time.Current < pulse.StartTime || Time.Current >= pulse.StartTime + 200) continue;

            pulses[count++] = pulse;
        }

        pulses.RemoveRange(count, pulses.Count - count);
        if (pulses.Count > 0 || count != previousCount) Invalidate(Invalidation.DrawNode);
        previousCount = count;
    }

    private int previousCount;

    protected override DrawNode CreateDrawNode() => new PulseDrawNode(this);

    internal readonly record struct SpriteState(Texture? Texture, IShader Shader, Quad Quad, DrawColourInfo Colour, RectangleF Coords);

    private static SpriteState snapshot(Sprite sprite)
    {
        var coords = sprite.DrawRectangle.RelativeIn(sprite.DrawTextureRectangle);
        if (sprite.Texture != null) coords *= new Vector2(sprite.Texture.DisplayWidth, sprite.Texture.DisplayHeight);
        return new SpriteState(sprite.Texture, sprite.TextureShader, sprite.ScreenSpaceDrawQuad, sprite.DrawColourInfo, coords);
    }

    private sealed class PulseDrawNode(BmsHitExplosionLayer source) : CompositeDrawableDrawNode(source)
    {
        internal readonly List<Pulse> Pulses = [];
        internal SpriteState Normal;
        internal SpriteState Hold;
        private double time;
        private Vector2 vertical;

        public override void ApplyState()
        {
            base.ApplyState();
            time = source.Time.Current;
            vertical = source.DrawInfo.Matrix.Row1.Xy;
            Pulses.Clear();
            Pulses.AddRange(source.pulses);
            if (source.normal.Sprite is { } normalSprite) Normal = snapshot(normalSprite);
            if (source.hold.Sprite is { } holdSprite) Hold = snapshot(holdSprite);
        }

        protected override void Draw(IRenderer renderer)
        {
            base.Draw(renderer);
            IShader? shader = null;
            foreach (var pulse in Pulses)
            {
                if (double.IsNaN(pulse.StartTime)) continue;

                var sprite = pulse.Style == BmsHitLightStyle.Hold ? Hold : Normal;
                if (sprite.Texture?.Available != true) continue;

                if (shader != sprite.Shader)
                {
                    shader?.Unbind();
                    shader = sprite.Shader;
                    shader.Bind();
                }

                var offset = -vertical * pulse.Offset;
                var quad = new Quad(sprite.Quad.TopLeft + offset, sprite.Quad.TopRight + offset,
                    sprite.Quad.BottomLeft + offset, sprite.Quad.BottomRight + offset);
                renderer.SetBlend(sprite.Colour.Blending);
                renderer.DrawQuad(sprite.Texture, quad, sprite.Colour.Colour.MultiplyAlpha(BmsHitExplosion.AlphaAt(time - pulse.StartTime)), textureCoords: sprite.Coords);
            }

            shader?.Unbind();
        }
    }

    private sealed partial class SkinTemplate : BmsCachedSkinnableDrawable
    {
        internal SkinTemplate(BmsSkinComponentLookup lookup)
            : base(lookup)
        {
            ComponentAnchor = null;
        }

        internal Sprite? Sprite => (Drawable as LegacyBmsHitExplosion)?.StaticSprite;

        protected override bool CanBeFlattened => false;

        protected override DrawNode CreateDrawNode() => new TemplateDrawNode(this);

        private sealed class TemplateDrawNode(SkinTemplate source) : CompositeDrawableDrawNode(source)
        {
            public override bool AddChildDrawNodes => false;

            protected override bool CanDrawOpaqueInterior => false;
        }
    }
}
