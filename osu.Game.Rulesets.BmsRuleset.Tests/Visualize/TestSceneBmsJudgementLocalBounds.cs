#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

// Independent feasibility only: these containers are not used by production or skins.
[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsJudgementLocalBounds : OsuTestScene
{
    private static HashSet<Drawable>? measured;
    private static int matrixEntries;
    private static int transformApplications;
    private static int measurementThread;

    [Test]
    public void LocalBoundsMatchOrdinaryParentSpaceContract()
    {
        var direct = frameworkBounds();
        var child = new LocalBoundsContainer { Size = new Vector2(72, 23) };
        var parent = new Container { Size = new Vector2(640, 480), Child = child };
        var maximumError = 0f;
        var comparisons = 0;

        AddStep("load parent-space comparison", () =>
        {
            Clear();
            Add(parent);
        });
        AddUntilStep("bounds child loaded", () => child.IsLoaded);
        AddStep("compare local transform against nonvirtual framework bounds", () =>
        {
            for (var repeat = 0; repeat < 128; repeat++)
            {
                parent.Position = new Vector2(137.25f + repeat / 8f, 91.75f - repeat / 4f);
                parent.Rotation = repeat % 2 == 0 ? 0 : 17;
                parent.Shear = repeat % 3 == 0 ? Vector2.Zero : new Vector2(.3f, -.2f);
                parent.Scale = repeat % 5 == 0 ? new Vector2(-1.2f, .7f) : Vector2.One;
                parent.Padding = new MarginPadding { Left = 3, Right = 7, Top = 2, Bottom = 5 };
                parent.RelativeChildSize = new Vector2(2, 3);
                parent.RelativeChildOffset = new Vector2(.125f, -.25f);
                child.RelativePositionAxes = repeat % 2 == 0 ? Axes.None : Axes.Both;
                child.Position = repeat % 2 == 0 ? new Vector2(17.5f, -9.25f) : new Vector2(.25f, -.125f);
                child.RelativeSizeAxes = repeat % 3 == 0 ? Axes.X : Axes.None;
                child.Size = repeat % 3 == 0 ? new Vector2(.25f, 23) : new Vector2(72, 23);
                child.Margin = new MarginPadding { Left = 2, Right = 7, Top = 3, Bottom = 5 };
                child.Anchor = Anchor.Custom;
                child.RelativeAnchorPosition = new Vector2(.3f, .7f);
                child.Origin = Anchor.Custom;
                child.OriginPosition = new Vector2(14.4f, 13.8f);
                child.Rotation = repeat % 2 == 0 ? 0 : -13;
                child.Shear = repeat % 4 == 0 ? Vector2.Zero : new Vector2(-.15f, .2f);
                child.Scale = repeat % 4 == 0 ? Vector2.One : repeat % 7 == 0 ? Vector2.Zero : new Vector2(-.85f, 1.3f);
                parent.UpdateSubTree();

                // The candidate runs first so the reference cannot prepopulate screen matrices.
                var actual = child.BoundingBox;
                var expected = direct(child);
                var error = boundsError(actual, expected);
                maximumError = Math.Max(maximumError, error);
                comparisons++;
                Assert.That(error, Is.LessThanOrEqualTo(.002f), $"Ordinary case {repeat}: {actual} / {expected}");
            }
            child.UseLocalBounds = true;
            child.Masking = true;
            child.CornerRadius = 4;
            Assert.That(child.BoundingBox, Is.EqualTo(compositeBounds()(child)), "Rounded bounds must keep the composite contract.");
            child.CornerRadius = 0;
            child.Rotation = 0;
            child.Shear = Vector2.Zero;
            child.Scale = Vector2.One;
            child.RelativeSizeAxes = Axes.None;
            child.Size = new Vector2(-20, -10);
            child.Margin = default;
            Assert.That(child.BoundingBox, Is.EqualTo(compositeBounds()(child)), "Negative sizes keep the framework AABB contract.");
            TestContext.WriteLine($"LOCAL_BOUNDS_CONTRACT comparisons={comparisons} maxError={maximumError:R}");
        });
    }

    [Test]
    public void SkinnableBoundsSurviveReplacementReparentingAndBufferIsolation()
    {
        var renderer = new DummyRenderer();
        var textures = new[] { renderer.CreateTexture(72, 23), renderer.CreateTexture(91, 37) };
        var lookup = new SkinComponentLookup<HitResult>(HitResult.Perfect);
        var candidate = new LocalBoundsSkinnable(lookup);
        var reference = new SkinnableDrawable(lookup);
        Container candidateParent = null!;
        Container referenceParent = null!;
        Container pool = null!;
        var skin = new JudgementSkin(() => new LegacyBmsJudgementPiece(HitResult.Perfect, new Sprite { Texture = textures[0] }));
        var provider = new JudgementSkinProvider(skin);
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var frame = 0UL;
        Drawable? previousCandidate = null;
        Drawable? previousReference = null;

        AddStep("load independently skinned wrappers", () =>
        {
            Clear();
            foreach (var wrapper in new[] { candidate, reference })
            {
                wrapper.RelativeSizeAxes = Axes.None;
                wrapper.AutoSizeAxes = Axes.Both;
                wrapper.Anchor = Anchor.TopCentre;
                wrapper.Origin = Anchor.TopCentre;
            }
            provider.Children =
            [
                candidateParent = new Container { Size = new Vector2(640, 480), Child = candidate },
                referenceParent = new Container { Size = new Vector2(640, 480), Child = reference },
                pool = new Container { Size = new Vector2(100, 200), Alpha = 0 },
            ];
            Add(provider);
        });
        AddUntilStep("both skin images loaded", () => candidate.IsLoaded && reference.IsLoaded && candidate.Drawable.IsLoaded && reference.Drawable.IsLoaded);
        AddStep("compare loaded skin and retained draw buffers", compare);
        AddStep("replace active skin with arbitrary composition", () =>
        {
            previousCandidate = candidate.Drawable;
            previousReference = reference.Drawable;
            provider.Replace(new JudgementSkin(() => new Container
            {
                AutoSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 2, Right = 3, Top = 4, Bottom = 5 },
                Rotation = 17,
                Scale = new Vector2(-.85f, 1.3f),
                Child = new Sprite { Texture = textures[1] },
            }));
        });
        AddUntilStep("active skin replacement loaded", () => candidate.Drawable != previousCandidate && reference.Drawable != previousReference
            && candidate.Drawable.IsLoaded && reference.Drawable.IsLoaded);
        AddStep("compare new active skin", compare);
        AddStep("reparent into hidden pool", () =>
        {
            candidateParent.Remove(candidate, false);
            referenceParent.Remove(reference, false);
            pool.AddRange([candidate, reference]);
            pool.UpdateSubTree();
            Assert.That((candidate.DrawSize - reference.DrawSize).Length, Is.LessThanOrEqualTo(.002f));
            previousCandidate = candidate.Drawable;
            previousReference = reference.Drawable;
            provider.Replace(new JudgementSkin(() => new Sprite { Texture = textures[0] }));
        });
        AddStep("reactivate pooled wrappers", () =>
        {
            pool.Remove(candidate, false);
            pool.Remove(reference, false);
            candidateParent.Add(candidate);
            referenceParent.Add(reference);
        });
        AddUntilStep("pooled skin replacement loaded", () => candidate.Drawable != previousCandidate && reference.Drawable != previousReference
            && candidate.Drawable.IsLoaded && reference.Drawable.IsLoaded);
        AddStep("compare reactivated skin and wrapper fallback", () =>
        {
            compare();
            foreach (var wrapper in new[] { candidate, reference })
            {
                wrapper.Scale = new Vector2(-.9f, 1.2f);
                wrapper.Rotation = 13;
            }
            compare();
            Assert.That(candidate.BoundingBox, Is.EqualTo(compositeBounds()(candidate)), "Transformed wrappers must use framework bounds.");
        });

        void compare()
        {
            candidateParent.UpdateSubTree();
            referenceParent.UpdateSubTree();
            Assert.That((candidate.DrawSize - reference.DrawSize).Length, Is.LessThanOrEqualTo(.002f));
            var actualSprite = candidate.Drawable as Sprite ?? candidate.Drawable.ChildrenOfType<Sprite>().Single();
            var expectedSprite = reference.Drawable as Sprite ?? reference.Drawable.ChildrenOfType<Sprite>().Single();
            var nodes = new DrawNode[3];
            var quads = new Quad[3];
            var colours = new DrawColourInfo[3];
            for (var slot = 0; slot < 3; slot++)
            {
                nodes[slot] = generate(actualSprite, ++frame, slot, true)!;
                var expected = generate(expectedSprite, ++frame, slot, true)!;
                quads[slot] = BmsSpriteDrawNodeSnapshot.ReadQuad(nodes[slot]);
                colours[slot] = readColour(nodes[slot]);
                Assert.That(quadError(quads[slot], BmsSpriteDrawNodeSnapshot.ReadQuad(expected)), Is.LessThanOrEqualTo(.002f));
                Assert.That(colourError(colours[slot], readColour(expected)), Is.LessThanOrEqualTo(.0001f));
            }
            var oldPosition = candidate.Position;
            candidate.Position += new Vector2(7, -3);
            candidateParent.UpdateSubTree();
            for (var slot = 0; slot < 3; slot++)
            {
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(nodes[slot]), Is.EqualTo(quads[slot]));
                Assert.That(readColour(nodes[slot]), Is.EqualTo(colours[slot]));
            }
            candidate.Position = oldPosition;
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [Category("JudgementInstrumentation")]
    public void MeasureOwnedOuterBoundsWithFrameworkJudgementChild(bool localOuterBounds, int contentKind)
    {
        var renderer = new DummyRenderer();
        var texture = renderer.CreateTexture(72, 23);
        var alternateTexture = renderer.CreateTexture(91, 37);
        var (candidateContent, candidateSprite) = content();
        var (referenceContent, referenceSprite) = content();
        var candidatePiece = new LegacyBmsJudgementPiece(HitResult.Perfect, candidateContent);
        var referencePiece = new LegacyBmsJudgementPiece(HitResult.Perfect, referenceContent);
        var (candidateRoot, candidateLayers) = createChain(candidatePiece, localOuterBounds);
        var (referenceRoot, referenceLayers) = createChain(referencePiece, false);
        var manualClock = new ManualClock { CurrentTime = 2000, IsRunning = true, Rate = 1 };
        var clock = new FramedClock(manualClock);
        candidateRoot.Clock = referenceRoot.Clock = clock;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var frame = 0UL;
        var candidateEntries = 0;
        var referenceEntries = 0;
        var candidateTransforms = 0;
        var referenceTransforms = 0;
        var maximumError = 0f;

        AddStep("load equivalent judgement chains", () =>
        {
            Clear();
            AddRange([candidateRoot, referenceRoot]);
        });
        AddUntilStep("judgement pieces loaded", () => candidateContent.IsLoaded && referenceContent.IsLoaded);
        AddStep("compare autosize and count matrix entries", () =>
        {
            candidateSprite = candidateContent as Sprite ?? candidateContent.ChildrenOfType<Sprite>().Single();
            referenceSprite = referenceContent as Sprite ?? referenceContent.ChildrenOfType<Sprite>().Single();
            using var probe = new ScopedMethodProbe(typeof(Drawable).GetMethod("computeDrawInfo", BindingFlags.Instance | BindingFlags.NonPublic)!,
                typeof(TestSceneBmsJudgementLocalBounds).GetMethod(nameof(countMatrix), BindingFlags.Static | BindingFlags.NonPublic)!);
            using var transformProbe = new ScopedMethodProbe(typeof(DrawInfo).GetMethod(nameof(DrawInfo.ApplyTransform))!,
                typeof(TestSceneBmsJudgementLocalBounds).GetMethod(nameof(countTransform), BindingFlags.Static | BindingFlags.NonPublic)!);
            candidateRoot.UpdateSubTree();
            referenceRoot.UpdateSubTree();
            var candidateMembers = new HashSet<Drawable>(candidateLayers) { candidateRoot, candidatePiece, candidateContent, candidateSprite };
            var referenceMembers = new HashSet<Drawable>(referenceLayers) { referenceRoot, referencePiece, referenceContent, referenceSprite };
            for (var repeat = 0; repeat < 128; repeat++)
            {
                candidateContent.Scale = referenceContent.Scale = new Vector2(.4f + repeat % 13 / 16f);
                candidateRoot.Position = referenceRoot.Position = new Vector2(137.25f + repeat / 8f, 91.75f - repeat / 4f);
                var actual = capture(candidateRoot, candidateSprite, candidateMembers, ref candidateEntries, ref candidateTransforms);
                var expected = capture(referenceRoot, referenceSprite, referenceMembers, ref referenceEntries, ref referenceTransforms);
                for (var slot = 0; slot < 3; slot++)
                {
                    var error = quadError(actual[slot].Quad, expected[slot].Quad);
                    maximumError = Math.Max(maximumError, error);
                    Assert.That(error, Is.LessThanOrEqualTo(.002f), $"Quad at {repeat}/{slot}");
                    Assert.That(actual[slot].Colour, Is.EqualTo(expected[slot].Colour));
                }
                for (var layer = 0; layer < candidateLayers.Length; layer++)
                    Assert.That((candidateLayers[layer].DrawSize - referenceLayers[layer].DrawSize).Length,
                        Is.LessThanOrEqualTo(.002f), $"Autosize at {repeat}/{layer}");
                Assert.That((candidatePiece.DrawSize - referencePiece.DrawSize).Length, Is.LessThanOrEqualTo(.002f));
            }
            TestContext.WriteLine($"LOCAL_BOUNDS_OUTER local={localOuterBounds} content={contentKind} frames=128 buffers=3 candidateEntries={candidateEntries} referenceEntries={referenceEntries} candidateTransforms={candidateTransforms} referenceTransforms={referenceTransforms} maxQuadError={maximumError:R}");
            if (!localOuterBounds)
            {
                Assert.That(candidateEntries, Is.EqualTo(referenceEntries), "Independent control chains calibrate the counter.");
                Assert.That(candidateTransforms, Is.EqualTo(referenceTransforms));
            }
            else
                Assert.That(candidateEntries, Is.LessThanOrEqualTo(referenceEntries));

            // A second phase exercises playback independently of the work-count experiment.
            var maximumAnimatedError = 0f;
            candidatePiece.PlayAnimation();
            referencePiece.PlayAnimation();
            for (var repeat = 0; repeat < 96; repeat++)
            {
                manualClock.CurrentTime = 2000 + repeat * 2.125;
                clock.ProcessFrame();
                candidateRoot.Rotation = referenceRoot.Rotation = repeat % 2 == 0 ? 0 : 17;
                candidateRoot.Shear = referenceRoot.Shear = new Vector2(.3f, -.2f);
                candidateRoot.Scale = referenceRoot.Scale = new Vector2(-1.2f, .7f);
                candidateRoot.Padding = referenceRoot.Padding = new MarginPadding { Left = 3, Top = 2 };
                for (var layer = 0; layer < candidateLayers.Length; layer++)
                    candidateLayers[layer].Margin = referenceLayers[layer].Margin = new MarginPadding { Left = 1, Right = 2, Top = 3, Bottom = 4 };
                var actual = capture(candidateRoot, candidateSprite, candidateMembers, ref candidateEntries, ref candidateTransforms);
                var expected = capture(referenceRoot, referenceSprite, referenceMembers, ref referenceEntries, ref referenceTransforms);
                for (var slot = 0; slot < 3; slot++)
                {
                    var error = quadError(actual[slot].Quad, expected[slot].Quad);
                    maximumAnimatedError = Math.Max(maximumAnimatedError, error);
                    Assert.That(error, Is.LessThanOrEqualTo(.002f), $"Animated quad at {repeat}/{slot}");
                    Assert.That(colourError(actual[slot].Colour, expected[slot].Colour), Is.LessThanOrEqualTo(.0001f));
                }
            }
            manualClock.CurrentTime = 2010;
            clock.ProcessFrame();
            candidateRoot.UpdateSubTree();
            referenceRoot.UpdateSubTree();
            Assert.That((candidatePiece.DrawSize - referencePiece.DrawSize).Length, Is.LessThanOrEqualTo(.002f), "Rewound autosize");
            foreach (var scale in new[] { Vector2.Zero, new Vector2(0, 1), new Vector2(.000001f), new Vector2(3) })
            {
                candidateRoot.Scale = referenceRoot.Scale = scale;
                var actual = capture(candidateRoot, candidateSprite, candidateMembers, ref candidateEntries, ref candidateTransforms);
                var expected = capture(referenceRoot, referenceSprite, referenceMembers, ref referenceEntries, ref referenceTransforms);
                for (var slot = 0; slot < 3; slot++)
                    Assert.That(quadError(actual[slot].Quad, expected[slot].Quad), Is.LessThanOrEqualTo(.002f), $"Parent scale {scale}/{slot}");
            }
            TestContext.WriteLine($"LOCAL_BOUNDS_PLAYBACK content={contentKind} frames=96 buffers=3 maxQuadError={maximumAnimatedError:R}");
        });

        (Drawable Content, Sprite? Sprite) content()
        {
            if (contentKind == 1)
            {
                var animation = new TextureAnimation();
                animation.AddFrame(texture, 50);
                animation.AddFrame(alternateTexture, 50);
                return (animation, null);
            }
            var sprite = new Sprite { Texture = texture };
            return contentKind == 0 ? (sprite, sprite) : (new Container
            {
                AutoSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 2, Right = 3, Top = 4, Bottom = 5 },
                Child = sprite,
            }, sprite);
        }

        (Quad Quad, DrawColourInfo Colour)[] capture(Container root, Sprite sprite, HashSet<Drawable> members, ref int total, ref int transforms)
        {
            matrixEntries = 0;
            transformApplications = 0;
            measurementThread = Environment.CurrentManagedThreadId;
            measured = members;
            try
            {
                root.UpdateSubTree();
                var result = new (Quad Quad, DrawColourInfo Colour)[3];
                for (var slot = 0; slot < 3; slot++)
                {
                    var node = generate(sprite, ++frame, slot, true)!;
                    result[slot] = (BmsSpriteDrawNodeSnapshot.ReadQuad(node), readColour(node));
                }
                total += matrixEntries;
                transforms += transformApplications;
                return result;
            }
            finally
            {
                measured = null;
            }
        }
    }

    private static (Container Root, LocalBoundsContainer[] Layers) createChain(Drawable piece, bool local)
    {
        var skin = new LocalBoundsContainer
        {
            UseLocalBounds = local,
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Child = piece,
        };
        piece.Anchor = Anchor.Centre;
        piece.Origin = Anchor.Centre;
        var area = new LocalBoundsContainer
        {
            UseLocalBounds = local,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.Centre,
            Child = skin,
        };
        var display = new LocalBoundsContainer
        {
            UseLocalBounds = false,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.Centre,
            Child = area,
        };
        return (new Container { Size = new Vector2(640, 480), Child = display }, [display, area, skin]);
    }

    private static void countMatrix(Drawable __instance)
    {
        if (measured?.Contains(__instance) == true)
            matrixEntries++;
    }

    private static void countTransform()
    {
        if (measured != null && Environment.CurrentManagedThreadId == measurementThread)
            transformApplications++;
    }

    private static Func<Drawable, RectangleF> frameworkBounds()
        => directBounds(typeof(Drawable));

    private static Func<Drawable, RectangleF> compositeBounds()
        => directBounds(typeof(CompositeDrawable));

    private static Func<Drawable, RectangleF> directBounds(Type type)
    {
        var method = new DynamicMethod("FrameworkJudgementBounds", typeof(RectangleF), [typeof(Drawable)], typeof(TestSceneBmsJudgementLocalBounds), true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        if (type != typeof(Drawable)) il.Emit(OpCodes.Castclass, type);
        il.Emit(OpCodes.Call, type.GetProperty(nameof(Drawable.BoundingBox))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<Drawable, RectangleF>>();
    }

    private static float boundsError(RectangleF actual, RectangleF expected)
        => new[] { actual.X - expected.X, actual.Y - expected.Y, actual.Width - expected.Width, actual.Height - expected.Height }
            .Select(value =>
            {
                Assert.That(float.IsFinite(value), Is.True, "Bounds differences must remain finite.");
                return Math.Abs(value);
            }).Max();

    private static float quadError(Quad actual, Quad expected)
        => boundsError(new RectangleF(actual.TopLeft, actual.TopRight), new RectangleF(expected.TopLeft, expected.TopRight))
            + boundsError(new RectangleF(actual.BottomLeft, actual.BottomRight), new RectangleF(expected.BottomLeft, expected.BottomRight));

    private static float colourError(DrawColourInfo actual, DrawColourInfo expected)
    {
        Assert.That(actual.Blending, Is.EqualTo(expected.Blending));
        var a = actual.Colour;
        var b = expected.Colour;
        Assert.That(a.HasSingleColour, Is.EqualTo(b.HasSingleColour));
        var error = new[] { a.TopLeft.ToVector() - b.TopLeft.ToVector(), a.TopRight.ToVector() - b.TopRight.ToVector(),
            a.BottomLeft.ToVector() - b.BottomLeft.ToVector(), a.BottomRight.ToVector() - b.BottomRight.ToVector() }.Max(v => v.Length);
        Assert.That(float.IsFinite(error), Is.True);
        return error;
    }

    private partial class LocalBoundsContainer : Container
    {
        public bool UseLocalBounds = true;

        public override RectangleF BoundingBox
        {
            get
            {
                if (!UseLocalBounds || Parent == null || CornerRadius != 0 || Rotation != 0 || Shear != Vector2.Zero || DrawScale != Vector2.One)
                    return base.BoundingBox;
                var rectangle = LayoutRectangle;
                if (rectangle.Width < 0 || rectangle.Height < 0)
                    return base.BoundingBox;
                return rectangle.Offset(DrawPosition + AnchorPosition + Parent.ChildOffset - OriginPosition);
            }
        }
    }

    private partial class LocalBoundsSkinnable(ISkinComponentLookup lookup) : SkinnableDrawable(lookup, ConfineMode.NoScaling)
    {
        public override RectangleF BoundingBox
        {
            get
            {
                if (Parent == null || CornerRadius != 0 || Rotation != 0 || Shear != Vector2.Zero || DrawScale != Vector2.One)
                    return base.BoundingBox;
                var rectangle = LayoutRectangle;
                if (rectangle.Width < 0 || rectangle.Height < 0)
                    return base.BoundingBox;
                return rectangle.Offset(DrawPosition + AnchorPosition + Parent.ChildOffset - OriginPosition);
            }
        }
    }

    private partial class JudgementSkinProvider(ISkin skin) : SkinProvidingContainer(skin)
    {
        public void Replace(ISkin skin)
        {
            SetSources([skin]);
            TriggerSourceChanged();
        }
    }

    private class JudgementSkin(Func<Drawable> create) : ISkin
    {
        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => lookup is SkinComponentLookup<HitResult> ? create() : null;
        public Texture? GetTexture(string name, WrapMode wrapS, WrapMode wrapT) => null;
        public ISample? GetSample(ISampleInfo sampleInfo) => null;
        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup) where TLookup : notnull where TValue : notnull => null;
    }
}
