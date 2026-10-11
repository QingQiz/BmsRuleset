#nullable enable

using System;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedTextureAnimation
{
    [TestCase(false)]
    [TestCase(true)]
    public void AnimatedQuadMatchesFrameworkAcrossDrawBuffers(bool relativeSize)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var renderer = new DummyRenderer();
        var animation = new BmsResolvedTextureAnimation([renderer.CreateTexture(20, 10), renderer.CreateTexture(30, 40)])
        {
            RelativeSizeAxes = relativeSize ? Axes.X : Axes.None,
            IsPlaying = false,
        };
        if (relativeSize) animation.Width = 1;
        Container host = null!;
        var frame = 0UL;
        var comparisons = 0;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        AddStep("load animated quad reference", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = animation });
        });
        AddUntilStep("animated leaf loaded", () => animation.IsLoaded);
        AddStep("compare scrolling, frames and all draw buffers", () =>
        {
            for (var repeat = 0; repeat < 128; repeat++)
            {
                host.Position = new Vector2(31.125f + repeat / 8f, -10000.25f + repeat * 17.75f);
                animation.Y = repeat % 2 == 0 ? -987.125f : 1123.75f;
                animation.GotoFrame(repeat % 2);
                host.UpdateSubTree();
                compare();
            }

            host.Rotation = 17;
            host.Shear = new Vector2(.3f, -.2f);
            host.Scale = new Vector2(-1.2f, .7f);
            compare();
            host.Padding = new MarginPadding { Left = 3, Right = 7, Top = 2 };
            compare();
            foreach (var width in new[] { 0f, -1f, .00001f, 73f })
            {
                animation.Width = width;
                foreach (var height in new[] { 0f, -1f, .00001f, 37f })
                {
                    animation.Height = height;
                    compare();
                }
            }
            animation.EdgeSmoothness = new Vector2(2, 1);
            compare();
            animation.EdgeSmoothness = Vector2.Zero;
            compare();
            host.Position = Vector2.Zero;
            compare();
            TestContext.WriteLine($"ANIMATION_QUAD_REFERENCE comparisons={comparisons} buffers=3");
        });

        void compare()
        {
            host.UpdateSubTree();
            var rectangle = animation.DrawRectangle;
            if (animation.EdgeSmoothness != Vector2.Zero)
                rectangle = rectangle.Inflate(animation.InflationAmount);
            var expected = animation.ToScreenSpace(rectangle);
            var actual = animation.ScreenSpaceDrawQuad;
            Assert.That(actual, Is.EqualTo(expected));
            for (var slot = 0; slot < 3; slot++)
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(generate(animation, ++frame, slot, true)!), Is.EqualTo(expected));
            comparisons++;
        }
    }
}
