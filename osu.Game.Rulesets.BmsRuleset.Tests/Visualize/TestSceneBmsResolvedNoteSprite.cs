#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsResolvedNoteSprite : OsuTestScene
{
    [TestCase(20, 10, 1f, null)]
    [TestCase(40, 20, 2f, null)]
    [TestCase(1, 1, 1f, null)]
    [TestCase(20, 10, 1f, 30f)]
    [TestCase(40, 20, 2f, 30f)]
    [TestCase(1, 1, 1f, 30f)]
    public void TestStaticSpriteMatchesComposite(int width, int height, float textureScale, float? referenceWidth)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        Func<DrawNode, Quad> readQuad = BmsSpriteDrawNodeSnapshot.ReadQuad;
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        Func<DrawNode, Texture?> readTexture = BmsSpriteDrawNodeSnapshot.ReadTexture;
        var readMasked = typeof(Drawable).GetProperty("IsMaskedAway", flags)!.GetMethod!
            .CreateDelegate<Func<Drawable, bool>>();
        var renderer = new DummyRenderer();
        var texture = renderer.CreateTexture(width, height);
        texture.ScaleAdjust = textureScale;
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);
        var oldPiece = new BmsResolvedNotePiece(lookup, [texture], referenceWidth)
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
        };
        var direct = new BmsResolvedNoteSprite(texture, referenceWidth)
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
        };
        Container oldHost = null!;
        Container directHost = null!;
        var frame = 0UL;

        AddStep("load equivalent skin hosts", () =>
        {
            Clear();
            AddRange([
                oldHost = new Container { Size = new Vector2(42, 100), Child = oldPiece },
                directHost = new Container { Size = new Vector2(42, 100), Child = direct },
            ]);
        });
        AddUntilStep("skin hosts loaded", () => oldPiece.IsLoaded && direct.IsLoaded);
        AddStep("compare geometry and all draw buffers", compare);
        AddStep("compare parent resizing", () =>
        {
            oldHost.Width = directHost.Width = 67.5f;
            compare();
        });
        AddStep("compare zero width and recovery", () =>
        {
            oldHost.Width = directHost.Width = 0;
            oldHost.UpdateSubTree();
            directHost.UpdateSubTree();
            Assert.That(direct.Height, Is.Zero);
            Assert.That(oldPiece.Height, Is.Zero);
            oldHost.Width = directHost.Width = 67.5f;
            compare();
        });
        AddStep("compare parent transforms and colour", () =>
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
            oldPiece.X = direct.X = 20;
            oldHost.UpdateSubTreeMasking();
            directHost.UpdateSubTreeMasking();
            Assert.That(readMasked(direct), Is.EqualTo(readMasked(oldPiece.ChildrenOfType<Sprite>().Single())));
            compare();
        });

        void compare()
        {
            oldHost.UpdateSubTree();
            directHost.UpdateSubTree();
            oldHost.UpdateSubTreeMasking();
            directHost.UpdateSubTreeMasking();
            Assert.That(direct.DrawWidth, Is.EqualTo(oldPiece.DrawWidth).Within(0.0001));
            Assert.That(direct.Height, Is.EqualTo(oldPiece.Height).Within(0.0001));

            for (var slot = 0; slot < 3; slot++)
            {
                var oldNode = spriteNode(generate(oldHost, ++frame, slot, true));
                var newNode = spriteNode(generate(directHost, ++frame, slot, true));
                Assert.That(oldNode, Is.Not.Null);
                Assert.That(newNode, Is.Not.Null);
                Assert.That(readTexture(newNode!), Is.SameAs(readTexture(oldNode!)));
                Assert.That(readColour(newNode!), Is.EqualTo(readColour(oldNode!)));
                var oldQuad = readQuad(oldNode!);
                var newQuad = readQuad(newNode!);
                Assert.That(Vector2.Distance(oldQuad.TopLeft, newQuad.TopLeft), Is.LessThan(0.001));
                Assert.That(Vector2.Distance(oldQuad.TopRight, newQuad.TopRight), Is.LessThan(0.001));
                Assert.That(Vector2.Distance(oldQuad.BottomLeft, newQuad.BottomLeft), Is.LessThan(0.001));
                Assert.That(Vector2.Distance(oldQuad.BottomRight, newQuad.BottomRight), Is.LessThan(0.001));
            }
        }

        static DrawNode? spriteNode(DrawNode? node)
        {
            if (node != null && BmsSpriteDrawNodeSnapshot.IsSprite(node))
                return node;
            if (node is ICompositeDrawNode { Children: { } children })
                foreach (var child in children)
                    if (spriteNode(child) is { } result)
                        return result;
            return null;
        }
    }
}
