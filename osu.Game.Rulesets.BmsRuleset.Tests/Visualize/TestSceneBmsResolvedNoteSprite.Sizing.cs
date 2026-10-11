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
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedNoteSprite
{
    private static bool countSizingReads;
    private static int sizingReads;

    [TestCase(null)]
    [TestCase(30f)]
    [NonParallelizable]
    [Category("JudgementInstrumentation")]
    public void TestScrollingStaticSpriteSkipsSizingReads(float? referenceWidth)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        Func<DrawNode, Quad> readQuad = BmsSpriteDrawNodeSnapshot.ReadQuad;
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var renderer = new DummyRenderer();
        var texture = renderer.CreateTexture(20, 10);
        var sprite = new BmsResolvedNoteSprite(texture, referenceWidth);
        Container host = null!;
        var frame = 0UL;
        var expectedHeight = (referenceWidth ?? 42) / 2;

        AddStep("load scrolling sprite", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = sprite });
        });
        AddUntilStep("sprite loaded", () => sprite.IsLoaded);
        AddStep("verify stable sizing and moving draw state", () =>
        {
            host.UpdateSubTree();
            host.UpdateSubTree();
            using var probe = new ScopedMethodProbe(typeof(Drawable).GetProperty(nameof(Drawable.DrawWidth))!.GetMethod!,
                typeof(TestSceneBmsResolvedNoteSprite).GetMethod(nameof(countSizingRead), BindingFlags.Static | BindingFlags.NonPublic)!);
            sizingReads = 0;
            for (var repeat = 0; repeat < 32; repeat++)
            {
                host.Y = 10 + repeat;
                host.Rotation = repeat / 4f;
                sprite.Colour = repeat % 2 == 0 ? Colour4.Cyan : Colour4.Yellow;
                sprite.Alpha = 0.4f + repeat / 100f;
                countSizingReads = true;
                try
                {
                    host.UpdateSubTree();
                }
                finally
                {
                    countSizingReads = false;
                }

                Assert.That(sprite.Height, Is.EqualTo(expectedHeight));
                host.UpdateSubTreeMasking();
                for (var slot = 0; slot < 3; slot++)
                {
                    var node = generate(sprite, ++frame, slot, false)!;
                    Assert.That(node, Is.Not.Null);
                    Assert.That(readQuad(node), Is.EqualTo(sprite.ScreenSpaceDrawQuad));
                    Assert.That(readColour(node), Is.EqualTo(sprite.DrawColourInfo));
                }
            }
            Assert.That(sizingReads, Is.Zero, "Position and colour changes must not require another note-height width read.");

            host.Width = 67;
            host.UpdateSubTree();
            Assert.That(sprite.Height, Is.EqualTo((referenceWidth ?? 67) / 2));
            host.Padding = new MarginPadding { Left = 3, Right = 4 };
            host.UpdateSubTree();
            Assert.That(sprite.Height, Is.EqualTo((referenceWidth ?? 60) / 2));
            sprite.Width = 0.5f;
            host.UpdateSubTree();
            Assert.That(sprite.Height, Is.EqualTo((referenceWidth ?? 30) / 2));
            host.Width = 7;
            host.UpdateSubTree();
            Assert.That(sprite.Height, Is.Zero);
            host.Width = 67;
            host.UpdateSubTree();
            Assert.That(sprite.Height, Is.EqualTo((referenceWidth ?? 30) / 2));
            TestContext.WriteLine("STATIC_SPRITE_SIZING stableReads=0 updates=32 slots=3");
        });
    }

    private static void countSizingRead(Drawable __instance)
    {
        if (countSizingReads && __instance is BmsResolvedNoteSprite)
            sizingReads++;
    }

    [TestCase(null)]
    [TestCase(30f)]
    public void TestSizingInvalidationPreservesLegacyFrames(float? referenceWidth)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        Func<DrawNode, Quad> readQuad = BmsSpriteDrawNodeSnapshot.ReadQuad;
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        Func<DrawNode, Texture?> readTexture = BmsSpriteDrawNodeSnapshot.ReadTexture;
        var renderer = new DummyRenderer();
        var direct = new BmsResolvedNoteSprite(renderer.CreateTexture(20, 10), referenceWidth);
        var reference = new PerFrameSizingSprite(renderer.CreateTexture(20, 10), referenceWidth);
        SizingHost directHost = null!;
        SizingHost referenceHost = null!;
        var frame = 0UL;

        AddStep("load sizing comparison", () =>
        {
            Clear();
            AddRange([
                directHost = new SizingHost { Size = new Vector2(42, 100), Child = direct },
                referenceHost = new SizingHost { Size = new Vector2(42, 100), Child = reference },
            ]);
        });
        AddUntilStep("sizing sprites loaded", () => direct.IsLoaded && reference.IsLoaded);
        AddStep("compare sizing invalidations frame by frame", () =>
        {
            compare();
            directHost.Padding = referenceHost.Padding = new MarginPadding { Left = 3, Right = 4 };
            compare();
            directHost.SetChildSize(new Vector2(2, 3));
            referenceHost.SetChildSize(new Vector2(2, 3));
            compare();
            direct.Size = reference.Size = new Vector2(0.75f, 25);
            compare();
            direct.Height = reference.Height = 37;
            compare();
            Assert.That(direct.Height, Is.EqualTo(37), "An unchanged width preserves externally assigned height.");
            direct.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.None;
            compare();
            directHost.Width = referenceHost.Width = 85;
            compare();
            direct.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.X;
            compare();
            direct.Width = reference.Width = 0.6f;
            compare();

            directHost.Height = referenceHost.Height = 1;
            direct.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.Both;
            compare();
            direct.Size = reference.Size = new Vector2(0.4f, 0.25f);
            direct.FillMode = reference.FillMode = FillMode.Fit;
            compare();
            direct.FillAspectRatio = reference.FillAspectRatio = 0.75f;
            compare();
            direct.FillMode = reference.FillMode = FillMode.Fill;
            compare();
            direct.FillAspectRatio = reference.FillAspectRatio = 2;
            compare();
            direct.FillMode = reference.FillMode = FillMode.Stretch;
            compare();
            direct.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.X;
            compare();

            directHost.Remove(direct, false);
            referenceHost.Remove(reference, false);
            AddRange([
                directHost = new SizingHost { Size = new Vector2(93, 100), Child = direct },
                referenceHost = new SizingHost { Size = new Vector2(93, 100), Child = reference },
            ]);
            compare();
            direct.Texture = reference.Texture = null!;
            compare();
            direct.Texture = renderer.CreateTexture(10, 20);
            reference.Texture = renderer.CreateTexture(10, 20);
            compare();
            direct.Texture.ScaleAdjust = reference.Texture.ScaleAdjust = 20;
            compare();
            direct.Width = reference.Width = 0.25f;
            compare();
            directHost.Width = referenceHost.Width = 0;
            compare();
            directHost.Width = referenceHost.Width = 42;
            compare();
        });

        void compare()
        {
            for (var update = 0; update < 3; update++)
            {
                directHost.UpdateSubTree();
                referenceHost.UpdateSubTree();
                Assert.That(direct.Size, Is.EqualTo(reference.Size));
                Assert.That(direct.DrawSize, Is.EqualTo(reference.DrawSize));
                for (var slot = 0; slot < 3; slot++)
                {
                    var actual = generate(direct, ++frame, slot, false)!;
                    var expected = generate(reference, ++frame, slot, false)!;
                    Assert.That(readQuad(actual), Is.EqualTo(readQuad(expected)));
                    Assert.That(readColour(actual), Is.EqualTo(readColour(expected)));
                    Assert.That(readTexture(actual)?.DisplaySize, Is.EqualTo(readTexture(expected)?.DisplaySize));
                }
            }
        }
    }

    private sealed partial class SizingHost : Container
    {
        internal void SetChildSize(Vector2 size) => RelativeChildSize = size;
    }

    // Preserve the pre-cache sizing path as a reference for coupled relative-axis changes.
    private sealed partial class PerFrameSizingSprite : Sprite
    {
        private readonly float? referenceWidth;
        private Texture? appliedTexture;
        private float appliedDrawWidth;
        private float appliedNoteWidth;

        internal PerFrameSizingSprite(Texture texture, float? referenceWidth)
        {
            this.referenceWidth = referenceWidth;
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
            RelativeSizeAxes = Axes.X;
            Texture = texture;
            Height = texture.DisplayHeight;
        }

        protected override void Update()
        {
            base.Update();
            var texture = Texture;
            if (texture == null)
                return;
            var drawWidth = DrawWidth;
            var noteWidth = referenceWidth ?? drawWidth;
            if (ReferenceEquals(texture, appliedTexture) && drawWidth == appliedDrawWidth && noteWidth == appliedNoteWidth)
                return;
            appliedTexture = texture;
            appliedDrawWidth = drawWidth;
            appliedNoteWidth = noteWidth;
            Height = drawWidth == 0 ? 0 : texture.DisplayHeight * (noteWidth / Math.Max(1, texture.DisplayWidth));
        }
    }
}
