#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void ColumnSpriteBatchPreservesFrameworkStateAndBufferIsolation(BmsTestSkins.SkinKind selectedSkin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readSource = typeof(DrawNode).GetProperty("Source", flags)!.GetMethod!.CreateDelegate<Func<DrawNode, IDrawable>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!.CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var forceBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var canFlatten = typeof(CompositeDrawable).GetProperty("CanBeFlattened", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, bool>>();
        var layer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var frame = 0UL;
        DrawableBmsHitObject note = null!;
        BmsColumnHitObjectContainer column = null!;
        BmsResolvedNoteSprite sprite = null!;
        var compared = 0;
        var references = 0;

        AddStep("load sprite batch player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze before notes", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(4800); });
        AddUntilStep("static note loaded", () =>
        {
            if (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) >= .001) return false;
            note = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().FirstOrDefault(n => n.HitObject.Column == 2)!;
            if (note?.SingleSpriteForBatch is not { IsLoaded: true } piece) return false;
            sprite = piece;
            column = (BmsColumnHitObjectContainer)note.Parent;
            return true;
        });
        AddStep("compare moving buffer snapshots", () =>
        {
            TestContext.WriteLine($"COLUMN_SPRITE_BATCH_DEFAULT opacity={sprite.Texture?.Opacity} alpha={sprite.DrawColourInfo.Colour.MinAlpha}");
            var defaultOpaque = sprite.Texture?.Opacity == Opacity.Opaque && sprite.DrawColourInfo.Colour.MinAlpha == 1;
            compare(expectBatch: !defaultOpaque);
            // Partial opacity disables the opaque pre-pass so this fixture exercises
            // the batch even if the synthetic skin happens to contain an opaque bitmap.
            note.Alpha = .9f;
            checkPreparationAllocations(expectBatch: true);
            for (var repeat = 0; repeat < 64; repeat++)
            {
                Playfield.Stage.Rotation = repeat / 4f;
                Playfield.Stage.Shear = new Vector2(.3f, -.2f);
                Playfield.Stage.Scale = repeat % 2 == 0 ? new Vector2(-1.2f, .7f) : Vector2.One;
                note.Y = -100.25f - repeat * 3.125f;
                compare(expectBatch: true);
            }
            foreach (var height in new[] { 0f, -1f, .00001f, 37f })
            {
                sprite.Height = height;
                compare(expectBatch: true);
            }
            sprite.Height = 12;
            sprite.TextureRelativeSizeAxes = Axes.None;
            sprite.TextureRectangle = new RectangleF(-2, 1, 23, 11);
            compare(expectBatch: true);
            sprite.TextureRelativeSizeAxes = Axes.Both;
            sprite.TextureRectangle = new RectangleF(.2f, -.1f, .6f, .8f);
            compare(expectBatch: true);
            sprite.Colour = ColourInfo.GradientHorizontal(Colour4.Red, Colour4.White);
            compare(expectBatch: true);
            sprite.Blending = BlendingParameters.Additive;
            compare(expectBatch: true);
            sprite.Blending = BlendingParameters.Inherit;
            sprite.Colour = Colour4.White;
            sprite.TextureRectangle = new RectangleF(0, 0, 1, 1);

            var savedNode = generate(column, ++frame, 0, true)!;
            var savedState = rendered(savedNode);
            var oldTexture = sprite.Texture;
            sprite.Texture = null;
            note.Y += 43;
            Playfield.UpdateSubTreeMasking();
            _ = generate(column, ++frame, 1, false);
            Assert.That(rendered(savedNode), Is.EqualTo(savedState), "Source changes leaked into a previous buffer.");
            sprite.Texture = oldTexture;
            compare(expectBatch: true);
            note.Y = 100000;
            compare(expectBatch: true);
            note.Y = -100;
            compare(expectBatch: true);
            sprite.Alpha = 0;
            compare(expectBatch: true);
            sprite.Alpha = 1;
            compare(expectBatch: true);

            sprite.EdgeSmoothness = new Vector2(2, 1);
            compare(expectBatch: false);
            sprite.EdgeSmoothness = Vector2.Zero;
            compare(expectBatch: true);
            note.ApplyNoteHeightScale(1.37f);
            compare(expectBatch: false, update: false);
            checkPreparationAllocations(expectBatch: false);
            note.ApplyNoteHeightScale(1);
            compare(expectBatch: true);
            ((BmsNoteVisual)layer.GetValue(note)!).Height = 3;
            compare(expectBatch: false);
            ((BmsNoteVisual)layer.GetValue(note)!).Height = 0;
            compare(expectBatch: true);
            var proxy = sprite.CreateProxy();
            compare(expectBatch: false);
            proxy.Dispose();
            TestContext.WriteLine($"COLUMN_SPRITE_BATCH comparisons={compared} referenceSprites={references} buffers=3");
        });
        AddStep("mixed columns retain original subtree", () =>
        {
            foreach (var mixed in Playfield.ChildrenOfType<BmsColumnHitObjectContainer>().Where(c => c != column))
            {
                var node = generate(mixed, ++frame, 0, true);
                if (node == null) continue;
                var batched = BmsColumnSpriteBatchSnapshot.Read(node);
                if (mixed.AliveObjects.Any(n => n.HitObject is BmsLongNote)) Assert.That(batched, Is.Null);
            }
        });

        void checkPreparationAllocations(bool expectBatch)
        {
            var node = (ICompositeDrawNode)generate(column, ++frame, 0, false)!;
            // Warm the existing buffers before measuring the framework's per-frame
            // eligibility and capture boundaries, including the wrapped fallback.
            for (var repeat = 0; repeat < 128; repeat++)
            {
                _ = canFlatten(column);
                _ = node.AddChildDrawNodes;
            }
            var flattened = true;
            var childrenAdded = true;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 128; repeat++)
            {
                flattened &= canFlatten(column);
                childrenAdded &= node.AddChildDrawNodes;
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            TestContext.WriteLine($"COLUMN_SPRITE_BATCH_ALLOCATIONS batch={expectBatch} calls=128 bytes={allocated}");
            Assert.That(flattened, Is.EqualTo(!expectBatch));
            Assert.That(childrenAdded, Is.EqualTo(!expectBatch));
            Assert.That(allocated, Is.Zero, "Stable draw preparation must reuse its buffers without allocating enumerators.");
        }

        void compare(bool expectBatch, bool update = true)
        {
            if (update) note.UpdateSubTree();
            Playfield.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
            {
                var node = generate(column, ++frame, slot, false)!;
                var before = rendered(node);
                var batch = BmsColumnSpriteBatchSnapshot.Read(node);
                Assert.That(batch != null, Is.EqualTo(expectBatch));
                if (expectBatch) Assert.That(((ICompositeDrawNode)node).Children, Is.Empty);
                forceBatch.SetValue(column, true);
                try
                {
                    var original = generate(column, ++frame, (slot + 1) % 3, true)!;
                    var reference = rendered(original);
                    Assert.That(reference, Is.EqualTo(before), $"Buffer {slot} differs from standard Sprite nodes.");
                    references += reference.Length;
                    Assert.That(rendered(node), Is.EqualTo(before), "Preparing another slot changed an older snapshot.");
                }
                finally { forceBatch.SetValue(column, false); }
                compared++;
            }
        }

        object[] rendered(DrawNode root)
        {
            var states = new List<object>();
            visit(root);
            return states.ToArray();

            void visit(DrawNode node)
            {
                if (BmsColumnSpriteBatchSnapshot.Read(node) is { } batch)
                {
                    states.AddRange(batch.Select(s => (object)((Sprite)s.Sprite, s.Texture, s.Quad, s.Colour,
                        (s.Rectangle.X, s.Rectangle.Y, s.Rectangle.Width, s.Rectangle.Height),
                        (s.TextureCoords.X, s.TextureCoords.Y, s.TextureCoords.Width, s.TextureCoords.Height), s.Shader)));
                    return;
                }
                if (node is SpriteDrawNode)
                {
                    var source = (Sprite)readSource(node);
                    var shader = (IShader)typeof(TexturedShaderDrawNode).GetField("textureShader", flags)!.GetValue(node)!;
                    var rectangle = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(node, "DrawRectangle");
                    var coords = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(node, "TextureCoords");
                    states.Add((source, BmsSpriteDrawNodeSnapshot.ReadTexture(node), BmsSpriteDrawNodeSnapshot.ReadQuad(node), readColour(node),
                        (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height),
                        (coords.X, coords.Y, coords.Width, coords.Height), shader));
                }
                if (node is ICompositeDrawNode { Children: { } children }) foreach (var child in children) visit(child);
            }
        }
    }
}

internal static class BmsColumnSpriteBatchSnapshot
{
    internal static BmsColumnHitObjectContainer.SpriteSnapshot[]? Read(DrawNode node)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = node.GetType();
        if (type.DeclaringType != typeof(BmsColumnHitObjectContainer) || type.GetField("batching", flags)?.GetValue(node) is not true) return null;
        return ((IEnumerable<BmsColumnHitObjectContainer.SpriteSnapshot>)type.GetField("sprites", flags)!.GetValue(node)!).ToArray();
    }
}
