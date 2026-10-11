#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
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
    [NonParallelizable]
    public void ColumnBatchTextureCoordinatesMatchFrameworkIncludingDegenerateSizes(BmsTestSkins.SkinKind selectedSkin)
        => compareBatchTextureCoordinates(selectedSkin);

    private void compareBatchTextureCoordinates(BmsTestSkins.SkinKind selectedSkin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var capture = typeof(BmsColumnHitObjectContainer).GetMethod("captureSpriteBatch", flags)!
            .CreateDelegate<Func<BmsColumnHitObjectContainer, List<BmsColumnHitObjectContainer.SpriteSnapshot>, bool>>();
        var snapshots = new List<BmsColumnHitObjectContainer.SpriteSnapshot>();
        BmsColumnHitObjectContainer column = null!;
        BmsResolvedNoteSprite sprite = null!;
        DrawableBmsHitObject note = null!;
        var comparisons = 0;
        AddStep("load texture-coordinate player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze texture-coordinate player", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(4800); });
        AddUntilStep("batch sprite loaded", () =>
        {
            note = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().FirstOrDefault(n => n.HitObject.Column == 2)!;
            if (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) >= .001
                || note?.SingleSpriteForBatch is not { IsLoaded: true } loaded) return false;
            sprite = loaded;
            column = (BmsColumnHitObjectContainer)note.Parent;
            return true;
        });
        AddStep("compare batch texture coordinates to standard sprite", () =>
        {
            note.Alpha = .8f;
            Playfield.UpdateSubTreeMasking();
            for (var repeat = 0; repeat < 64; repeat++)
            {
                note.Y = -100.25f - repeat * .125f;
                compare();
            }

            // Both signs of zero, signed finite dimensions and overflow must retain
            // the independent framework formula, including its NaN classifications.
            var height = sprite.Height;
            foreach (var value in new[] { 0f, -0f, -1f, float.Epsilon, 37f, float.MaxValue })
            {
                sprite.Height = value;
                compare();
            }
            sprite.Height = height;
            var width = sprite.Width;
            var axes = sprite.RelativeSizeAxes;
            sprite.RelativeSizeAxes = Axes.None;
            foreach (var value in new[] { 0f, -0f, -1f, float.Epsilon, 37f, float.MaxValue })
            {
                sprite.Width = value;
                compare();
            }
            sprite.RelativeSizeAxes = Axes.Both;
            sprite.Size = new Vector2(float.MaxValue);
            Assert.That(float.IsInfinity(sprite.DrawWidth), Is.True, "The fixture must exercise derived overflow.");
            compare();
            sprite.RelativeSizeAxes = axes;
            sprite.Width = width;
            sprite.Height = height;

            foreach (var textureAxes in new[] { Axes.None, Axes.X, Axes.Y, Axes.Both })
            {
                sprite.TextureRelativeSizeAxes = textureAxes;
                foreach (var rectangle in new[] { new RectangleF(0, 0, 1, 1), new RectangleF(-.125f, .25f, .75f, 1.5f), new RectangleF(0, 0, 0, 1) })
                {
                    sprite.TextureRectangle = rectangle;
                    compare();
                }
            }
            sprite.TextureRectangle = new RectangleF(0, 0, 1, 1);
            var texture = sprite.Texture;
            sprite.Texture = null;
            compare();
            sprite.Texture = texture;
            var adjust = texture.ScaleAdjust;
            texture.ScaleAdjust = 2;
            compare();
            texture.ScaleAdjust = adjust;
            TestContext.WriteLine($"COLUMN_BATCH_TEXTURE_COORDINATES skin={selectedSkin} comparisons={comparisons}");
        });

        void compare()
        {
            // Read the batch first; the reference's lazy state must not prime it.
            Assert.That(capture(column, snapshots), Is.True);
            Assert.That(snapshots, Has.Count.EqualTo(1));
            using var reference = new SpriteDrawNode(sprite);
            reference.ApplyState();
            var expected = BmsSpriteDrawNodeSnapshot.Read<RectangleF>(reference, "TextureCoords");
            var actual = snapshots[0].TextureCoords;
            Assert.That(actual.X, Is.EqualTo(expected.X));
            Assert.That(actual.Y, Is.EqualTo(expected.Y));
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            comparisons++;
        }
    }

}
