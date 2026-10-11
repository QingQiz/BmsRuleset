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
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void ColumnBatchGeometryMatchesFrameworkAcrossLayoutChanges(BmsTestSkins.SkinKind selectedSkin)
    {
        var capture = createColumnGeometryCapture();
        var snapshots = new List<BmsColumnHitObjectContainer.SpriteSnapshot>();
        BmsColumnHitObjectContainer column = null!;
        BmsResolvedNoteSprite sprite = null!;
        DrawableBmsHitObject note = null!;
        var comparisons = 0;
        loadColumnGeometryPlayer(selectedSkin, () =>
        {
            note = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().FirstOrDefault(n => n.HitObject.Column == 2)!;
            if (note?.SingleSpriteForBatch is not { IsLoaded: true } loaded) return false;
            sprite = loaded;
            column = (BmsColumnHitObjectContainer)note.Parent;
            return true;
        });
        AddStep("compare first geometry and standard sprite state", () =>
        {
            note.Alpha = .8f;
            for (var repeat = 0; repeat < 64; repeat++)
            {
                Playfield.Stage.Rotation = repeat / 4f;
                Playfield.Stage.Shear = new Vector2(.3f, -.2f);
                Playfield.Stage.Scale = repeat % 2 == 0 ? new Vector2(-1.2f, .7f) : Vector2.One;
                note.Y = -100.25f - repeat * .125f;
                compare();
            }
            typeof(CompositeDrawable).GetProperty("Padding", BindingFlags.Instance | BindingFlags.Public)!
                .SetValue(note, new MarginPadding { Left = 3, Right = 7, Top = 2, Bottom = 5 });
            compare();
            sprite.Margin = new MarginPadding { Left = 1.25f, Right = 2, Top = 3, Bottom = 4 };
            compare();
            foreach (var anchor in new[] { Anchor.TopLeft, Anchor.Centre, Anchor.BottomRight })
            {
                sprite.Anchor = sprite.Origin = anchor;
                compare();
            }
            sprite.RelativeAnchorPosition = new Vector2(.23f, .71f);
            sprite.OriginPosition = new Vector2(7.125f, -3.25f);
            compare();
            sprite.RelativePositionAxes = Axes.Both;
            sprite.Position = new Vector2(.31f, -.27f);
            compare();
            sprite.RelativePositionAxes = Axes.None;
            sprite.Position = Vector2.Zero;
            foreach (var height in new[] { 0f, -0f, -1f, float.Epsilon, 37f, float.MaxValue })
            {
                sprite.Height = height;
                compare();
            }
            sprite.Height = 12;
            sprite.RelativeSizeAxes = Axes.None;
            foreach (var width in new[] { 0f, -0f, -1f, float.Epsilon, 37f, float.MaxValue })
            {
                sprite.Width = width;
                compare();
            }
            sprite.Size = new Vector2(37, 12);
            sprite.Rotation = 17;
            compare();
            sprite.Shear = new Vector2(.21f, -.13f);
            compare();
            sprite.Scale = new Vector2(-1.2f, .7f);
            compare();
            sprite.Scale = new Vector2(float.Epsilon, -float.Epsilon);
            compare();
            sprite.Rotation = 0;
            sprite.Shear = Vector2.Zero;
            sprite.Scale = Vector2.One;
            note.Colour = ColourInfo.GradientHorizontal(Colour4.Red, Colour4.White);
            compare();
            sprite.Colour = ColourInfo.GradientVertical(Colour4.Cyan, Colour4.Yellow);
            compare();
            sprite.Blending = BlendingParameters.Additive;
            compare();
            note.Y = -100000;
            sprite.UpdateSubTreeMasking();
            Assert.That(capture(column, snapshots), Is.True);
            Assert.That(snapshots, Is.Empty, "Masking must still omit offscreen sprites.");
            note.Y = -100;
            sprite.UpdateSubTreeMasking();
            compare();
            TestContext.WriteLine($"COLUMN_BATCH_GEOMETRY skin={selectedSkin} comparisons={comparisons}");
        });

        void compare()
        {
            // The reference must not warm DrawInfo before the candidate's first quad read.
            sprite.Invalidate(Invalidation.DrawInfo);
            var actualQuad = sprite.ScreenSpaceDrawQuad;
            var expectedQuad = sprite.ToScreenSpace(sprite.DrawRectangle);
            assertColumnQuad(actualQuad, expectedQuad);
            sprite.UpdateSubTreeMasking();
            Assert.That(capture(column, snapshots), Is.True);
            foreach (var snapshot in snapshots)
            {
                using var reference = new SpriteDrawNode(snapshot.Sprite);
                reference.ApplyState();
                assertColumnQuad(snapshot.Quad, BmsSpriteDrawNodeSnapshot.ReadQuad(reference));
                assertColumnColour(snapshot.Colour, snapshot.Sprite.DrawColourInfo);
                var expectedCoords = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(reference, "TextureCoords");
                Assert.That(snapshot.TextureCoords.X, Is.EqualTo(expectedCoords.X));
                Assert.That(snapshot.TextureCoords.Y, Is.EqualTo(expectedCoords.Y));
                Assert.That(snapshot.TextureCoords.Width, Is.EqualTo(expectedCoords.Width));
                Assert.That(snapshot.TextureCoords.Height, Is.EqualTo(expectedCoords.Height));
            }
            comparisons++;
        }
    }

    private void loadColumnGeometryPlayer(BmsTestSkins.SkinKind selectedSkin, Func<bool> findSprite)
    {
        AddStep("load column-geometry player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze column-geometry player", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(4800); });
        AddUntilStep("batch sprite loaded", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001 && findSprite());
    }

    private static Func<BmsColumnHitObjectContainer, List<BmsColumnHitObjectContainer.SpriteSnapshot>, bool> createColumnGeometryCapture()
        => typeof(BmsColumnHitObjectContainer).GetMethod("captureSpriteBatch", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<BmsColumnHitObjectContainer, List<BmsColumnHitObjectContainer.SpriteSnapshot>, bool>>();

    private static void assertColumnQuad(Quad actual, Quad expected)
    {
        Vector2[] actualCorners = [actual.TopLeft, actual.TopRight, actual.BottomLeft, actual.BottomRight];
        Vector2[] expectedCorners = [expected.TopLeft, expected.TopRight, expected.BottomLeft, expected.BottomRight];
        for (var index = 0; index < actualCorners.Length; index++)
        {
            Assert.That(actualCorners[index].X, Is.EqualTo(expectedCorners[index].X));
            Assert.That(actualCorners[index].Y, Is.EqualTo(expectedCorners[index].Y));
        }
    }

    private static void assertColumnColour(DrawColourInfo actual, DrawColourInfo expected)
    {
        Assert.That(actual.Blending, Is.EqualTo(expected.Blending));
        Colour4[] actualCorners = [actual.Colour.TopLeft, actual.Colour.TopRight, actual.Colour.BottomLeft, actual.Colour.BottomRight];
        Colour4[] expectedCorners = [expected.Colour.TopLeft, expected.Colour.TopRight, expected.Colour.BottomLeft, expected.Colour.BottomRight];
        // Gradient interpolation on a zero-height note can produce NaN channels.
        // Compare their classifications as well as ordinary values, rather than struct equality.
        for (var index = 0; index < actualCorners.Length; index++)
        {
            Assert.That(actualCorners[index].R, Is.EqualTo(expectedCorners[index].R));
            Assert.That(actualCorners[index].G, Is.EqualTo(expectedCorners[index].G));
            Assert.That(actualCorners[index].B, Is.EqualTo(expectedCorners[index].B));
            Assert.That(actualCorners[index].A, Is.EqualTo(expectedCorners[index].A));
        }
    }
}
