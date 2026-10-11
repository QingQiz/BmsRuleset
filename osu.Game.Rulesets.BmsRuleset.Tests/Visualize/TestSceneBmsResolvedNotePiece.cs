#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Colour;
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
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsResolvedNotePiece : OsuTestScene
{
    [TestCase(null)]
    [TestCase(30f)]
    public void TestScrollingAnimationMaintainsSizing(float? referenceWidth)
    {
        var renderer = new DummyRenderer();
        var piece = createPiece([renderer.CreateTexture(20, 10), renderer.CreateTexture(40, 30)], referenceWidth);
        Container host = null!;
        AddStep("load animated piece", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(42, 100), Child = piece });
        });
        AddUntilStep("animation loaded", () => piece.IsLoaded);
        AddStep("check sizing while scrolling", () =>
        {
            var animationDrawable = animationOf(piece);
            var animation = (IFramedAnimation)animationDrawable;
            animation.GotoAndStop(0);
            for (var update = 0; update < 4; update++)
                host.UpdateSubTree();

            for (var update = 0; update < 32; update++)
            {
                host.Y = 10 + update;
                host.Rotation = update / 4f;
                piece.Colour = update % 2 == 0 ? Colour4.Cyan : Colour4.Yellow;
                piece.Alpha = 0.4f + update / 100f;
                host.UpdateSubTree();
                Assert.That(animationDrawable.Scale, Is.EqualTo(new Vector2(42, referenceWidth ?? 42) / 20));
            }

            animation.GotoAndStop(1);
            for (var update = 0; update < 3; update++)
                host.UpdateSubTree();
            Assert.That(animationDrawable.Scale, Is.EqualTo(new Vector2(42, referenceWidth ?? 42) / 40));
            host.Width = 67;
            host.UpdateSubTree();
            Assert.That(animationDrawable.Scale, Is.EqualTo(new Vector2(67, referenceWidth ?? 67) / 40));
        });
    }

    [TestCase(false, null, false)]
    [TestCase(false, 30f, false)]
    [TestCase(true, null, false)]
    [TestCase(true, 30f, false)]
    [TestCase(true, 0f, false)]
    [TestCase(true, -30f, false)]
    [TestCase(true, 0.1f, false)]
    [TestCase(false, null, true)]
    [TestCase(true, null, true)]
    public void TestSizingAndAnimationFramesMatchPerFramePath(bool animated, float? referenceWidth, bool subPixelTextures)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var setAutoSize = typeof(CompositeDrawable).GetProperty("AutoSizeAxes", flags | BindingFlags.Public)!.SetMethod!
            .CreateDelegate<Action<CompositeDrawable, Axes>>();
        var renderer = new DummyRenderer();
        Texture[] textures = animated
            ? [renderer.CreateTexture(20, 10), renderer.CreateTexture(40, 30)]
            : [renderer.CreateTexture(20, 10)];
        if (subPixelTextures)
            foreach (var texture in textures) texture.ScaleAdjust = 40;
        var piece = createPiece(textures, referenceWidth);
        var reference = new PerFrameSizingPiece(textures, referenceWidth);
        SizingHost actualHost = null!;
        SizingHost expectedHost = null!;
        var frame = 0UL;
        AddStep("load sizing comparison", () =>
        {
            Clear();
            AddRange([
                actualHost = new SizingHost { Size = new Vector2(42, 100), Child = piece },
                expectedHost = new SizingHost { Size = new Vector2(42, 100), Child = reference },
            ]);
        });
        AddUntilStep("pieces loaded", () => piece.IsLoaded && reference.IsLoaded);
        AddStep("compare sizing, transforms, frames and draw slots", () =>
        {
            if (animated)
            {
                ((IFramedAnimation)animationOf(piece)).GotoAndStop(0);
                reference.ChildrenOfType<TextureAnimation>().Single().GotoAndStop(0);
            }
            compare();
            actualHost.Padding = expectedHost.Padding = new MarginPadding { Left = 3, Right = 4 };
            compare();
            actualHost.SetChildSize(new Vector2(2, 3));
            expectedHost.SetChildSize(new Vector2(2, 3));
            compare();
            piece.Width = reference.Width = 0.5f;
            compare();
            actualHost.Width = expectedHost.Width = 67;
            compare();
            actualHost.Width = expectedHost.Width = 7;
            compare();
            actualHost.Width = expectedHost.Width = 67;
            compare();
            piece.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.None;
            compare();
            piece.Width = reference.Width = 37;
            compare();
            piece.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.X;
            piece.Width = reference.Width = 1;
            compare();
            actualHost.Position = expectedHost.Position = new Vector2(13, 17);
            actualHost.Rotation = expectedHost.Rotation = 13;
            actualHost.Shear = expectedHost.Shear = new Vector2(0.1f, 0.2f);
            actualHost.Scale = expectedHost.Scale = new Vector2(1.3f, 0.7f);
            piece.Colour = reference.Colour = Colour4.Cyan;
            piece.Alpha = reference.Alpha = 0.4f;
            compare();
            if (referenceWidth != 0)
            {
                // A zero-height parent has no meaningful gradient interpolation area.
                // Keep the finite colour comparison on visible geometry.
                actualHost.Colour = expectedHost.Colour = ColourInfo.GradientHorizontal(Color4.Red, Color4.White);
                piece.Colour = reference.Colour = ColourInfo.GradientVertical(Color4.Cyan, Color4.Yellow);
                compare();
                actualHost.Colour = expectedHost.Colour = Colour4.White;
                piece.Colour = reference.Colour = Colour4.Cyan;
                compare();
            }
            setAutoSize(piece, Axes.None);
            setAutoSize(reference, Axes.None);
            piece.Size = reference.Size = new Vector2(0.4f, 0.25f);
            piece.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.Both;
            piece.FillMode = reference.FillMode = FillMode.Fit;
            piece.FillAspectRatio = reference.FillAspectRatio = 0.75f;
            compare();
            piece.FillMode = reference.FillMode = FillMode.Fill;
            piece.FillAspectRatio = reference.FillAspectRatio = 2;
            compare();
            piece.FillMode = reference.FillMode = FillMode.Stretch;
            piece.RelativeSizeAxes = reference.RelativeSizeAxes = Axes.X;
            setAutoSize(piece, Axes.Y);
            setAutoSize(reference, Axes.Y);
            compare();
            actualHost.Remove(piece, false);
            expectedHost.Remove(reference, false);
            AddRange([
                actualHost = new SizingHost { Size = new Vector2(93, 100), Child = piece },
                expectedHost = new SizingHost { Size = new Vector2(93, 100), Child = reference },
            ]);
            compare();

            if (animated)
            {
                for (var repeat = 0; repeat < 4; repeat++)
                {
                    ((IFramedAnimation)animationOf(piece)).GotoAndStop(repeat % 2);
                    reference.ChildrenOfType<TextureAnimation>().Single().GotoAndStop(repeat % 2);
                    compare();
                }
                clearFrames(animationOf(piece));
                reference.ChildrenOfType<TextureAnimation>().Single().ClearFrames();
                compare();
                addFrame(animationOf(piece), renderer.CreateTexture(10, 20));
                reference.ChildrenOfType<TextureAnimation>().Single().AddFrame(renderer.CreateTexture(10, 20));
                compare();
            }
            else
            {
                piece.ChildrenOfType<Sprite>().Single().Texture = null!;
                reference.ChildrenOfType<Sprite>().Single().Texture = null!;
                compare();
                piece.ChildrenOfType<Sprite>().Single().Texture = renderer.CreateTexture(10, 20);
                reference.ChildrenOfType<Sprite>().Single().Texture = renderer.CreateTexture(10, 20);
                compare();
            }
        });

        void compare()
        {
            for (var update = 0; update < 3; update++)
            {
                actualHost.UpdateSubTree();
                expectedHost.UpdateSubTree();
                Assert.That(piece.Size, Is.EqualTo(reference.Size));
                Assert.That(piece.DrawSize, Is.EqualTo(reference.DrawSize));
                var actualSprite = piece.ChildrenOfType<Sprite>().Single();
                var expectedSprite = reference.ChildrenOfType<Sprite>().Single();
                Assert.That(actualSprite.DrawSize, Is.EqualTo(expectedSprite.DrawSize));
                for (var slot = 0; slot < 3; slot++)
                {
                    var actualNode = generate(actualSprite, ++frame, slot, false)!;
                    var expectedNode = generate(expectedSprite, ++frame, slot, false)!;
                    var actualQuad = BmsSpriteDrawNodeSnapshot.ReadQuad(actualNode);
                    var expectedQuad = BmsSpriteDrawNodeSnapshot.ReadQuad(expectedNode);
                    if (animated && referenceWidth < 0)
                    {
                        // Removing the centred identity transform changes float rounding
                        // for mirrored geometry; compare within two representable coordinates.
                        var actualCoordinates = coordinates(actualQuad);
                        var expectedCoordinates = coordinates(expectedQuad);
                        Assert.That(actualCoordinates.All(float.IsFinite) && expectedCoordinates.All(float.IsFinite), Is.True);
                        for (var coordinate = 0; coordinate < actualCoordinates.Length; coordinate++)
                        {
                            var low = MathF.BitDecrement(MathF.BitDecrement(expectedCoordinates[coordinate]));
                            var high = MathF.BitIncrement(MathF.BitIncrement(expectedCoordinates[coordinate]));
                            Assert.That(actualCoordinates[coordinate], Is.InRange(low, high), $"Coordinate {coordinate}");
                        }
                    }
                    else
                        Assert.That(actualQuad, Is.EqualTo(expectedQuad));
                    var actualColour = readColour(actualNode);
                    var expectedColour = readColour(expectedNode);
                    if (animated && !expectedColour.Colour.HasSingleColour)
                    {
                        // Sub-pixel heights amplify inverse-matrix rounding during gradient
                        // resampling. Bound this case below 1/16 of a linear 8-bit step;
                        // ordinary geometry retains the tighter channel tolerance.
                        var actualChannels = channels(actualColour.Colour);
                        var expectedChannels = channels(expectedColour.Colour);
                        var colourTolerance = referenceWidth is > 0 and < 1 ? 1d / 4096 : 2e-6;
                        Assert.That(actualChannels.Zip(expectedChannels, (a, e) => float.IsFinite(a) == float.IsFinite(e)).All(equal => equal), Is.True,
                            $"Actual: {string.Join(",", actualChannels)}; expected: {string.Join(",", expectedChannels)}");
                        Assert.That(actualColour.Blending, Is.EqualTo(expectedColour.Blending));
                        Assert.That(actualColour.Colour.HasSingleColour, Is.False);
                        Assert.That(actualChannels, Is.EqualTo(expectedChannels).Within(colourTolerance),
                            $"Maximum linear channel difference: {actualChannels.Zip(expectedChannels, (a, e) => Math.Abs(a - e)).Max():R}; " +
                            $"actual: {string.Join(",", actualChannels.Select(c => c.ToString("R")))}; " +
                            $"expected: {string.Join(",", expectedChannels.Select(c => c.ToString("R")))}");
                    }
                    else
                        Assert.That(actualColour, Is.EqualTo(expectedColour));
                    Assert.That(BmsSpriteDrawNodeSnapshot.ReadTexture(actualNode)?.DisplaySize,
                        Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadTexture(expectedNode)?.DisplaySize));
                }
            }
        }
    }

    private static BmsResolvedNotePiece createPiece(Texture[] textures, float? referenceWidth)
        => new(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1), textures, referenceWidth);

    private static float[] channels(ColourInfo colour) =>
    [
        colour.TopLeft.Linear.R, colour.TopLeft.Linear.G, colour.TopLeft.Linear.B, colour.TopLeft.Linear.A,
        colour.TopRight.Linear.R, colour.TopRight.Linear.G, colour.TopRight.Linear.B, colour.TopRight.Linear.A,
        colour.BottomLeft.Linear.R, colour.BottomLeft.Linear.G, colour.BottomLeft.Linear.B, colour.BottomLeft.Linear.A,
        colour.BottomRight.Linear.R, colour.BottomRight.Linear.G, colour.BottomRight.Linear.B, colour.BottomRight.Linear.A,
    ];

    private static float[] coordinates(Quad quad) =>
    [
        quad.TopLeft.X, quad.TopLeft.Y, quad.TopRight.X, quad.TopRight.Y,
        quad.BottomLeft.X, quad.BottomLeft.Y, quad.BottomRight.X, quad.BottomRight.Y,
    ];

    private static Drawable animationOf(Drawable drawable)
        => drawable.ChildrenOfType<Drawable>().Single(d => d is IFramedAnimation);

    private static void clearFrames(Drawable drawable)
    {
        if (drawable is BmsResolvedTextureAnimation animation)
            animation.ClearFrames();
        else
            ((TextureAnimation)drawable).ClearFrames();
    }

    private static void addFrame(Drawable drawable, Texture texture)
    {
        if (drawable is BmsResolvedTextureAnimation animation)
            animation.AddFrame(texture);
        else
            ((TextureAnimation)drawable).AddFrame(texture);
    }

    private sealed partial class SizingHost : Container
    {
        internal void SetChildSize(Vector2 size) => RelativeChildSize = size;
    }

    // The old per-frame path provides an independent reference for coupled sizing and animation invalidations.
    private sealed partial class PerFrameSizingPiece : CompositeDrawable
    {
        private readonly float? referenceWidth;
        private readonly Drawable content;
        private Texture? appliedTexture;
        private float appliedDrawWidth;
        private float appliedNoteWidth;

        internal PerFrameSizingPiece(Texture[] textures, float? referenceWidth)
        {
            this.referenceWidth = referenceWidth;
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            if (textures.Length == 1)
                content = new Sprite { Texture = textures[0] };
            else
            {
                var animation = new TextureAnimation { DefaultFrameLength = 1000 / 60d, Loop = true };
                foreach (var texture in textures)
                    animation.AddFrame(texture);
                content = animation;
            }
            InternalChild = content;
        }

        protected override void Update()
        {
            base.Update();
            var texture = content switch
            {
                Sprite sprite => sprite.Texture,
                TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
                _ => null,
            };
            if (texture == null)
                return;
            var noteWidth = referenceWidth ?? DrawWidth;
            if (ReferenceEquals(texture, appliedTexture) && DrawWidth == appliedDrawWidth && noteWidth == appliedNoteWidth)
                return;
            appliedTexture = texture;
            appliedDrawWidth = DrawWidth;
            appliedNoteWidth = noteWidth;
            content.Scale = Vector2.Divide(new Vector2(DrawWidth, noteWidth), Math.Max(1, texture.DisplayWidth));
        }
    }
}
