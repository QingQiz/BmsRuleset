#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsColumnSeparators : OsuTestScene
{
    [TestCase(1f, 1f)]
    [TestCase(2.5f, 0.4f)]
    [TestCase(1f, 0f)]
    [TestCase(0f, 1f)]
    public void TestDirectSeparatorMatchesFormerComposition(float width, float alpha)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readQuad = typeof(SpriteDrawNode).GetProperty("ScreenSpaceDrawQuad", flags)!.GetMethod!
            .CreateDelegate<Func<SpriteDrawNode, Quad>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var readTexture = typeof(SpriteDrawNode).GetProperty("Texture", flags)!.GetMethod!
            .CreateDelegate<Func<SpriteDrawNode, Texture?>>();
        var colour = new Color4(0.3f, 0.6f, 0.9f, alpha);
        var oldSeparator = new Container
        {
            RelativeSizeAxes = Axes.Y,
            Width = width,
            Scale = new Vector2(0.740f, 1),
            Alpha = width > 0 ? 1 : 0,
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopLeft,
            X = -0.16f,
            Child = LegacyColourCompatibility.ApplyWithDoubledAlpha(new Box { RelativeSizeAxes = Axes.Both }, colour),
        };
        var direct = new BmsColumnSeparator(width, colour)
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopLeft,
            X = -0.16f,
        };
        Container oldHost = null!;
        Container directHost = null!;
        var frame = 0UL;

        AddStep("load equivalent separator hosts", () =>
        {
            Clear();
            AddRange([
                oldHost = new Container
                {
                    Size = new Vector2(42, 200),
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Bottom = 80 },
                            Child = oldSeparator,
                        },
                    },
                },
                directHost = new Container
                {
                    Size = new Vector2(42, 200),
                    Child = new BmsHitTargetInsetContainer { Child = direct },
                },
            ]);
        });
        AddUntilStep("separator hosts loaded", () => oldSeparator.IsLoaded && direct.IsLoaded);
        AddStep("compare all draw buffers", compare);
        AddStep("compare right line visibility overrides", () =>
        {
            oldSeparator.Alpha = 1;
            direct.Alpha = alpha;
            compare();
            oldSeparator.Alpha = direct.Alpha = 0;
            compare();
            oldSeparator.Alpha = 1;
            direct.Alpha = alpha;
        });
        AddStep("compare parent resizing", () =>
        {
            oldHost.Size = directHost.Size = new Vector2(67.5f, 360);
            compare();
        });
        AddStep("compare zero width and inset larger than parent", () =>
        {
            oldHost.Size = directHost.Size = new Vector2(0, 40);
            compare();
            oldHost.Size = directHost.Size = new Vector2(67.5f, 360);
            compare();
        });
        AddStep("compare colour opacity and geometry", () =>
        {
            Container[] hosts = [oldHost, directHost];
            foreach (var host in hosts)
            {
                host.Position = new Vector2(137.25f, 91.75f);
                host.Scale = new Vector2(1.2f, 0.7f);
                host.Rotation = 17;
                host.Colour = Colour4.Cyan;
                host.Alpha = 0.4f;
            }
            compare();
        });
        AddStep("compare masked edges", () =>
        {
            oldHost.Masking = directHost.Masking = true;
            compare();
        });

        void compare()
        {
            oldHost.UpdateSubTree();
            directHost.UpdateSubTree();
            oldHost.UpdateSubTreeMasking();
            directHost.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
            {
                var original = snapshots(generate(oldHost, ++frame, slot, true));
                var candidate = snapshots(generate(directHost, ++frame, slot, true));
                if (width > 0 && alpha > 0 && oldSeparator.Alpha > 0)
                    Assert.That(original, Is.Not.Empty);
                Assert.That(candidate.Count, Is.EqualTo(original.Count));
                for (var i = 0; i < original.Count; i++)
                {
                    Assert.That(candidate[i].Texture, Is.SameAs(original[i].Texture));
                    Assert.That(candidate[i].Colour, Is.EqualTo(original[i].Colour));
                    Assert.That(Vector2.Distance(candidate[i].Quad.TopLeft, original[i].Quad.TopLeft), Is.LessThan(0.001));
                    Assert.That(Vector2.Distance(candidate[i].Quad.TopRight, original[i].Quad.TopRight), Is.LessThan(0.001));
                    Assert.That(Vector2.Distance(candidate[i].Quad.BottomLeft, original[i].Quad.BottomLeft), Is.LessThan(0.001));
                    Assert.That(Vector2.Distance(candidate[i].Quad.BottomRight, original[i].Quad.BottomRight), Is.LessThan(0.001));
                }
            }
        }

        List<(Texture? Texture, Quad Quad, DrawColourInfo Colour)> snapshots(DrawNode? node)
        {
            var result = new List<(Texture?, Quad, DrawColourInfo)>();
            visit(node);
            return result;

            void visit(DrawNode? child)
            {
                if (child is SpriteDrawNode sprite)
                    result.Add((readTexture(sprite), readQuad(sprite), readColour(sprite)));
                if (child is ICompositeDrawNode { Children: { } children })
                    foreach (var nested in children)
                        visit(nested);
            }
        }
    }
}
