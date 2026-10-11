#nullable enable

using System;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedNoteSprite
{
    private static bool measuringQuadProducts;
    private static int quadProducts;

    [Test]
    [NonParallelizable]
    [Category("JudgementInstrumentation")]
    public void TestNoteQuadUsesSharedAffineProducts()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        Func<DrawNode, Quad> readQuad = BmsSpriteDrawNodeSnapshot.ReadQuad;
        var sprite = new BmsResolvedNoteSprite(new DummyRenderer().CreateTexture(20, 10), null);
        Container host = null!;
        var frame = 0UL;

        AddStep("load quad comparison", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = sprite });
        });
        AddUntilStep("quad sprite loaded", () => sprite.IsLoaded);
        AddStep("compare scrolling products and snapshots", () =>
        {
            host.UpdateSubTree();
            _ = sprite.ScreenSpaceDrawQuad;
            using var probe = new ScopedMethodProbe(typeof(Quad).GetMethod("op_Multiply", [typeof(Quad), typeof(Matrix3)])!,
                typeof(TestSceneBmsResolvedNoteSprite).GetMethod(nameof(countQuadProducts), BindingFlags.Static | BindingFlags.NonPublic)!);
            quadProducts = 0;
            for (var repeat = 0; repeat < 128; repeat++)
            {
                host.Position = new Vector2(31.125f + repeat / 8f, -10000.25f + repeat * 17.75f);
                sprite.Y = repeat % 2 == 0 ? -987.125f : 1123.75f;
                compare();
            }
            TestContext.WriteLine($"SCROLL_QUAD_PRODUCTS translations=128 products={quadProducts}");
            Assert.That(quadProducts, Is.Zero, "The zero-origin rectangle uses shared affine products.");

            host.Rotation = 17;
            compare();
            host.Shear = new Vector2(0.3f, -0.2f);
            compare();
            host.Scale = new Vector2(-1.2f, 0.7f);
            compare();
            host.Width = 73;
            host.UpdateSubTree();
            compare();
            host.Padding = new MarginPadding { Left = 3, Right = 7 };
            host.UpdateSubTree();
            compare();
            sprite.Height = 37;
            compare();
            sprite.EdgeSmoothness = new Vector2(2, 1);
            compare();
            sprite.EdgeSmoothness = Vector2.Zero;
            compare();
            host.Width = 0;
            host.UpdateSubTree();
            compare();
            host.Width = 42;
            host.UpdateSubTree();
            host.Position = Vector2.Zero;
            compare();
            // The framework owns the inflated edge-smoothing path; its internal matrix
            // implementation may change without affecting the public quad contract.
        });

        void compare()
        {
            // Build the reference through the framework's public transform path, outside the work counter.
            var rectangle = sprite.DrawRectangle;
            if (sprite.EdgeSmoothness != Vector2.Zero)
                rectangle = rectangle.Inflate(sprite.InflationAmount);
            var expected = sprite.ToScreenSpace(rectangle);
            Quad actual;
            measuringQuadProducts = true;
            try
            {
                actual = sprite.ScreenSpaceDrawQuad;
            }
            finally
            {
                measuringQuadProducts = false;
            }
            Assert.That(actual, Is.EqualTo(expected));
            for (var slot = 0; slot < 3; slot++)
            {
                var node = generate(sprite, ++frame, slot, false)!;
                Assert.That(readQuad(node), Is.EqualTo(expected), $"Buffer {slot} changed the scrolling quad.");
            }
        }
    }

    private static void countQuadProducts()
    {
        if (measuringQuadProducts)
            quadProducts++;
    }
}
