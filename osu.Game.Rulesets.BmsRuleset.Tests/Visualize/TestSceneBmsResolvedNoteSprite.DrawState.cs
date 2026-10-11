#nullable enable

using System;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedNoteSprite
{
    [Test]
    public void TestFullTextureScrollingMatchesFrameworkAcrossDrawBuffers()
    {
        var generate = drawNodeGenerator();
        var sprite = new BmsResolvedNoteSprite(new DummyRenderer().CreateTexture(20, 10), null);
        Container host = null!;
        var frame = 0UL;

        AddStep("load full texture sprite", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = sprite });
        });
        AddUntilStep("full texture loaded", () => sprite.IsLoaded);
        AddStep("check scrolling mapping work", () =>
        {
            host.UpdateSubTree();
            using var reference = new SpriteDrawNode(sprite);
            for (var repeat = 0; repeat < 64; repeat++)
            {
                host.Y = -1000.25f + repeat * 17.125f;
                host.Rotation = repeat / 4f;
                sprite.Y = repeat / 8f;
                reference.ApplyState();
                for (var slot = 0; slot < 3; slot++)
                {
                    var actual = generate(sprite, ++frame, slot, false)!;
                    assertDrawState(actual, reference);
                }
            }
        });
    }

    [Test]
    public void TestOverflowingRelativeDimensionsMatchFramework()
    {
        var generate = drawNodeGenerator();
        var sprite = new BmsResolvedNoteSprite(new DummyRenderer().CreateTexture(20, 10), null);
        Container host = null!;
        var frame = 0UL;

        AddStep("load overflow comparison", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = sprite });
        });
        AddUntilStep("overflow sprite loaded", () => sprite.IsLoaded);
        AddStep("compare derived infinite dimensions", () =>
        {
            sprite.RelativeSizeAxes = Axes.Both;
            Vector2[] sizes = [new(2, 1), new(1, 2)];
            foreach (var size in sizes)
            {
                host.Size = new Vector2(float.MaxValue);
                sprite.Size = size;
                Assert.That(float.IsInfinity(sprite.DrawWidth) || float.IsInfinity(sprite.DrawHeight), Is.True);
                using var reference = new SpriteDrawNode(sprite);
                reference.ApplyState();
                for (var slot = 0; slot < 3; slot++)
                {
                    var actual = generate(sprite, ++frame, slot, false)!;
                    assertTextureCoords(actual, reference);
                }
            }

            // Recover before the next visual update: the legacy height setter rejects infinity.
            host.Size = new Vector2(42, 100);
            sprite.Size = Vector2.One;
            sprite.RelativeSizeAxes = Axes.X;
        });
    }

    [TestCase(WrapMode.None)]
    [TestCase(WrapMode.ClampToEdge)]
    [TestCase(WrapMode.Repeat)]
    public void TestDrawStateMatchesFrameworkForTextureAndLayoutChanges(WrapMode wrapMode)
    {
        var generate = drawNodeGenerator();
        var renderer = new DummyRenderer();
        var texture = new Texture(renderer.CreateTexture(20, 10), wrapMode, wrapMode);
        var sprite = new BmsResolvedNoteSprite(texture, null);
        Container host = null!;
        var frame = 0UL;

        AddStep("load draw state comparison", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = sprite });
        });
        AddUntilStep("draw state sprite loaded", () => sprite.IsLoaded);
        AddStep("compare texture and geometry states", () =>
        {
            compare();
            host.Position = new Vector2(31.125f, -10000.25f);
            host.Scale = new Vector2(-1.2f, 0.7f);
            host.Rotation = 17;
            host.Shear = new Vector2(0.3f, -0.2f);
            compare();
            host.Padding = new MarginPadding { Left = 3, Right = 7 };
            host.Width = 73;
            compare();
            sprite.EdgeSmoothness = new Vector2(2, 1);
            compare();
            sprite.TextureRectangle = new RectangleF(-0.125f, 0.25f, 0.75f, 1.5f);
            compare();
            Axes[] textureAxes = [Axes.None, Axes.X, Axes.Y, Axes.Both];
            foreach (var axes in textureAxes)
            {
                sprite.TextureRelativeSizeAxes = axes;
                compare();
            }
            sprite.TextureRectangle = new RectangleF(0, 0, 1, 1);
            sprite.EdgeSmoothness = Vector2.Zero;
            texture.ScaleAdjust = 2;
            compare();
            sprite.Alpha = 0.4f;
            sprite.Colour = Colour4.Cyan;
            compare();
            sprite.Alpha = 1;
            sprite.Colour = Colour4.White;
            typeof(Texture).GetProperty(nameof(Texture.Opacity))!.SetValue(texture, Opacity.Opaque);
            compare();
            sprite.Blending = BlendingParameters.Additive;
            compare();
            sprite.Blending = BlendingParameters.Mixture;
            sprite.EdgeSmoothness = new Vector2(1, 2);
            compare();
            host.Width = 0;
            compare();
            host.Width = 42;
            compare();
            sprite.TextureRectangle = new RectangleF(0, 0, 0, 1);
            compare();
            sprite.TextureRectangle = new RectangleF(0, 0, 1, 1);
            sprite.Texture = null!;
            compare();
            sprite.Texture = renderer.CreateTexture(10, 20);
            compare();
        });
        AddStep("check three independent snapshots", () =>
        {
            DrawNode[] nodes = new DrawNode[3];
            SpriteDrawNode[] references = new SpriteDrawNode[3];
            try
            {
                for (var slot = 0; slot < 3; slot++)
                {
                    sprite.Y += 13.5f;
                    sprite.Colour = slot == 1 ? Colour4.Cyan : Colour4.Yellow;
                    sprite.TextureRectangle = new RectangleF(slot / 8f, 0, 1, 1);
                    references[slot] = new SpriteDrawNode(sprite);
                    references[slot].ApplyState();
                    nodes[slot] = generate(sprite, ++frame, slot, false)!;
                }
                sprite.Y += 100;
                sprite.TextureRectangle = new RectangleF(0, 0, 1, 1);
                for (var slot = 0; slot < 3; slot++)
                    assertDrawState(nodes[slot], references[slot]);
            }
            finally
            {
                foreach (var reference in references)
                    reference?.Dispose();
            }
        });

        void compare()
        {
            host.UpdateSubTree();
            using var reference = new SpriteDrawNode(sprite);
            reference.ApplyState();
            for (var slot = 0; slot < 3; slot++)
                assertDrawState(generate(sprite, ++frame, slot, false)!, reference);
        }
    }

    private static Func<Drawable, ulong, int, bool, DrawNode?> drawNodeGenerator()
        => typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
                           .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();

    private static void assertDrawState(DrawNode actual, SpriteDrawNode expected)
    {
        Assert.That(actual, Is.Not.Null);
        Assert.That(BmsSpriteDrawNodeSnapshot.ReadTexture(actual), Is.SameAs(BmsSpriteDrawNodeSnapshot.ReadTexture(expected)));
        Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(actual), Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadQuad(expected)));
        string[] properties = ["DrawInfo", "DrawColourInfo", "DrawRectangle", "InflationAmount", "CanDrawOpaqueInterior"];
        foreach (var property in properties)
            Assert.That(BmsSpriteDrawNodeSnapshot.Read<object>(actual, property), Is.EqualTo(BmsSpriteDrawNodeSnapshot.Read<object>(expected, property)), property);

        assertTextureCoords(actual, expected);
        if (BmsSpriteDrawNodeSnapshot.Read<bool>(expected, "CanDrawOpaqueInterior"))
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var expectedQuad = typeof(SpriteDrawNode).GetField("ConservativeScreenSpaceDrawQuad", flags)!.GetValue(expected);
            var actualQuad = actual.GetType().GetField("conservativeScreenSpaceDrawQuad", flags)?.GetValue(actual)
                             ?? typeof(SpriteDrawNode).GetField("ConservativeScreenSpaceDrawQuad", flags)!.GetValue(actual);
            Assert.That(actualQuad, Is.EqualTo(expectedQuad));
        }
    }

    private static void assertTextureCoords(DrawNode actual, SpriteDrawNode expected)
    {
        var actualCoords = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(actual, "TextureCoords");
        var expectedCoords = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(expected, "TextureCoords");
        Assert.That(actualCoords.X, Is.EqualTo(expectedCoords.X));
        Assert.That(actualCoords.Y, Is.EqualTo(expectedCoords.Y));
        Assert.That(actualCoords.Width, Is.EqualTo(expectedCoords.Width));
        Assert.That(actualCoords.Height, Is.EqualTo(expectedCoords.Height));
    }
}
